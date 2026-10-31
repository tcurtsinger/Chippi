// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.Core.Island;
using Chippi.Core.Modules;
using Chippi.UI.Controls;
using Microsoft.Extensions.Logging;

namespace Chippi.Features.KeepAwake;

public sealed class KeepAwakeModule : IFeatureModule
{
    private readonly SemaphoreSlim _gate = new(1);
    private readonly IExecutionState _native;
    private AwakeSession? _session;
    private ModuleContext? _context;
    private IIslandTile? _tile;
    private KeepAwakeIslandView? _view;
    private Timer? _tileClock;
    private static readonly TimeSpan TileTick = TimeSpan.FromSeconds(30);
    public KeepAwakeSettings Settings { get; private set; } = new();
    public bool IsOn { get; private set; }

    /// <summary>"Off", "No limit" or "Until 6:00 PM".</summary>
    public string StateLine { get; private set; } = "Off";

    /// <summary>The running session's mode and length, as started; null while off.</summary>
    public KeepAwakeSettings? Active { get; private set; }

    /// <summary>When the running session ends; null while off or with no time limit.</summary>
    public DateTimeOffset? Deadline { get; private set; }
    public string? Error { get; private set; }
    public event Action? Changed;

    public KeepAwakeModule() : this(new WindowsExecutionState()) { }

    public KeepAwakeModule(IExecutionState native)
    {
        _native = native;
    }

    /// <summary>The island tile's key.</summary>
    public const string TileKey = "keep-awake";

    public FeatureDescriptor Descriptor { get; } = new("keep-awake", "Keep awake", "Stops your PC from sleeping", FeatureGroup.System)
    {
        Icon = nameof(LucideIcons.Coffee),
        Presets = PresetMembership.Essentials | PresetMembership.PowerUser,
    };

    public Task StartAsync(ModuleContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        _context = context;
        Settings = context.Settings.Get<KeepAwakeSettings>();
        try { Settings.Validate(); }
        catch (ArgumentException) { Settings = new(); }
        Error = null;
        IsOn = false;
        Active = null;
        Deadline = null;
        StateLine = "Off";
        CreateSession();
        // No status in the page's header: the lit card carries the time left.
        _tile = context.Ui.AddIslandTile(new IslandTileSpec(TileKey, Descriptor.Name, nameof(LucideIcons.Coffee), Order: 40, SectionOrder: 0, () => _view = new KeepAwakeIslandView(this))
        {
            // The home's button starts the last fixed length, or stops.
            Toggle = new IslandTileToggle(() => IsOn, ToggleFromHomeAsync),
            // Esc leaves the Until picker before it closes the island.
            HandleEscape = () => _view?.CloseUntil() == true,
        });
        UpdateTile();
        // Runs only while a timed session counts down ("45 min left"); see OnSessionChanged. The page refreshes with it
        // (Changed), so the lit card and the tile never disagree.
        _tileClock = new Timer(_ =>
        {
            UpdateTile();
            Changed?.Invoke();
        });
        context.Ui.SetSettingsPage(() => new KeepAwakePage(this));
        return Task.CompletedTask;
    }

    public async Task ApplyAsync(KeepAwakeSettings settings, CancellationToken ct = default)
    {
        settings.Validate();
        await _gate.WaitAsync(ct);
        try
        {
            if (_context is null || _session is null) throw new InvalidOperationException("Keep awake uninstalled");
            _context.Settings.Save(settings);
            Settings = settings;
            // The remembered length waits for the next start; only the screen setting reaches a running session.
            if (IsOn) await _session.SetDisplayAsync(settings.KeepDisplayOn);
            Changed?.Invoke();
        }
        finally { _gate.Release(); }
    }

    /// <summary>
    /// Starts the last fixed length used (1 hr the first time), or stops. Never No limit or an Until time, which one
    /// click from the home shouldn't start without saying so.
    /// </summary>
    public Task ToggleAsync(CancellationToken ct = default) =>
        IsOn ? StopSessionAsync(ct) : StartSessionAsync(Settings with { Mode = AwakeMode.Duration }, ct);

    /// <summary>
    /// Starts (or restarts) a session of <paramref name="length"/>'s mode and length, and remembers it: a fixed length as
    /// the one the home's button starts next, an Until time as the picker's next start. The screen follows the Keep screen
    /// on setting.
    /// </summary>
    public async Task StartSessionAsync(KeepAwakeSettings length, CancellationToken ct = default)
    {
        await RunAsync(session => session.StartAsync(length with { KeepDisplayOn = Settings.KeepDisplayOn }), length, ct);
        if (IsOn)
            await RememberAsync(length, ct);
    }

