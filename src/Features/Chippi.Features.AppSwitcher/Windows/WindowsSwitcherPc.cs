// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Runtime.InteropServices;
using Chippi.Core.Actions;
using Chippi.Core.Input;

namespace Chippi.Features.AppSwitcher.Windows;

/// <summary>
/// The real windows and the foreground. The list follows Alt+Tab's rules (the same as Quit apps' <c>AltTabApps</c>):
/// visible, not cloaked (which also hides other virtual desktops), its own app, not a tool window, with a title.
/// The focus hand-off is the spike's (task 376, Windows Sandbox, 81 runs): activate the target while the overlay still
/// holds the foreground, then hide the overlay; Esc activates the original window first, as hiding alone handed the
/// keyboard to another Chippi window.
/// </summary>
public sealed partial class WindowsSwitcherPc : ISwitcherPc
{
    private readonly Chippi.UI.Actions.WindowsWindowList _windows;
    private readonly IActionPc? _actions;


    /// <param name="actions">The shell's window moves, which bring a window to the switcher's display; without them, nothing moves.</param>
    public WindowsSwitcherPc(IActionPc? actions = null) : this((uint)Environment.ProcessId, actions)
    {
    }

    /// <param name="self">The process whose windows are left out: Chippi's own, or 0 in tests that list their own windows.</param>
    internal WindowsSwitcherPc(uint self, IActionPc? actions = null)
    {
        _windows = new(self);
        _actions = actions;
    }

    public IReadOnlyList<SwitcherWindow> Windows() => _windows.Read()
        .Select(w => new SwitcherWindow(w.Handle, w.ProcessId, w.App, w.Title, w.Executable, w.Minimized, w.Monitor)).ToArray();
    public nint Foreground() => GetForegroundWindow();

    public nint MonitorOf(nint hwnd) => hwnd == 0 ? 0 : MonitorFromWindow(hwnd, MonitorDefaultToNull);

    public nint PointerMonitor() => GetCursorPos(out var p) ? MonitorFromPoint(p, MonitorDefaultToNearest) : 0;

    public bool IsSame(nint hwnd, uint processId) =>
        IsWindow(hwnd) && GetWindowThreadProcessId(hwnd, out uint pid) != 0 && pid == processId;

    /// <summary>
    /// Gives the overlay the keyboard. A shortcut, an island click or Search leaves Chippi the right to the foreground;
    /// the input hub's doors (the Radial menu, mouse buttons, Caps Lock combos) don't, as the hub swallowed the press.
    /// Then one tap of an unassigned key, marked as Chippi's so the hub passes it, earns the right, and it tries once
    /// more. Never a loop, and never Alt, which can open an app's menu bar.
    /// </summary>
    public bool TakeForeground(nint overlay)
    {
        if (SetForegroundWindow(overlay) && GetForegroundWindow() == overlay)
            return true;
        Tap(Unassigned);
        return SetForegroundWindow(overlay) && GetForegroundWindow() == overlay;
    }

    /// <summary>Snap's Next monitor move (<see cref="IActionPc.MoveToDisplay"/>). An admin window isn't tried: Chippi can't move it.</summary>
    public bool BringTo(SwitcherWindow window, nint monitor)
    {
        if (_actions?.CaptureWindow(window.Handle) is not { Elevated: false } target)
            return false;
        try
        {
            _actions.MoveToDisplay(target, monitor);
            return true;
        }
        catch (ActionFailure)
        {
            return false;
        }
    }

    public bool Activate(SwitcherWindow window)
    {
        if (IsIconic(window.Handle))
            ShowWindow(window.Handle, SwRestore);
        // True also for an app that's busy: the foreground stays empty until it answers, as with Windows' own switch.
        return SetForegroundWindow(window.Handle);
    }

    public void GiveBack(nint original)
    {
        if (original != 0 && IsWindow(original))
            SetForegroundWindow(original);
    }

    /// <summary>VK 0x07 is unassigned: no app acts on it.</summary>
    private const ushort Unassigned = 0x07;

    private static void Tap(ushort vk)
    {
        Input[] inputs =
        [
            new() { Type = 1, Keyboard = new KeyboardInput { Vk = vk, ExtraInfo = ChippiInput.Marker } },
            new() { Type = 1, Keyboard = new KeyboardInput { Vk = vk, Flags = 0x0002, ExtraInfo = ChippiInput.Marker } },
        ];
        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>());
    }


    private const uint MonitorDefaultToNull = 0, MonitorDefaultToNearest = 2;

    private const int SwRestore = 9;

    [StructLayout(LayoutKind.Sequential)]
    private struct Point { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput { public ushort Vk, Scan; public uint Flags, Time; public nint ExtraInfo; }

    // INPUT with the keyboard member; the union's size is the mouse member's, hence the padding.
    [StructLayout(LayoutKind.Explicit, Size = 40)]
    private struct Input { [FieldOffset(0)] public uint Type; [FieldOffset(8)] public KeyboardInput Keyboard; }

    [LibraryImport("user32.dll")] private static partial nint GetForegroundWindow();
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool SetForegroundWindow(nint hwnd);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool ShowWindow(nint hwnd, int cmd);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool IsIconic(nint hwnd);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool IsWindow(nint hwnd);
    [LibraryImport("user32.dll")] private static partial uint GetWindowThreadProcessId(nint hwnd, out uint pid);
    [LibraryImport("user32.dll")] private static partial nint MonitorFromWindow(nint hwnd, uint flags);
    [LibraryImport("user32.dll")] private static partial nint MonitorFromPoint(Point point, uint flags);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool GetCursorPos(out Point point);
    [LibraryImport("user32.dll")] private static partial uint SendInput(uint count, [In] Input[] inputs, int size);
}
