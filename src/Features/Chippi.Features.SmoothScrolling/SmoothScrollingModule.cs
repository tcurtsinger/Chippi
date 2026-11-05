// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Diagnostics;
using System.IO;
using System.Windows;
using Chippi.Core.Diagnostics;
using Chippi.Core.Input;
using Chippi.Core.Modules;
using Chippi.UI.Controls;
using Microsoft.Win32;

namespace Chippi.Features.SmoothScrolling;

/// <summary>An app's own scroll speed (<see cref="Percent"/> of the usual, or 0 to leave the app alone). Matched by exe name.</summary>
public sealed record AppScroll(string App, int Percent);

/// <summary>Smooth and Linear are separate and both start off, even right after install (task 226).</summary>
public sealed record SmoothScrollingSettings
{
    public const int DistanceMin = 25, DistanceMax = 300, ResponseMin = 50, ResponseMax = 400, CoastMax = 400;

    public bool SmoothEnabled { get; init; }

    public bool LinearEnabled { get; init; }

    /// <summary>Of Windows' own distance per notch.</summary>
    public int DistancePercent { get; init; } = 100;

    /// <summary>How long a glide takes to settle.</summary>
    public int ResponseMs { get; init; } = 150;

    /// <summary>Extra time the glide keeps going after the wheel stops.</summary>
    public int CoastMs { get; init; }

    public AppScroll[] Apps { get; init; } = [];
}

/// <summary>
/// Smooth and linear wheel scrolling: one filter in the input hub, in only while a switch is on. The hub pauses it in a
/// fullscreen app; a pause, a lock or turning it off drops what is still gliding.
/// </summary>
public sealed class SmoothScrollingModule : IFeatureModule
{
    public const string Id = "smooth-scrolling";
    public const string MouseSection = "mouse";

    private readonly object _gate = new();
    private readonly bool _includeInjected;
    private ModuleContext? _context;
    private ScrollFilter? _filter;
    private IInputRegistration? _registration;
    private WindowsScrollEnvironment? _env;

    /// <param name="includeInjected">Only the Sandbox probe: it sends input with <c>SendInput</c>, which Windows marks injected.</param>
    public SmoothScrollingModule(bool includeInjected = false) => _includeInjected = includeInjected;

    public SmoothScrollingSettings Settings { get; private set; } = new();

    /// <summary>"Watching the mouse failed" while the hook is down, else null.</summary>
    public string? Error { get; private set; }

    public event Action? Changed;

    public FeatureDescriptor Descriptor { get; } = new(Id, "Smooth scrolling",
        "Smooth or linear wheel scrolling for mice that step", FeatureGroup.Windows)
    { Icon = nameof(LucideIcons.Scroll), Requires = [Capability.InputHooks], PausesInFullscreen = true };

    /// <summary>Lines per notch in Windows' own mouse setting, for the Distance row's caption.</summary>
    public static int WindowsLinesPerNotch()
    {
        try { return SystemParameters.WheelScrollLines; }
        catch { return 3; }
    }

    public Task StartAsync(ModuleContext context, CancellationToken ct)
    {
        lock (_gate)
        {
            _context = context;
            Settings = Normalize(context.Settings.Get<SmoothScrollingSettings>());
            context.Ui.SetSettingsPage(_ => new SmoothScrollingRows(this));
            // Both switches, as rows of Actions' Input menu and in Search.
            context.Ui.AddCommands(() =>
            [
                new ChippiCommand("smooth-scrolling/smooth", "Smooth scrolling", nameof(LucideIcons.Scroll),
                    () => Flip(linear: false))
                { Words = "scroll wheel glide", IsOn = Settings.SmoothEnabled, Tile = ActionsTile.Input },
                new ChippiCommand("smooth-scrolling/linear", "Linear scrolling", nameof(LucideIcons.MoveVertical),
                    () => Flip(linear: true))
                { Words = "scroll wheel acceleration steady", IsOn = Settings.LinearEnabled, Tile = ActionsTile.Input },
            ]);
            if (context.Input is { } hub)
                hub.PauseChanged += OnPause;
            SystemEvents.SessionSwitch += OnSession;
            Apply();
        }
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct)
    {
        lock (_gate)
        {
            if (_context?.Input is { } hub)
                hub.PauseChanged -= OnPause;
            SystemEvents.SessionSwitch -= OnSession;
            Teardown();
            _context = null;
        }
        return Task.CompletedTask;
    }

    public void Update(Func<SmoothScrollingSettings, SmoothScrollingSettings> change)
    {
        lock (_gate)
        {
            if (_context is null)
                return;
            var next = Normalize(change(Settings));
            _context.Settings.Save(next);
            Settings = next;
            Apply();
        }
        Changed?.Invoke();
    }

    private Task Flip(bool linear)
    {
        Update(s => linear ? s with { LinearEnabled = !s.LinearEnabled } : s with { SmoothEnabled = !s.SmoothEnabled });
        return Task.CompletedTask;
    }

    private void OnPause() => _filter?.Cancel();

    private void OnSession(object? sender, SessionSwitchEventArgs e) => _filter?.Cancel();

    private static SmoothScrollingSettings Normalize(SmoothScrollingSettings s) => s with
    {
        DistancePercent = Math.Clamp(s.DistancePercent, SmoothScrollingSettings.DistanceMin, SmoothScrollingSettings.DistanceMax),
        ResponseMs = Math.Clamp(s.ResponseMs, SmoothScrollingSettings.ResponseMin, SmoothScrollingSettings.ResponseMax),
        CoastMs = Math.Clamp(s.CoastMs, 0, SmoothScrollingSettings.CoastMax),
        Apps = [.. (s.Apps ?? []).Where(a => !string.IsNullOrWhiteSpace(a.App))
            .Select(a => a with { Percent = a.Percent == 0 ? 0 : Math.Clamp(a.Percent, SmoothScrollingSettings.DistanceMin, 400) })
            .GroupBy(a => Path.GetFileName(a.App), StringComparer.OrdinalIgnoreCase).Select(g => g.Last())],
    };

    private void Apply()
    {
        var hub = _context?.Input;
        if (hub is null)
            return;
        if (!Settings.SmoothEnabled && !Settings.LinearEnabled)
        {
            Teardown();
            Error = null;
            return;
        }
        if (_registration is null)
        {
            try
            {
                _env = new WindowsScrollEnvironment();
                _filter = new ScrollFilter(_env, () => hub.Paused) { IncludeInjected = _includeInjected };
                _env.Start(_filter.Tick);
                _registration = hub.Add(_filter);
                Error = null;
            }
            catch (Exception ex)
            {
                Log.Warn("Smooth scrolling: the mouse hook failed", ex);
                Teardown();
                Error = "Watching the mouse failed";
            }
        }
        if (_filter is not null)
            _filter.Tuning = new ScrollTuning(Settings.SmoothEnabled, Settings.LinearEnabled, Settings.DistancePercent, Settings.ResponseMs, Settings.CoastMs, Settings.Apps);
    }

    private void Teardown()
    {
        _registration?.Dispose();
        _registration = null;
        _filter?.Dispose();
        _filter = null;
        _env?.Dispose();
        _env = null;
    }
}
