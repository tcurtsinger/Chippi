// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.ComponentModel;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Chippi.UI.Themes;

namespace Chippi.Features.CleaningMode.Screens;

/// <summary>
/// The black on every screen (docs/phase-5.5/cleaning-mode.md): one topmost window per monitor, out of Alt+Tab and the
/// taskbar, catching every click; the main one carries <see cref="CleaningFace"/> and stays the window in front. It fades
/// in over 200ms, and after an unlock holds 700ms for Chippi's celebration, then fades out over 240ms. Under reduced
/// motion it comes and goes at once. A screen turned off or on meanwhile gets its black again.
/// </summary>
public sealed class CleaningScreens : ICleaningScreens
{
    internal static readonly TimeSpan FadeIn = TimeSpan.FromMilliseconds(200);
    internal static readonly TimeSpan Hold = TimeSpan.FromMilliseconds(700);
    internal static readonly TimeSpan FadeOut = TimeSpan.FromMilliseconds(240);

    private readonly List<CleaningWindow> _windows = [];
    private CleaningWindow? _main;
    private CleaningFace? _face;
    private SessionWatch? _watch;
    private DispatcherTimer? _hold;
    private bool _leaving;

    public event Action? UnlockClicked;

    public event Action? Interrupted;

    public void Show()
    {
        var monitors = Native.Monitors();
        if (monitors.Count == 0)
            throw new InvalidOperationException("Cleaning mode failed: no screens");
        _face = new CleaningFace();
        _face.Unlock.Click += (_, _) => UnlockClicked?.Invoke();
        int main = Main(monitors);
        _main = new CleaningWindow(monitors[main].Bounds, _face);
        _windows.Add(_main);
        _main.Show();
        for (int i = 0; i < monitors.Count; i++)
            if (i != main)
                Add(monitors[i].Bounds);
        _main.Activate();
        Native.Foreground(_main.Handle);
        _watch = new SessionWatch(_main.Handle, () => Interrupted?.Invoke(), Rearrange);
        foreach (var window in _windows)
            window.Fade(1, Motion.IsReduced ? TimeSpan.Zero : FadeIn);
    }

    private static int Main(List<(Native.Rect Bounds, bool Primary)> monitors) => Math.Max(0, monitors.FindIndex(m => m.Primary));

    private CleaningWindow Add(Native.Rect bounds)
    {
        var window = new CleaningWindow(bounds, face: null);
        _windows.Add(window);
        window.Show();
        return window;
    }

    /// <summary>The screens changed (one turned off to wipe it, or back on): the face to the main screen, plain black on the rest.</summary>
    private void Rearrange()
    {
        if (_main is null || _leaving)
            return;
        var monitors = Native.Monitors();
        if (monitors.Count == 0)
            return;
        int main = Main(monitors);
        _main.Bounds = monitors[main].Bounds;
        foreach (var window in _windows.Where(w => w != _main).ToList())
        {
            _windows.Remove(window);
            window.CloseNow();
        }
        for (int i = 0; i < monitors.Count; i++)
            if (i != main)
                Add(monitors[i].Bounds).Fade(1, TimeSpan.Zero);
        Native.Foreground(_main.Handle);
    }

    public void Count(int presses, bool counted) => _face?.Count(presses, counted);

    public void Unlock(Action gone)
    {
        _leaving = true;
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
        _leaving = true;
        _hold?.Stop();
        _hold = null;
        _watch?.Dispose();
        _watch = null;
        foreach (var window in _windows)
            window.CloseNow();
        _windows.Clear();
        _main = null;
        _face = null;
    }

    /// <summary>One screen's black. Placed in physical pixels, again whenever its DPI changes; only Cleaning mode closes it.</summary>
    private sealed class CleaningWindow : Window
    {
        private Native.Rect _bounds;
        private bool _closing;

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
                Native.MakeToolWindow(Handle, noActivate: face is null);
                Place();
            };
            DpiChanged += (_, _) => Dispatcher.BeginInvoke(Place);
        }

        public IntPtr Handle { get; private set; }

        public Native.Rect Bounds
        {
            set
            {
                _bounds = value;
                Place();
            }
        }

        private void Place()
        {
            if (Handle != IntPtr.Zero)
                Native.Cover(Handle, _bounds);
        }

        public void CloseNow()
        {
            _closing = true;
            Close();
        }

        // Alt+F4 and the like: the keyboard is the lock's, but nothing else closes the black either.
        protected override void OnClosing(CancelEventArgs e)
        {
            e.Cancel = !_closing;
            base.OnClosing(e);
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

    /// <summary>
    /// Hears Windows take over (the session locking, the PC going to sleep, Ctrl+Alt+Del's secure desktop) and the screens
    /// changing, on the main window.
    /// </summary>
    private sealed class SessionWatch : IDisposable
    {
        private readonly IntPtr _window;
        private readonly HwndSource? _source;
        private readonly Native.WinEventProc _proc; // kept alive while hooked
        private readonly IntPtr _desktopHook;
        private readonly Action _interrupted;
        private readonly Action _screensChanged;
        private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
        private bool _disposed;

        public SessionWatch(IntPtr window, Action interrupted, Action screensChanged)
        {
            _window = window;
            _interrupted = interrupted;
            _screensChanged = screensChanged;
            _source = HwndSource.FromHwnd(window);
            _source?.AddHook(Message);
            Native.WatchSession(window);
            _proc = (_, _, _, _, _, _, _) => Later(_interrupted);
            _desktopHook = Native.WatchDesktopSwitch(_proc);
        }

        private IntPtr Message(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if ((message == Native.SessionChange && wParam.ToInt32() == Native.SessionLock) || (message == Native.PowerBroadcast && wParam.ToInt32() == Native.Suspend))
                Later(_interrupted);
            else if (message == Native.DisplayChange)
                Later(_screensChanged);
            return IntPtr.Zero;
        }

        // After the message: the screens close or move their windows, this one included.
        private void Later(Action action) => _dispatcher.BeginInvoke(() =>
        {
            if (!_disposed)
                action();
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
