// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using Chippi.Core.Diagnostics;
using Chippi.Core.Modules;
using Chippi.UI.Controls;

namespace Chippi.Features.FocusFollowsMouse;

public sealed record FocusTrackingSettings
{
    public bool Enabled { get; init; }
}

// Registry lifecycle and UI calls are serialized. Construction never accesses Windows.
public sealed class FocusFollowsMouseModule(Func<FocusTrackingEngine> createEngine, Action<Action>? post = null) : IFeatureModule
{
    public const string Id = "focus-follows-mouse";
    private readonly object _gate = new();
    private readonly Action<Action> _post = post ?? (work => Application.Current.Dispatcher.BeginInvoke(work));
    private ModuleContext? _context;
    private FocusTrackingEngine? _engine;
    public FocusTrackingSettings Settings { get; private set; } = new();
    public string? Error { get; private set; }
    public event Action? Changed;

    public FeatureDescriptor Descriptor { get; } = new(Id, "Focus follows mouse",
        "Activates the window under the pointer", FeatureGroup.Windows)
    { Icon = nameof(LucideIcons.Eye), PausesInFullscreen = true };

    public Task StartAsync(ModuleContext context, CancellationToken ct)
    {
        lock (_gate)
        {
            ct.ThrowIfCancellationRequested();
            _context = context;
            Settings = context.Settings.Get<FocusTrackingSettings>();
            context.Ui.SetSettingsPage(_ => new FocusTrackingRows(this));
            context.Ui.AddCommands(() => [new ChippiCommand("focus-follows-mouse/toggle", "Focus follows mouse", nameof(LucideIcons.Eye), () =>
            {
                SetEnabled(!Settings.Enabled);
                if (Error is { } error) throw new InvalidOperationException(error);
                return Task.CompletedTask;
            }) { IsOn = Settings.Enabled, Tile = ActionsTile.Input }]);
            if (context.Input is { } input) input.PauseChanged += OnPauseChanged;
            try
            {
                _engine = createEngine();
                _engine.Recover();
                _engine.SetPaused(_context.Input?.Paused == true);
                _engine.SetEnabled(Settings.Enabled);
                Error = null;
            }
            catch (Exception ex) { Failed(ex); }
            return Task.CompletedTask;
        }
    }

    public void SetEnabled(bool enabled)
    {
        lock (_gate)
        {
            if (_context is null) return;
            try
            {
                // A failed startup (including an unreadable journal) can be retried from this row.
                if (_engine is null)
                {
                    _engine = createEngine();
                    _engine.Recover();
                }
                if (enabled) _engine.SetPaused(_context.Input?.Paused == true);
                var previous = Settings;
                _engine.SetEnabled(enabled);
                try { _context.Settings.Save(Settings with { Enabled = enabled }); }
                catch
                {
                    _engine.SetEnabled(previous.Enabled);
                    throw;
                }
                Settings = Settings with { Enabled = enabled };
                Error = null;
            }
            catch (Exception ex) { Failed(ex); }
            Changed?.Invoke();
        }
    }

    // The hub raises changes on its own thread. Native preferences and UI notifications stay on the UI dispatcher.
    private void OnPauseChanged()
    {
        var context = Volatile.Read(ref _context);
        if (context is null) return;
        _post(() =>
        {
            if (ReferenceEquals(_context, context)) SetPaused(context.Input?.Paused == true);
        });
    }

    public void SetPaused(bool paused)
    {
        lock (_gate)
        {
            if (_engine is null) return;
            try { _engine.SetPaused(paused); Error = null; }
            catch (Exception ex) { Failed(ex); }
            Changed?.Invoke();
        }
    }

    public Task StopAsync(CancellationToken ct)
    {
        lock (_gate)
        {
            if (_context?.Input is { } input) input.PauseChanged -= OnPauseChanged;
            try { _engine?.SetEnabled(false); }
            catch (Exception ex) { Failed(ex); }
            _engine = null;
            _context = null;
            Changed?.Invoke();
            return Task.CompletedTask;
        }
    }

    public Task UninstallAsync(CancellationToken ct)
    {
        lock (_gate)
        {
            try { (_engine ?? createEngine()).Recover(); }
            catch (Exception ex) { Failed(ex); }
            return Task.CompletedTask;
        }
    }

    private void Failed(Exception error)
    {
        Error = "Focus change failed";
        Log.Warn("Focus follows mouse: focus change failed", error);
    }
}
