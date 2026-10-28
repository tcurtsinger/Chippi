// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Chippi.Clipboard.Windows;

internal static unsafe class Native
{
    public const uint CF_DIB = 8, CF_UNICODETEXT = 13, CF_HDROP = 15, CF_LOCALE = 16, CF_DIBV5 = 17;
    public const uint WM_DESTROY = 0x0002, WM_RENDERFORMAT = 0x0305, WM_RENDERALLFORMATS = 0x0306,
        WM_DESTROYCLIPBOARD = 0x0307, WM_CLIPBOARDUPDATE = 0x031D;

    /// <summary>WM_APP: work is queued.</summary>
    public const uint WmWake = 0x8000;

    public const uint GMEM_MOVEABLE = 0x0002;
    private static readonly nint HWND_MESSAGE = -3;

    public delegate nint WndProc(nint hwnd, uint msg, nint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSEX
    {
        public uint cbSize, style;
        public nint lpfnWndProc;
        public int cbClsExtra, cbWndExtra;
        public nint hInstance, hIcon, hCursor, hbrBackground;
        public string? lpszMenuName, lpszClassName;
        public nint hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MSG { public nint hwnd; public uint message; public nint wParam, lParam; public uint time; public int x, y; }

    public static nint CreateMessageWindow(string className, WndProc proc)
    {
        var wc = new WNDCLASSEX
        {
            cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(proc),
            hInstance = GetModuleHandleW(null),
            lpszClassName = className + "-" + Environment.ProcessId,
        };
        if (RegisterClassExW(ref wc) == 0 && Marshal.GetLastPInvokeError() != 1410) // ERROR_CLASS_ALREADY_EXISTS
            throw new Win32Exception();
        nint hwnd = CreateWindowExW(0, wc.lpszClassName!, className, 0, 0, 0, 0, 0, HWND_MESSAGE, 0, wc.hInstance, 0);
        return hwnd != 0 ? hwnd : throw new Win32Exception();
    }

    public static string? ProcessPath(uint pid)
    {
        nint process = OpenProcess(0x1000, false, pid); // PROCESS_QUERY_LIMITED_INFORMATION
        if (process == 0)
            return null;
        try
        {
            char* buffer = stackalloc char[1024];
            uint size = 1024;
            return QueryFullProcessImageNameW(process, 0, buffer, ref size) ? new string(buffer, 0, (int)size) : null;
        }
        finally
        {
            CloseHandle(process);
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern ushort RegisterClassExW(ref WNDCLASSEX wc);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint CreateWindowExW(uint ex, string cls, string name, uint style, int x, int y, int w, int h, nint parent, nint menu, nint instance, nint param);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern nint GetModuleHandleW(string? name);
    [DllImport("user32.dll")] public static extern nint DefWindowProcW(nint hwnd, uint msg, nint wParam, nint lParam);
    [DllImport("user32.dll")] public static extern bool DestroyWindow(nint hwnd);
    [DllImport("user32.dll")] public static extern void PostQuitMessage(int code);
    [DllImport("user32.dll")] public static extern int GetMessageW(out MSG msg, nint hwnd, uint min, uint max);
    [DllImport("user32.dll")] public static extern nint DispatchMessageW(ref MSG msg);
    [DllImport("user32.dll")] public static extern bool PostMessageW(nint hwnd, uint msg, nint wParam, nint lParam);

    [DllImport("user32.dll", SetLastError = true)] public static extern bool AddClipboardFormatListener(nint hwnd);
    [DllImport("user32.dll", SetLastError = true)] public static extern bool RemoveClipboardFormatListener(nint hwnd);
    [DllImport("user32.dll", SetLastError = true)] public static extern bool OpenClipboard(nint hwnd);
    [DllImport("user32.dll", SetLastError = true)] public static extern bool CloseClipboard();
    [DllImport("user32.dll", SetLastError = true)] public static extern bool EmptyClipboard();
    [DllImport("user32.dll", SetLastError = true)] public static extern nint SetClipboardData(uint format, nint data);
    [DllImport("user32.dll", SetLastError = true)] public static extern nint GetClipboardData(uint format);
    [DllImport("user32.dll")] public static extern uint EnumClipboardFormats(uint format);
    [DllImport("user32.dll")] public static extern bool IsClipboardFormatAvailable(uint format);
    [DllImport("user32.dll")] public static extern uint GetClipboardSequenceNumber();
    [DllImport("user32.dll")] public static extern nint GetClipboardOwner();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern uint RegisterClipboardFormatW(string name);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClipboardFormatNameW(uint format, char* name, int max);

    [DllImport("kernel32.dll")] public static extern nint GlobalAlloc(uint flags, nuint bytes);
    [DllImport("kernel32.dll")] public static extern nint GlobalLock(nint handle);
    [DllImport("kernel32.dll")] public static extern bool GlobalUnlock(nint handle);
    [DllImport("kernel32.dll")] public static extern nuint GlobalSize(nint handle);

    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(nint hwnd, out uint pid);
    [DllImport("kernel32.dll", SetLastError = true)] public static extern nint OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("kernel32.dll")] public static extern bool CloseHandle(nint handle);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool QueryFullProcessImageNameW(nint process, uint flags, char* name, ref uint size);
}
