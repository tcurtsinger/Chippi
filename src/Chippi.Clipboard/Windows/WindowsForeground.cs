// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Runtime.InteropServices;
using Chippi.Core.Clipboard;

namespace Chippi.Clipboard.Windows;

/// <summary>
/// The foreground window and the keyboard (architecture §5, measured in Windows Sandbox): SendInput for
/// Ctrl+V, the integrity check that UIPI needs, and SetForegroundWindow, which Windows allows right after
/// Chippi's own shortcut (WM_HOTKEY) or a click in the island.
/// </summary>
public sealed class WindowsForeground : IForeground
{
    private static readonly TimeSpan Poll = TimeSpan.FromMilliseconds(10);
    private readonly uint _self = (uint)Environment.ProcessId;
    private readonly Lazy<int?> _selfLevel = new(() => Integrity((uint)Environment.ProcessId));

    public PasteTarget? Current()
    {
        nint hwnd = GetForegroundWindow();
        if (hwnd == 0 || GetWindowThreadProcessId(hwnd, out uint pid) == 0 || pid == _self || IsTaskbar(hwnd))
            return null;
        return new PasteTarget(hwnd, pid);
    }

    // The taskbar is in front right after a tray click: nothing to paste into.
    private static unsafe bool IsTaskbar(nint hwnd)
    {
        char* name = stackalloc char[32];
        int length = GetClassNameW(hwnd, name, 32);
        var cls = new ReadOnlySpan<char>(name, Math.Max(0, length));
        return cls.SequenceEqual("Shell_TrayWnd") || cls.SequenceEqual("Shell_SecondaryTrayWnd");
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern unsafe int GetClassNameW(nint hwnd, char* name, int max);

    public bool IsAlive(PasteTarget target) =>
        IsWindow(target.Window) && GetWindowThreadProcessId(target.Window, out uint pid) != 0 && pid == target.ProcessId;

    public bool CanSendInput(PasteTarget target) =>
        Integrity(target.ProcessId) is { } level && _selfLevel.Value is { } self && level <= self;

    public async Task<bool> BringToFrontAsync(PasteTarget target, TimeSpan timeout, CancellationToken cancel = default)
    {
        if (!IsAlive(target))
            return false;
        if (GetForegroundWindow() != target.Window)
            SetForegroundWindow(target.Window);
        var until = DateTime.UtcNow + timeout;
        while (GetForegroundWindow() != target.Window)
        {
            if (DateTime.UtcNow >= until)
                return false;
            await Task.Delay(Poll, cancel).ConfigureAwait(false);
        }
        return true;
    }

    public async Task WaitForModifiersReleasedAsync(TimeSpan timeout, CancellationToken cancel = default)
    {
        var until = DateTime.UtcNow + timeout;
        while (Held(0x11) || Held(0x10) || Held(0x12) || Held(0x5B) || Held(0x5C))
        {
            if (DateTime.UtcNow >= until)
                return;
            await Task.Delay(Poll, cancel).ConfigureAwait(false);
        }

        static bool Held(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;
    }

    public unsafe void SendPaste()
    {
        var inputs = stackalloc Input[4];
        inputs[0] = Key(0x11, up: false);
        inputs[1] = Key(0x56, up: false);
        inputs[2] = Key(0x56, up: true);
        inputs[3] = Key(0x11, up: true);
        SendInput(4, inputs, sizeof(Input));
    }

    public void Beep() => MessageBeep(0);

    /// <summary>The process's integrity RID (0x2000 medium, 0x3000 high), or null when its token can't be read.</summary>
    private static unsafe int? Integrity(uint pid)
    {
        nint process = OpenProcess(0x1000, false, pid); // PROCESS_QUERY_LIMITED_INFORMATION
        if (process == 0)
            return null;
        try
        {
            if (!OpenProcessToken(process, 0x0008, out nint token)) // TOKEN_QUERY
                return null;
            try
            {
                byte* buffer = stackalloc byte[128];
                if (!GetTokenInformation(token, 25, buffer, 128, out _)) // TokenIntegrityLevel
                    return null;
                nint sid = *(nint*)buffer;
                byte count = *GetSidSubAuthorityCount(sid);
                return count == 0 ? null : *GetSidSubAuthority(sid, (uint)(count - 1));
            }
            finally
            {
                CloseHandle(token);
            }
        }
        finally
        {
            CloseHandle(process);
        }
    }

    private static Input Key(ushort vk, bool up) => new() { Type = 1, Keyboard = new KeyboardInput { Vk = vk, Flags = up ? 2u : 0u } };

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput { public ushort Vk, Scan; public uint Flags, Time; public nint Extra; }

    // INPUT is a type tag plus a union sized by MOUSEINPUT (the largest member); pad to match.
    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type;
        public KeyboardInput Keyboard;
        private readonly long _pad;
    }

    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint hwnd);
    [DllImport("user32.dll")] private static extern bool IsWindow(nint hwnd);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint hwnd, out uint pid);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int vk);
    [DllImport("user32.dll", SetLastError = true)] private static extern unsafe uint SendInput(uint count, Input* inputs, int size);
    [DllImport("user32.dll")] private static extern bool MessageBeep(uint type);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern nint OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(nint handle);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool OpenProcessToken(nint process, uint access, out nint token);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern unsafe bool GetTokenInformation(nint token, int cls, byte* info, int length, out int needed);
    [DllImport("advapi32.dll")] private static extern unsafe byte* GetSidSubAuthorityCount(nint sid);
    [DllImport("advapi32.dll")] private static extern unsafe int* GetSidSubAuthority(nint sid, uint index);
}
