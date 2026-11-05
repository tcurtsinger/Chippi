// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using Chippi.Features.AppSwitcher.Windows;

namespace Chippi.Features.AppSwitcher.Views;

/// <summary>
/// The App switcher's window: a topmost window out of Alt+Tab that shows without activating, sends keys to the switcher
/// and closes when it loses the foreground, drawing <see cref="SwitcherBoard"/>. Each card in view gets the window's live
/// preview from Windows (DWM), fitted inside its slot; cards out of view have none. It stays an ordinary window, not a
/// layered one, so the previews draw, and Windows rounds its corners.
/// </summary>
public sealed partial class SwitcherOverlay : Window, ISwitcherView
{
    private readonly Switcher _switcher;
    private readonly AppSwitcherModule? _module;
    private readonly SwitcherBoard _board;
    private readonly Dictionary<nint, WindowPreview> _previews = [];
    private SwitcherSession? _session;
    private nint _hwnd;
    private nint _monitor;
    private bool _placing;

    public SwitcherOverlay(Switcher switcher, AppSwitcherModule? module = null)
    {
        _switcher = switcher;
        _module = module;
        Title = "App switcher";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        SetResourceReference(BackgroundProperty, "IslandBrush");
        Width = SwitcherBoard.Widest;
        Height = SwitcherBoard.HeightFor(SwitcherBoard.MaxRows);
        _board = new SwitcherBoard(on =>
        {
            if (_module is not null) _module.ShowCurrentDisplayOnly(on);
            else _session?.ShowCurrentDisplayOnly(on);
        }, index =>
        {
            if (_session is null) return;
            _session.Select(index);
            _switcher.Commit();
        }) { VerticalAlignment = VerticalAlignment.Top };
        _board.Drawn += () => Dispatcher.BeginInvoke(PlacePreviews, DispatcherPriority.Render);
        Content = _board;
        PreviewKeyDown += OnKey;
        Deactivated += (_, _) => _switcher.Dismiss();
    }

    internal SwitcherBoard Board => _board;

    /// <summary>The live previews Windows is drawing now, for tests.</summary>
    internal int PreviewCount => _previews.Count;

    public nint Show(SwitcherSession session, nint monitor)
    {
        if (_session is not null)
            _session.Changed -= Draw;
        _session = session;
        _monitor = monitor;
        session.Changed += Draw;
        if (_hwnd == 0)
        {
            _hwnd = new WindowInteropHelper(this).EnsureHandle();
            SetWindowLong(_hwnd, GwlExStyle, GetWindowLong(_hwnd, GwlExStyle) | WsExToolWindow);
            int round = DwmCornerRound;
            DwmSetWindowAttribute(_hwnd, DwmCornerPreference, ref round, sizeof(int));
        }
        var settings = _module?.Settings ?? new AppSwitcherSettings();
        _board.Show(session, w => WantsPreview(settings, w), Math.Min(SwitcherBoard.Widest, WorkWidth(monitor)));
        Width = _board.Width;
        Center(monitor);
        Show();
        _board.Search.Focus();
        return _hwnd;
    }

    /// <summary>Previews are on, and the window's app isn't in Apps without previews.</summary>
    internal static bool WantsPreview(AppSwitcherSettings settings, SwitcherWindow window) =>
        settings.Previews && !settings.NoPreview.Any(p =>
            string.Equals(System.IO.Path.GetFileName(p), System.IO.Path.GetFileName(window.Path), StringComparison.OrdinalIgnoreCase));

    public void ShowFailed(SwitcherWindow window) => _board.Failed(window);

    void ISwitcherView.Hide()
    {
        if (_session is not null)
            _session.Changed -= Draw;
        _session = null;
        ClearPreviews();
        Hide();
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        if (_session is null)
            return;
        switch (e.Key)
        {
            case Key.Enter:
                _switcher.Commit();
                break;
            case Key.Escape:
                _switcher.Cancel();
                break;
            // The cards are a grid: Up and Down step a row, Left, Right and Tab one card.
            case Key.Down:
                _session.Move(_board.Columns);
                break;
            case Key.Up:
                _session.Move(-_board.Columns);
                break;
            case Key.Right:
                _session.Move(1);
                break;
            case Key.Left:
                _session.Move(-1);
                break;
            case Key.Tab:
                _session.Move(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? -1 : 1);
                break;
            default:
                return; // letters go to the search box
        }
        e.Handled = true;
    }

    private void Draw() => _board.Draw();

