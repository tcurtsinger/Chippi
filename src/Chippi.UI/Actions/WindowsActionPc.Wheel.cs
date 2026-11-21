// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.Core.Actions;
using Native = Chippi.UI.Actions.ActionNative;

namespace Chippi.UI.Actions;

public sealed partial class WindowsActionPc
{
    public void PostWheel(nint window, int screenX, int screenY, int notches)
    {
        if (window == 0) throw new ActionFailure("No window");
        if (!Native.IsWindow(window)) throw new ActionFailure("Window closed");
        nint wParam, lParam;
        try { wParam = WheelMessage.WParam(notches); lParam = WheelMessage.LParam(screenX, screenY); }
        catch (ArgumentOutOfRangeException) { throw new ActionFailure("Invalid wheel"); }
        if (IsWindowElevated(window)) throw new ActionFailure("Needs admin");
        if (!Native.PostMessage(window, WheelMessage.Message, wParam, lParam)) throw new ActionFailure("Input blocked");
    }

    /// <summary>
    /// Whether the window's process runs at a higher integrity than Chippi, which refuses Chippi's posted input (UIPI): an admin
    /// window for the owner's standard Chippi. In a session where everything runs high (Windows Sandbox), nothing is above.
    /// A window whose process can't be read counts as above, as the other guards do.
    /// </summary>
    public static bool IsWindowElevated(nint window)
    {
        if (Native.GetWindowThreadProcessId(window, out uint pid) == 0) return true;
        nint process = OpenProcess(0x1000, false, pid); // PROCESS_QUERY_LIMITED_INFORMATION, including admin windows
        if (process == 0) return true;
        try { return IntegrityOf(process) > OwnIntegrity.Value; }
        finally { CloseHandle(process); }
    }

    private static readonly Lazy<int> OwnIntegrity = new(() => IntegrityOf(GetCurrentProcess()));

    [System.Runtime.InteropServices.DllImport("kernel32.dll")] private static extern nint GetCurrentProcess();
}
