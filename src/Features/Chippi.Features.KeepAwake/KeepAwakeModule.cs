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
    public KeepAwakeSettings Settings { get; private set; } = new();
    public bool IsOn { get; private set; }

    /// <summary>"Off", "Indefinitely" or "Until 6:00 PM".</summary>
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
        Badges = [FeatureBadge.Overlap("Overlaps PowerToys")],
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
        context.Ui.AddFlyoutPanel(Descriptor.Name, nameof(LucideIcons.Coffee), () => new KeepAwakePanel(this));
        context.Ui.AddIslandTile(new IslandTileSpec(TileKey, Descriptor.Name, nameof(LucideIcons.Coffee), Order: 40, SectionOrder: 0, () => new KeepAwakeIslandView(this))
        {
            Orbit = IslandOrbit.Left,
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
            // The default length waits for the next start; only the screen setting reaches a running session.
            if (IsOn) await _session.SetDisplayAsync(settings.KeepDisplayOn);
            Changed?.Invoke();
        }
        finally { _gate.Release(); }
    }

    /// <summary>Starts the default length, or stops.</summary>
    public Task ToggleAsync(CancellationToken ct = default) => IsOn ? StopSessionAsync(ct) : StartSessionAsync(Settings, ct);

    /// <summary>
    /// Starts (or restarts) a session of <paramref name="length"/>'s mode and length, without changing
    /// the default. The screen follows the Keep screen on setting.
    /// </summary>
    public Task StartSessionAsync(KeepAwakeSettings length, CancellationToken ct = default) =>
        RunAsync(session => session.StartAsync(length with { KeepDisplayOn = Settings.KeepDisplayOn }), length, ct);

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
        StateLine = active ? deadline is { } end ? $"Until {end.LocalDateTime:t}" : "Indefinitely" : "Off";
        if (active)
            _context?.Island.Publish(new IslandActivity("awake", IslandActivityKind.Status, "Keep awake", StateLine));
        else _context?.Island.Clear("awake");
        if (timed) _context?.Island.ReportSuccess("Keep awake finished");
        if (error is not null) _context?.Logger.LogError(error, "Keep awake execution state failed");
        Changed?.Invoke();
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
            Changed?.Invoke();
        }
        finally { _gate.Release(); }
    }
}
