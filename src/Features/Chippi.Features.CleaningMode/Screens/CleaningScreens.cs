// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Chippi.UI.Themes;

namespace Chippi.Features.CleaningMode.Screens;

/// <summary>
/// The black on every screen (docs/phase-5.5/cleaning-mode.md): one topmost window per monitor, out of Alt+Tab and the
/// taskbar, catching every click; the main one carries <see cref="CleaningFace"/>. It fades in over 200ms, and after an
/// unlock holds 700ms for Chippi's celebration, then fades out over 240ms. Under reduced motion it comes and goes at once.
/// </summary>
public sealed class CleaningScreens : ICleaningScreens
{
    internal static readonly TimeSpan FadeIn = TimeSpan.FromMilliseconds(200);
    internal static readonly TimeSpan Hold = TimeSpan.FromMilliseconds(700);
    internal static readonly TimeSpan FadeOut = TimeSpan.FromMilliseconds(240);

    private readonly List<CleaningWindow> _windows = [];
    private CleaningFace? _face;
    private SessionWatch? _watch;
    private DispatcherTimer? _hold;

    public event Action? UnlockClicked;

    public event Action? Interrupted;

    public void Show()
    {
        var monitors = Native.Monitors();
        if (monitors.Count == 0)
            throw new InvalidOperationException("Cleaning mode failed: no screens");
        int main = Math.Max(0, monitors.FindIndex(m => m.Primary));
        for (int i = 0; i < monitors.Count; i++)
        {
            CleaningFace? face = null;
            if (i == main)
            {
                face = _face = new CleaningFace();
                face.Unlock.Click += (_, _) => UnlockClicked?.Invoke();
            }
            var window = new CleaningWindow(monitors[i].Bounds, face);
            _windows.Add(window);
            window.Show();
        }
        var front = _windows[main];
        front.Activate();
        Native.Foreground(front.Handle);
        _watch = new SessionWatch(front.Handle, () => Interrupted?.Invoke());
        foreach (var window in _windows)
            window.Fade(1, Motion.IsReduced ? TimeSpan.Zero : FadeIn);
    }

    public void Count(int presses, bool counted) => _face?.Count(presses, counted);

    public void Unlock(Action gone)
    {
        _face?.Celebrate();
        _hold = new DispatcherTimer { Interval = Motion.IsReduced ? TimeSpan.Zero : Hold };
        _hold.Tick += (_, _) =>
        {
            _hold?.Stop();
            int left = _windows.Count;
            foreach (var window in _windows.ToList())
                window.Fade(0, Motion.IsReduced ? TimeSpan.Zero : FadeOut, () =>
                {
                    if (--left > 0)
                        return;
                    Close();
                    gone();
                });
        };
        _hold.Start();
    }

    public void Close()
    {
        _hold?.Stop();
        _hold = null;
        _watch?.Dispose();
        _watch = null;
        foreach (var window in _windows)
            window.Close();
        _windows.Clear();
        _face = null;
    }

    /// <summary>One screen's black. Placed in physical pixels, again whenever its DPI changes.</summary>
    private sealed class CleaningWindow : Window
    {
        private readonly Native.Rect _bounds;

        public CleaningWindow(Native.Rect bounds, CleaningFace? face)
        {
            _bounds = bounds;
            Title = "Cleaning mode";
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            AllowsTransparency = true; // for the fades
            Background = Brushes.Black;
            Topmost = true;
            ShowInTaskbar = false;
            ShowActivated = face is not null;
            WindowStartupLocation = WindowStartupLocation.Manual;
            Opacity = 0;
            Content = face;
            SourceInitialized += (_, _) =>
            {
                Handle = new WindowInteropHelper(this).Handle;
                Native.MakeToolWindow(Handle);
                Place();
            };
            DpiChanged += (_, _) => Dispatcher.BeginInvoke(Place);
        }

        public IntPtr Handle { get; private set; }

        private void Place()
        {
            if (Handle != IntPtr.Zero)
                Native.Cover(Handle, _bounds);
        }

        public void Fade(double to, TimeSpan time, Action? done = null)
        {
            if (time <= TimeSpan.Zero)
            {
                BeginAnimation(OpacityProperty, null);
                Opacity = to;
                done?.Invoke();
                return;
            }
            var fade = new DoubleAnimation(to, time) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            if (done is not null)
                fade.Completed += (_, _) => done();
            BeginAnimation(OpacityProperty, fade);
        }
    }

    /// <summary>Hears Windows take over: the session locking, the PC going to sleep, or Ctrl+Alt+Del's secure desktop.</summary>
    private sealed class SessionWatch : IDisposable
    {
        private readonly IntPtr _window;
        private readonly HwndSource? _source;
        private readonly Native.WinEventProc _proc; // kept alive while hooked
        private readonly IntPtr _desktopHook;
        private readonly Action _interrupted;
        private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
        private bool _disposed;

        public SessionWatch(IntPtr window, Action interrupted)
        {
            _window = window;
            _interrupted = interrupted;
            _source = HwndSource.FromHwnd(window);
            _source?.AddHook(Message);
            Native.WatchSession(window);
            _proc = (_, _, _, _, _, _, _) => Raise();
            _desktopHook = Native.WatchDesktopSwitch(_proc);
        }

        private IntPtr Message(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if ((message == Native.SessionChange && wParam.ToInt32() == Native.SessionLock) || (message == Native.PowerBroadcast && wParam.ToInt32() == Native.Suspend))
                Raise();
            return IntPtr.Zero;
        }

        // After the message: the screens close their windows, this one included.
        private void Raise() => _dispatcher.BeginInvoke(() =>
        {
            if (!_disposed)
                _interrupted();
        });

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            if (_desktopHook != IntPtr.Zero)
                Native.Unwatch(_desktopHook);
            Native.UnwatchSession(_window);
            _source?.RemoveHook(Message);
        }
    }
}
