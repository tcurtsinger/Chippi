// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.Core.Actions;
using Chippi.Core.Input;
using Chippi.Core.Modules;
using Chippi.Core.Shortcuts;

namespace Chippi.Features.AppSwitcher.Tests;

/// <summary>Windows and the foreground in memory; <see cref="Calls"/> records the order of what the switcher asked.</summary>
internal sealed class FakePc : ISwitcherPc
{
    public List<SwitcherWindow> Open = [];
    public nint Front;
    public bool RefuseOverlay, RefuseSwitch;
    public readonly List<string> Calls = [];
    public readonly HashSet<nint> Closed = [];

    public IReadOnlyList<SwitcherWindow> Windows() => [.. Open.Where(w => !Closed.Contains(w.Handle))];
    public nint Foreground() => Front;
    public nint MonitorOf(nint hwnd) => Open.FirstOrDefault(w => w.Handle == hwnd)?.Monitor ?? 0;
    public nint PointerMonitor() => 2;
    public bool IsSame(nint hwnd, uint processId) => !Closed.Contains(hwnd) && Open.Any(w => w.Handle == hwnd && w.ProcessId == processId);

    public bool TakeForeground(nint overlay)
    {
        Calls.Add($"take {overlay}");
        if (RefuseOverlay)
            return false;
        Front = overlay;
        return true;
    }

    public bool Activate(SwitcherWindow window)
    {
        Calls.Add($"activate {window.Handle}");
        if (RefuseSwitch)
            return false;
        Front = window.Handle;
        return true;
    }

    public void GiveBack(nint original)
    {
        Calls.Add($"give back {original}");
        Front = original;
    }
}

internal sealed class FakeView(FakePc pc) : ISwitcherView
{
    public const nint Handle = 900;
    public SwitcherSession? Shown;
    public nint Monitor;
    public SwitcherWindow? Failed;
    public int Shows;

    public nint Show(SwitcherSession session, nint monitor)
    {
        Shows++;
        Shown = session;
        Monitor = monitor;
        Failed = null;
        pc.Calls.Add("show");
        return Handle;
    }

    public void ShowFailed(SwitcherWindow window) => Failed = window;

    public void Hide()
    {
        Shown = null;
        pc.Calls.Add("hide");
    }
}

internal sealed class FakeEvents : IWindowEvents
{
    public event Action<nint>? Foreground;
    public event Action<nint>? Destroyed;
    public bool Disposed;
    public bool WatchingClosed => Destroyed is not null;
    public void RaiseForeground(nint hwnd) => Foreground?.Invoke(hwnd);
    public void RaiseDestroyed(nint hwnd) => Destroyed?.Invoke(hwnd);
    public void Dispose() => Disposed = true;
}

internal sealed class SettingsStore : IModuleSettings
{
    public AppSwitcherSettings Value = new();

    public T Get<T>() where T : class, new() => (T)(object)Value;

    public void Save<T>(T value) where T : class => Value = (AppSwitcherSettings)(object)value;
}

internal sealed class Ui : IModuleUi
{
    public Func<IReadOnlyList<ChippiCommand>>? Commands;
    public IIslandTile AddIslandTile(IslandTileSpec spec) => throw new InvalidOperationException("No home tile");
    public void SetSettingsPage(Func<string?, object> page) { }
    public void AddCommands(Func<IReadOnlyList<ChippiCommand>> commands) => Commands = commands;
}

internal sealed class Hotkey : IHotkey
{
    public bool Available = true, Disposed;
    public KeyShortcut? Registered;
    public Action? Pressed;

    public bool Register(KeyShortcut shortcut, Action pressed)
    {
        if (!Available)
            return false;
        Registered = shortcut;
        Pressed = pressed;
        return true;
    }

    public void Unregister()
    {
        Registered = null;
        Pressed = null;
    }

    public void Dispose()
    {
        Disposed = true;
        Pressed = null;
    }
}

internal sealed class Hub : IInputHub
{
    public bool Paused { get; set; }
    public bool FullscreenInFront => Paused;
    public event Action? PauseChanged;
    public IInputRegistration Add(IInputFilter filter) => throw new InvalidOperationException("The switcher adds no filter");
    public void Refresh() { }
    public void HoldModifiers(uint heldBy, ReadOnlySpan<ushort> modifiers) { }
    public void Pause(bool on)
    {
        Paused = on;
        PauseChanged?.Invoke();
    }
}

internal sealed class ActionPc : IActionPc
{
    public nint InFront;
    public bool? HasApp(string parsingName) => null;
    public bool Exists(string path) => false;
    public nint WindowUnderPointer() => 0;
    public nint WindowInFront() => InFront;
    public void Open(string target) { }
    public void Window(nint window, WindowAction action) { }
    public void Media(MediaAction action) { }
    public Task PressAsync(KeyCombo keys) => Task.CompletedTask;
    public Task TypeAsync(string text) => Task.CompletedTask;
    public void Run(CommandShell shell, string line) { }
    public Task<(bool Terminal, string? Text)> TerminalSelectionAsync() => Task.FromResult((false, (string?)null));
    public IDisposable Record(Action<KeyCombo?> recorded) => throw new InvalidOperationException();
}