    /// <summary>Puts each card's live preview in its slot, in the window's physical pixels, and drops the ones out of view.</summary>
    private void PlacePreviews()
    {
        if (_placing || _hwnd == 0 || _session is null || !IsVisible) return;
        _placing = true;
        try
        {
            Resize();
            UpdateLayout();
            double scale = GetDpiForWindow(_hwnd) / 96.0;
            var keep = new HashSet<nint>();
            foreach (var (window, slot, live) in _board.Slots.ToList())
            {
                if (!live || !slot.IsVisible) continue;
                if (!_previews.TryGetValue(window.Handle, out var preview))
                {
                    if (WindowPreview.Create(_hwnd, window.Handle) is not { } created)
                    {
                        _board.NoThumbnail(window.Handle);
                        continue;
                    }
                    _previews[window.Handle] = preview = created;
                }
                keep.Add(window.Handle);
                var (sw, sh) = preview.SourceSize;
                if (sw <= 0 || sh <= 0)
                {
                    preview.Place(0, 0, 0, 0, visible: false);
                    continue;
                }
                var origin = slot.TranslatePoint(new Point(Inset, Inset), this);
                double w = slot.ActualWidth - 2 * Inset, h = slot.ActualHeight - 2 * Inset;
                double fit = Math.Min(w / sw, h / sh), dw = sw * fit, dh = sh * fit;
                double left = origin.X + (w - dw) / 2, top = origin.Y + (h - dh) / 2;
                preview.Place((int)Math.Round(left * scale), (int)Math.Round(top * scale),
                    (int)Math.Round((left + dw) * scale), (int)Math.Round((top + dh) * scale));
            }
            foreach (var gone in _previews.Keys.Where(k => !keep.Contains(k)).ToList())
            {
                _previews[gone].Dispose();
                _previews.Remove(gone);
            }
        }
        finally { _placing = false; }
    }

    private const double Inset = 4;

    private void ClearPreviews()
    {
        foreach (var preview in _previews.Values) preview.Dispose();
        _previews.Clear();
    }

    /// <summary>The window grows and shrinks with its rows, its top staying where a full one's would be.</summary>
    private void Resize()
    {
        if (Math.Abs(Height - SwitcherBoard.HeightFor(_board.Rows)) >= 0.5)
            Center(_monitor);
    }

    /// <summary>The display's work area in DIPs, less a margin, for the board's width.</summary>
    private double WorkWidth(nint monitor)
    {
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (monitor == 0 || !GetMonitorInfo(monitor, ref info)) return SwitcherBoard.Widest;
        double scale = _hwnd != 0 ? GetDpiForWindow(_hwnd) / 96.0 : 1;
        return (info.Work.Right - info.Work.Left) / scale - 48;
    }

    // Centered across the display's work area, in physical pixels; its top is where a full three rows' would be, so the
    // search doesn't jump as the list grows or shrinks.
    private void Center(nint monitor)
    {
        Height = SwitcherBoard.HeightFor(_board.Rows);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (monitor == 0 || !GetMonitorInfo(monitor, ref info))
            return;
        double scale = GetDpiForWindow(_hwnd) / 96.0;
        int width = (int)(Width * scale), height = (int)(Height * scale);
        int full = (int)(SwitcherBoard.HeightFor(SwitcherBoard.MaxRows) * scale);
        int left = info.Work.Left + (info.Work.Right - info.Work.Left - width) / 2;
        int top = info.Work.Top + (info.Work.Bottom - info.Work.Top - full) / 2;
        SetWindowPos(_hwnd, 0, left, top, width, height, SwpNoZOrder | SwpNoActivate);
    }

    private const int GwlExStyle = -20, WsExToolWindow = 0x80;
    private const int DwmCornerPreference = 33, DwmCornerRound = 2;
    private const uint SwpNoZOrder = 0x4, SwpNoActivate = 0x10;

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo { public int Size; public NativeRect Monitor, Work; public uint Flags; }

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongW")] private static partial int GetWindowLong(nint hwnd, int index);
    [LibraryImport("user32.dll", EntryPoint = "SetWindowLongW")] private static partial int SetWindowLong(nint hwnd, int index, int value);
    [LibraryImport("user32.dll", EntryPoint = "GetMonitorInfoW")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
    [LibraryImport("user32.dll")] private static partial uint GetDpiForWindow(nint hwnd);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool SetWindowPos(nint hwnd, nint after, int x, int y, int cx, int cy, uint flags);
    [LibraryImport("dwmapi.dll")] private static partial int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);
}