    private async Task RememberAsync(KeepAwakeSettings length, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            // Only what the length sets: No limit keeps the last fixed length and the last Until time.
            var remembered = Settings with
            {
                Mode = length.Mode,
                DurationMinutes = length.Mode == AwakeMode.Duration ? length.DurationMinutes : Settings.DurationMinutes,
                UntilMinute = length.Mode == AwakeMode.UntilTime ? length.UntilMinute : Settings.UntilMinute,
            };
            if (_context is null || remembered == Settings)
                return;
            _context.Settings.Save(remembered);
            Settings = remembered;
        }
        finally { _gate.Release(); }
    }

    public Task StopSessionAsync(CancellationToken ct = default) => RunAsync(session => session.StopAsync(), null, ct);

    private async Task RunAsync(Func<AwakeSession, Task> command, KeepAwakeSettings? active, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (_session is null) throw new InvalidOperationException("Keep awake uninstalled");
            if (_session.HasFailed)
            {
                // Let the failed worker finish releasing its request before a new one starts.
                await _session.DisposeAsync();
                _session.Changed -= OnSessionChanged;
                CreateSession();
            }
            Error = null;
            await command(_session);
            Active = IsOn ? active : null;
            UpdateTile(); // an Until time shows as its clock time, which needs the mode
            Changed?.Invoke();
        }
        finally { _gate.Release(); }
    }

    private void CreateSession()
    {
        _session = new AwakeSession(_native);
        _session.Changed += OnSessionChanged;
    }

    private void OnSessionChanged(bool active, DateTimeOffset? deadline, bool timed, Exception? error)
    {
        Error = error?.Message;
        IsOn = active;
        Deadline = active ? deadline : null;
        if (!active) Active = null;
        StateLine = active ? deadline is { } end ? $"Until {end.LocalDateTime:t}" : "No limit" : "Off";
        if (active)
            _context?.Island.Publish(new IslandActivity("awake", IslandActivityKind.Status, "Keep awake", StateLine) { TileKey = TileKey });
        else _context?.Island.Clear("awake");
        UpdateTile();
        // Twice a minute while a timed session counts down, so its tile is never a minute stale.
        _tileClock?.Change(Deadline is null ? Timeout.InfiniteTimeSpan : TileTick, TileTick);
        if (timed) _context?.Island.ReportSuccess("Keep awake finished");
        if (error is not null) _context?.Logger.LogError(error, "Keep awake execution state failed");
        Changed?.Invoke();
    }

    /// <summary>
    /// The island tile's status, which the bar and the home show: "Off", "45 min left", "Until 6:00 PM" (the time you picked,
    /// not a countdown) or "No limit".
    /// </summary>
    public string TileStatus(DateTimeOffset now) => !IsOn ? "Off"
        : Deadline is not { } end ? "No limit"
        : Active?.Mode == AwakeMode.UntilTime ? $"Until {end.LocalDateTime:t}"
        : $"{KeepAwakeText.Left(end - now)} left";

    private void UpdateTile()
    {
        _tile?.SetStatus(TileStatus(DateTimeOffset.Now));
        _tile?.Refresh();
    }

    // A failure is logged here and passed on: the home says "Failed" under the button.
    private async Task ToggleFromHomeAsync()
    {
        try { await ToggleAsync(); }
        catch (Exception ex) { _context?.Logger.LogWarning(ex, "Toggling Keep awake from the home failed"); throw; }
    }

    /// <summary>Opens this feature's Settings page.</summary>
    public void OpenSettings() => _context?.OpenSettings(null);

    public void ReleaseOnExit()
    {
        // The worker never waits for the dispatcher; this also covers direct Application.Shutdown.
        _session?.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    public async Task StopAsync(CancellationToken ct)
    {
        // Cleanup is intentionally uncancellable: uninstall and exit must release the request.
        await _gate.WaitAsync();
        try
        {
            if (_session is not null)
            {
                await _session.DisposeAsync();
                _session.Changed -= OnSessionChanged;
                _session = null;
            }
            IsOn = false;
            Active = null;
            Deadline = null;
            StateLine = "Off";
            _context?.Island.Clear("awake");
            _context = null;
            _tileClock?.Dispose();
            _tileClock = null;
            _tile = null;
            _view = null;
            Changed?.Invoke();
        }
        finally { _gate.Release(); }
    }
}
