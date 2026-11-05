// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
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
    private readonly uint _self;
    private readonly IActionPc? _actions;
    private readonly ConcurrentDictionary<string, string> _names = new(StringComparer.OrdinalIgnoreCase);

    /// <param name="actions">The shell's window moves, which bring a window to the switcher's display; without them, nothing moves.</param>
    public WindowsSwitcherPc(IActionPc? actions = null) : this((uint)Environment.ProcessId, actions)
    {
    }

    /// <param name="self">The process whose windows are left out: Chippi's own, or 0 in tests that list their own windows.</param>
    internal WindowsSwitcherPc(uint self, IActionPc? actions = null)
    {
        _self = self;
        _actions = actions;
    }

    public IReadOnlyList<SwitcherWindow> Windows()
    {
        nint shell = GetShellWindow();
        var paths = new Dictionary<uint, string?>();
        var found = new List<SwitcherWindow>();
        foreach (nint top in TopLevel())
        {
            if (top == shell || !IsAltTab(top))
                continue;
            GetWindowThreadProcessId(top, out uint pid);
            uint appPid = pid;
            if (ClassIs(top, "ApplicationFrameWindow"))
            {
                // A UWP app's frame: its own window inside names the app. Minimized, it leaves the frame; then the frame's title still does.
                nint content = FindWindowEx(top, 0, "Windows.UI.Core.CoreWindow", null);
                if (content != 0)
                    GetWindowThreadProcessId(content, out appPid);
            }
            if (pid == _self || appPid == _self)
                continue;
            if (!paths.TryGetValue(appPid, out var path))
                paths[appPid] = path = ImagePath(appPid);
            string title = Title(top);
            found.Add(new SwitcherWindow(top, pid, path is null ? title : Name(path), title, path ?? "", IsIconic(top),
                MonitorFromWindow(top, MonitorDefaultToNearest)));
        }
        return found;
    }

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

    private static unsafe bool IsAltTab(nint hwnd)
    {
        if (!IsWindowVisible(hwnd))
            return false;
        int style = GetWindowLong(hwnd, GwlExStyle);
        if ((style & WsExToolWindow) != 0)
            return false;
        if ((style & WsExAppWindow) == 0 && (GetWindow(hwnd, GwOwner) != 0 || (style & WsExNoActivate) != 0))
            return false;
        uint cloaked = 0;
        if (DwmGetWindowAttribute(hwnd, DwmwaCloaked, &cloaked, sizeof(uint)) == 0 && cloaked != 0)
            return false;
        if (ClassIs(hwnd, "Windows.UI.Core.CoreWindow"))
            return false; // Start, Search and the like
        return GetWindowTextLength(hwnd) > 0;
    }

    /// <summary>Every top-level window, in z-order (the front one first).</summary>
    private static unsafe List<nint> TopLevel()
    {
        var windows = new List<nint>();
        var handle = GCHandle.Alloc(windows);
        try
        {
            EnumWindows(&Add, GCHandle.ToIntPtr(handle));
        }
        finally
        {
            handle.Free();
        }
        return windows;
    }

    [UnmanagedCallersOnly]
    private static int Add(nint hwnd, nint list)
    {
        ((List<nint>)GCHandle.FromIntPtr(list).Target!).Add(hwnd);
        return 1;
    }

    private static unsafe bool ClassIs(nint hwnd, string name)
    {
        char* buffer = stackalloc char[64];
        int length = GetClassName(hwnd, buffer, 64);
        return length > 0 && new ReadOnlySpan<char>(buffer, length).SequenceEqual(name);
    }

    private static unsafe string Title(nint hwnd)
    {
        int length = GetWindowTextLength(hwnd);
        if (length <= 0)
            return "";
        var buffer = new char[length + 1];
        fixed (char* text = buffer)
        {
            int read = GetWindowText(hwnd, text, buffer.Length);
            return new string(text, 0, Math.Max(0, read));
        }
    }

    private static unsafe string? ImagePath(uint pid)
    {
        nint process = OpenProcess(ProcessQueryLimitedInformation, false, pid);
        if (process == 0)
            return null;
        try
        {
            char* buffer = stackalloc char[1024];
            uint size = 1024;
            return QueryFullProcessImageName(process, 0, buffer, ref size) ? new string(buffer, 0, (int)size) : null;
        }
        finally
        {
            CloseHandle(process);
        }
    }

    /// <summary>The file's description ("Google Chrome"), else its name without ".exe".</summary>
    private string Name(string path) => _names.GetOrAdd(path, static p =>
    {
        try
        {
            if (FileVersionInfo.GetVersionInfo(p).FileDescription?.Trim() is { Length: > 0 } description)
                return description;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
        return Path.GetFileNameWithoutExtension(p);
    });

    private const int GwlExStyle = -20, WsExToolWindow = 0x80, WsExAppWindow = 0x40000, WsExNoActivate = 0x08000000;
    private const uint GwOwner = 4, DwmwaCloaked = 14, MonitorDefaultToNull = 0, MonitorDefaultToNearest = 2;
    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const int SwRestore = 9;

    [StructLayout(LayoutKind.Sequential)]
    private struct Point { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput { public ushort Vk, Scan; public uint Flags, Time; public nint ExtraInfo; }

    // INPUT with the keyboard member; the union's size is the mouse member's, hence the padding.
    [StructLayout(LayoutKind.Explicit, Size = 40)]
    private struct Input { [FieldOffset(0)] public uint Type; [FieldOffset(8)] public KeyboardInput Keyboard; }

    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static unsafe partial bool EnumWindows(delegate* unmanaged<nint, nint, int> proc, nint data);
    [LibraryImport("user32.dll")] private static partial nint GetShellWindow();
    [LibraryImport("user32.dll")] private static partial nint GetForegroundWindow();
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool SetForegroundWindow(nint hwnd);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool ShowWindow(nint hwnd, int cmd);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool IsIconic(nint hwnd);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool IsWindow(nint hwnd);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool IsWindowVisible(nint hwnd);
    [LibraryImport("user32.dll")] private static partial uint GetWindowThreadProcessId(nint hwnd, out uint pid);
    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongW")] private static partial int GetWindowLong(nint hwnd, int index);
    [LibraryImport("user32.dll")] private static partial nint GetWindow(nint hwnd, uint cmd);
    [LibraryImport("user32.dll", EntryPoint = "GetWindowTextLengthW")] private static partial int GetWindowTextLength(nint hwnd);
    [LibraryImport("user32.dll", EntryPoint = "GetWindowTextW")] private static unsafe partial int GetWindowText(nint hwnd, char* text, int max);
    [LibraryImport("user32.dll", EntryPoint = "GetClassNameW")] private static unsafe partial int GetClassName(nint hwnd, char* name, int max);
    [LibraryImport("user32.dll", EntryPoint = "FindWindowExW", StringMarshalling = StringMarshalling.Utf16)] private static partial nint FindWindowEx(nint parent, nint after, string? cls, string? title);
    [LibraryImport("user32.dll")] private static partial nint MonitorFromWindow(nint hwnd, uint flags);
    [LibraryImport("user32.dll")] private static partial nint MonitorFromPoint(Point point, uint flags);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool GetCursorPos(out Point point);
    [LibraryImport("user32.dll")] private static partial uint SendInput(uint count, [In] Input[] inputs, int size);
    [LibraryImport("dwmapi.dll")] private static unsafe partial int DwmGetWindowAttribute(nint hwnd, uint attribute, void* value, uint size);
    [LibraryImport("kernel32.dll")] private static partial nint OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint pid);
    [LibraryImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool CloseHandle(nint handle);
    [LibraryImport("kernel32.dll", EntryPoint = "QueryFullProcessImageNameW")] [return: MarshalAs(UnmanagedType.Bool)] private static unsafe partial bool QueryFullProcessImageName(nint process, uint flags, char* name, ref uint size);
}
