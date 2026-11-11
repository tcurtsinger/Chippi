// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.IO;
using Chippi.Core.Config;
using Chippi.Core.Input;
using Chippi.Core.Shortcuts;

namespace Chippi.App.Island;

/// <summary>Shell-owned Peek shortcut. UI calls stay on the dispatcher; the hub only observes releases.</summary>
internal sealed class PeekController : IDisposable
{
    public const string Owner = "peek", ShortcutName = "Peek";
    private readonly ConfigStore _config;
    private readonly IInputHub _hub;
    private readonly Action<Action> _post;
    private readonly Func<string?, bool> _open;
    private readonly Action _close;
    private readonly IHotkey _shortcut;
    private readonly PeekInput _input;
    private IInputRegistration? _registration;
    private KeyShortcut? _registered, _watched;
    private AppSettings _settings = new();
    private bool _active, _disposed;
    private long _press;
    internal bool Active => _active;

    public PeekController(ConfigStore config, IShortcutService shortcuts, IInputHub hub, Action<Action> post, Func<string?, bool> open, Action close)
    {
        _config = config; _hub = hub; _post = post; _open = open; _close = close;
        _input = new(press => _post(() => Released(press)));
        _shortcut = shortcuts.Add(Owner, ShortcutName, new ShortcutEdits(() => Read().PeekShortcut, Change, () => Change(null)) { Icon = "Eye" });
        _config.SectionChanged += OnSectionChanged; _config.Imported += OnImported;
        Apply();
    }

    private AppSettings Read() => _config.GetSection<AppSettings>(AppSettings.SectionName);
    private void OnSectionChanged(object? sender, ConfigSectionChangedEventArgs e) { if (e.Name == AppSettings.SectionName) _post(Apply); }
    private void OnImported(object? sender, EventArgs e) => _post(Apply);

    private void Apply()
    {
        if (_disposed) return;
        var settings = Read();
        if (!Enum.IsDefined(settings.PeekMode)) settings.PeekMode = PeekMode.Hold;
        if (_settings.PeekPage != settings.PeekPage || _settings.PeekMode != settings.PeekMode || _settings.PeekShortcut != settings.PeekShortcut || !settings.IslandEnabled) Cancel();
        _settings = settings;
        var desired = settings.IslandEnabled ? settings.PeekShortcut : null;
        if (_registered != desired)
        {
            if (desired == null) { _shortcut.Unregister(); _registered = null; }
            else if (_shortcut.Register(desired, Pressed)) _registered = desired;
            else { _shortcut.Unregister(); _registered = null; }
        }
        var watch = settings.PeekMode == PeekMode.Hold ? _registered : null;
        if (Watch(watch) is { } error)
        {
            _shortcut.Unregister(); _registered = null; Watch(null);
            Chippi.Core.Diagnostics.Log.Warn(error);
        }
    }

    private string? Watch(KeyShortcut? keys)
    {
        if (keys == _watched) return null;
        if (keys == null) { _registration?.Dispose(); _registration = null; }
        _input.Configure(keys);
        if (keys != null && _registration == null)
        {
            try { _registration = _hub.Add(_input); }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
            { _input.Configure(_watched); return "Peek release tracking failed"; }
        }
        _watched = keys; return null;
    }

    private string? Change(KeyShortcut? keys)
    {
        var before = _registered; var beforeWatch = _watched;
        if (keys != null && !_shortcut.Register(keys, Pressed)) return _shortcut.Error ?? "Shortcut in use";
        Cancel();
        if (keys != null) _registered = keys;
        if (keys != null && _settings.IslandEnabled && _settings.PeekMode == PeekMode.Hold && Watch(keys) is { } error)
        {
            if (before != null) _shortcut.Register(before, Pressed); else _shortcut.Unregister();
            _registered = before; Watch(beforeWatch); return error;
        }
        try { var settings = Read(); settings.PeekShortcut = keys; _config.SetSection(AppSettings.SectionName, settings); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (before != null) _shortcut.Register(before, Pressed); else _shortcut.Unregister();
            _registered = before; Watch(beforeWatch); return "Save failed";
        }
        Apply(); return null;
    }

    private void Pressed()
    {
        if (_disposed || !_settings.IslandEnabled || _registered == null) return;
        if (_settings.PeekMode == PeekMode.Toggle)
        {
            if (_active) Cancel(); else _active = _open(_settings.PeekPage);
            return;
        }
        var current = _input.Current;
        if (!current.Held) return; // The user may have released before WM_HOTKEY reached the UI thread.
        _press = current.Press;
        if (!_active) _active = _open(_settings.PeekPage);
    }

    private void Released(long press) { if (!_disposed && _settings.PeekMode == PeekMode.Hold && _active && _press == press) Cancel(); }
    public void Dismissed() { _active = false; _press = 0; }
    public void Cancel() { if (!_active) return; Dismissed(); _close(); }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; _config.SectionChanged -= OnSectionChanged; _config.Imported -= OnImported;
        Cancel(); _registration?.Dispose(); _shortcut.Dispose();
    }
}
