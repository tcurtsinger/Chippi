// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Diagnostics;
using Chippi.Core.Diagnostics;
using Chippi.Core.Input;
using Chippi.Core.Island;
using Chippi.Core.Modules;
using Chippi.UI.Controls;

namespace Chippi.Features.CloseProtection;

public sealed record CloseProtectionSettings
{
    public bool Enabled { get; init; }
    public List<string> ControlWApps { get; init; } = [];
}

// Native observation is separate from the filter. Tests supply this without watching the owner's desktop.
public sealed record CloseApp(string Path, string Name);

public interface ICloseDesktop : IDisposable
{
    CloseTarget? Current { get; }
    CloseModifiers Modifiers();
    event Action? ForegroundChanged;
    event Action? DesktopChanged;
    void Start();
    void Post(Action work);
    void Beep();
    Task<IReadOnlyList<CloseApp>> OpenAppsAsync();
}

public sealed class CloseProtectionModule(Func<ICloseDesktop> createDesktop, TimeProvider? clock = null) : IFeatureModule
{
    public const string Id = "close-protection";
    private const string FeedbackKey = "close-protection/pending";
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private ModuleContext? _context;
    private ICloseDesktop? _desktop;
    private IInputRegistration? _registration;
    private CloseProtectionFilter? _filter;
    private ITimer? _timer;
    private PendingClose? _latest;
    private readonly HashSet<PendingClose> _announced = [];
    private long _run;
    public CloseProtectionSettings Settings { get; private set; } = new();
    public string? Error { get; private set; }
    public event Action? Changed;

    public FeatureDescriptor Descriptor { get; } = new(Id, "Close protection",
        "Protects keyboard close commands from accidental presses", FeatureGroup.Windows)
    { Icon = nameof(LucideIcons.ShieldX), Requires = [Capability.InputHooks], PausesInFullscreen = true, Presets = PresetMembership.PowerUser };

    public Task StartAsync(ModuleContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        _context = context;
        long run = Interlocked.Increment(ref _run);
        Settings = context.Settings.Get<CloseProtectionSettings>();
        _announced.Clear();
        _desktop = createDesktop();
        _filter = new CloseProtectionFilter(() => _desktop.Current, _desktop.Modifiers);
        _filter.Feedback += pending =>
        {
            Volatile.Write(ref _latest, pending);
            _desktop?.Post(() => Feedback(run, pending));
        };
        _desktop.ForegroundChanged += Reset;
        _desktop.DesktopChanged += DesktopReset;
        try { _desktop.Start(); }
        catch { StopAsync(default).GetAwaiter().GetResult(); throw; }
        _filter.Configure(Settings.Enabled, Settings.ControlWApps);
        var input = context.Input ?? throw new InvalidOperationException("Input unavailable");
        input.PauseChanged += OnPauseChanged;
        OnPauseChanged();
        _registration = input.Add(_filter);
        _timer = _clock.CreateTimer(_ => _filter?.Advance(_clock.GetTimestamp()), null,
            Settings.Enabled ? TimeSpan.FromMilliseconds(50) : Timeout.InfiniteTimeSpan, TimeSpan.FromMilliseconds(50));
        context.Ui.SetSettingsPage(_ => new CloseProtectionRows(this));
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<CloseApp>> OpenAppsAsync() => _desktop?.OpenAppsAsync() ?? Task.FromResult<IReadOnlyList<CloseApp>>([]);

    public void SetEnabled(bool enabled) => Save(Settings with { Enabled = enabled });
    public void AddApp(string path)
    {
        if (!System.IO.Path.IsPathFullyQualified(path)) throw new ArgumentException("App path invalid", nameof(path));
        Save(Settings with { ControlWApps = [.. Settings.ControlWApps.Append(path).Distinct(StringComparer.OrdinalIgnoreCase)] });
    }
    public void RemoveApp(string path) => Save(Settings with
    { ControlWApps = [.. Settings.ControlWApps.Where(p => !string.Equals(p, path, StringComparison.OrdinalIgnoreCase))] });

    private void Save(CloseProtectionSettings next)
    {
        if (_context is null || _filter is null) return;
        try
        {
            _context.Settings.Save(next);
            Settings = next;
            _filter.Configure(next.Enabled, next.ControlWApps);
            _registration?.Refresh();
            _timer?.Change(next.Enabled ? TimeSpan.FromMilliseconds(50) : Timeout.InfiniteTimeSpan, TimeSpan.FromMilliseconds(50));
            Error = null;
        }
        catch (Exception ex) { Error = "Close protection failed"; Log.Warn("Close protection: changing settings failed", ex); }
        Changed?.Invoke();
    }

    // Kayla's shared pause supplies every fullscreen, lock and disabled-policy reset.
    public void SetPaused(bool paused) => _filter?.SetPaused(paused);
    private void Reset() => _filter?.Reset();
    private void DesktopReset() => _filter?.Reset();
    private void OnPauseChanged() => _filter?.SetPaused(_context?.Input?.Paused == true);

    // All island and beep work happens after the hook returns, on the UI dispatcher.
    private void Feedback(long run, PendingClose? pending)
    {
        if (run != Volatile.Read(ref _run) || !ReferenceEquals(pending, Volatile.Read(ref _latest)) || _context is null) return;
        if (pending is null)
        {
            _context.Island.Clear(FeedbackKey);
            _announced.Clear();
            return;
        }
        bool first = _announced.Add(pending);
        _announced.RemoveWhere(p => p.Expires <= _clock.GetTimestamp());
        if (_context.CanShowIslandFeedback())
            _context.Island.Publish(new IslandActivity(FeedbackKey, IslandActivityKind.Status,
                "Press again to close", (pending.Chord == CloseChord.AltF4 ? "Alt+F4" : "Ctrl+W") + " · " + pending.Target.Name)
            { IsFeedback = true, Icon = nameof(LucideIcons.ShieldX) });
        else
        {
            _context.Island.Clear(FeedbackKey);
            if (first) _desktop?.Beep();
        }
    }

    public Task StopAsync(CancellationToken ct)
    {
        Interlocked.Increment(ref _run);
        _timer?.Dispose(); _timer = null;
        if (_context?.Input is { } input) input.PauseChanged -= OnPauseChanged;
        _registration?.Dispose(); _registration = null;
        _filter?.Reset(); _filter = null;
        if (_desktop is { } desktop)
        {
            desktop.ForegroundChanged -= Reset;
            desktop.DesktopChanged -= DesktopReset;
            desktop.Dispose();
            _desktop = null;
        }
        _context?.Island.Clear(FeedbackKey);
        _context = null;
        return Task.CompletedTask;
    }
}
