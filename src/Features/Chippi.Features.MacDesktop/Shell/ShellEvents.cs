// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows.Interop;
using System.Windows.Threading;

namespace Chippi.Features.MacDesktop.Shell;

/// <summary>
/// What the desktop tells every top-level window: Explorer restarted (<c>TaskbarCreated</c>), the wallpaper or a setting
/// changed, displays came or went. A hidden top-level window, since a message-only window hears no broadcasts. Raised on
/// its (the UI) thread, a burst coalesced into one.
/// </summary>
internal sealed class ShellEvents : IDisposable
{
    private const uint SpiSetDeskWallpaper = 0x0014;
    private static readonly uint TaskbarCreated = Native.RegisterWindowMessage("TaskbarCreated");
    private readonly HwndSource _source;
    private readonly DispatcherTimer _settle;
    private bool _explorer, _wallpaper, _displays, _workArea;

    public event Action? ExplorerRestarted;
    public event Action? WallpaperChanged;
    public event Action? DisplaysChanged;

    public ShellEvents()
    {
        _source = new HwndSource(new HwndSourceParameters("Chippi.MacDesktop.Shell")
        {
            WindowStyle = unchecked((int)0x80000000), // WS_POPUP, never shown
            ExtendedWindowStyle = (int)Native.WsExToolWindow,
            Width = 0,
            Height = 0,
        });
        _source.AddHook(Hook);
        _settle = new DispatcherTimer(TimeSpan.FromMilliseconds(250), DispatcherPriority.Background, (_, _) => Raise(), _source.Dispatcher);
        _settle.Stop();
    }

    private nint Hook(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        uint message = (uint)msg;
        if (message == TaskbarCreated)
            _explorer = true;
        else if (message == Native.WmSettingChange && (uint)wParam == SpiSetDeskWallpaper)
            _wallpaper = true;
        else if (message == Native.WmSettingChange && (uint)wParam == 0x002F) // SPI_SETWORKAREA: another bar moved
            _workArea = true;
        else if (message is Native.WmDisplayChange or Native.WmDpiChanged)
            _displays = true;
        else
            return 0;
        _settle.Stop();
        _settle.Start();
        return 0;
    }

    private void Raise()
    {
        _settle.Stop();
        bool explorer = _explorer, wallpaper = _wallpaper, displays = _displays, workArea = _workArea;
        _explorer = _wallpaper = _displays = _workArea = false;
        if (explorer) ExplorerRestarted?.Invoke();
        if (displays || workArea) DisplaysChanged?.Invoke();
        if (wallpaper || displays) WallpaperChanged?.Invoke();
    }

    public void Dispose()
    {
        _settle.Stop();
        _source.RemoveHook(Hook);
        _source.Dispose();
    }
}
