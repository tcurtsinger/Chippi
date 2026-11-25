// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Chippi.UI.Controls;

namespace Chippi.Features.MacDesktop;

/// <summary>
/// Settings › Mac-style desktop: Desktop style, then the Menu bar, Dock and Notch switches and each surface's options. Terse
/// labels and values only (AGENTS.md, copy).
/// </summary>
public sealed class MacDesktopRows : UserControl
{
    private readonly MacDesktopModule _module;
    private readonly ComboBox _style = SettingsLayout.Combo(["Windows", "Mac-style"]);
    private readonly ToggleButton _menuBar = Switch("Menu bar"), _dock = Switch("Dock"), _notch = Switch("Notch");
    private readonly ComboBox _hide = SettingsLayout.Combo(["Always shown", "Auto-hide", "When a window touches it"]);
    private readonly ComboBox _position = SettingsLayout.Combo(["Bottom", "Left", "Right"]);
    private readonly ToggleButton _magnify = Switch("Magnification");
    private readonly Slider _size = new() { Minimum = MacDesktopSettings.MinMagnified, Maximum = MacDesktopSettings.MaxMagnified, Width = SettingsLayout.ComboWidth, IsSnapToTickEnabled = true, TickFrequency = 4, VerticalAlignment = VerticalAlignment.Center };
    private readonly ComboBox _opens = SettingsLayout.Combo(["Hover or click", "Click"]);
    private readonly TextBlock _error = new() { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 8, 0, 0) };
    private readonly FrameworkElement _surfaces, _dockRows, _notchRows;
    private bool _updating;

    public MacDesktopRows(MacDesktopModule module)
    {
        _module = module;
        AutomationProperties.SetName(_style, "Desktop style");
        AutomationProperties.SetName(_hide, "Hide");
        AutomationProperties.SetName(_position, "Position");
        AutomationProperties.SetName(_size, "Magnified size");
        AutomationProperties.SetName(_opens, "Opens on");
        _error.SetResourceReference(StyleProperty, "CaptionTextStyle");
        _error.SetResourceReference(TextBlock.ForegroundProperty, "WarningTextBrush");

        var center = new Button { Content = "Center", VerticalAlignment = VerticalAlignment.Center };
        center.SetResourceReference(StyleProperty, "SecondaryButtonStyle");
        center.Click += (_, _) => Save(s => s with { NotchDisplay = null, NotchPlace = 0.5 });

        _surfaces = SettingsLayout.Card(
            SettingsLayout.Row("Menu bar", _menuBar),
            SettingsLayout.Row("Dock", _dock),
            SettingsLayout.Row("Notch", _notch));
        _surfaces.Margin = new Thickness(0, 8, 0, 0);
        _dockRows = new StackPanel
        {
            Children =
            {
                SettingsLayout.Section("Dock"),
                SettingsLayout.Card(
                    SettingsLayout.Row("Hide", _hide),
                    SettingsLayout.Row("Position", _position),
                    SettingsLayout.Row("Magnification", _magnify),
                    SettingsLayout.Row("Magnified size", _size)),
            },
        };
        _notchRows = new StackPanel
        {
            Children =
            {
                SettingsLayout.Section("Notch"),
                SettingsLayout.Card(
                    SettingsLayout.Row("Opens on", _opens),
                    SettingsLayout.Row("Place", center)),
            },
        };
        Content = new StackPanel
        {
            Children =
            {
                SettingsLayout.Card(SettingsLayout.Row("Desktop style", _style)),
                _error,
                _surfaces,
                _dockRows,
                _notchRows,
            },
        };

        _style.SelectionChanged += (_, _) => Save(s => s with { Style = _style.SelectedIndex == 1 ? DesktopStyle.MacStyle : DesktopStyle.Windows });
        Hook(_menuBar, on => s => s with { MenuBar = on });
        Hook(_dock, on => s => s with { Dock = on });
        Hook(_notch, on => s => s with { Notch = on });
        Hook(_magnify, on => s => s with { Magnify = on });
        _hide.SelectionChanged += (_, _) => Save(s => s with { DockHide = (DockHiding)Math.Max(0, _hide.SelectedIndex) });
        _position.SelectionChanged += (_, _) => Save(s => s with { DockPosition = (DockEdge)Math.Max(0, _position.SelectedIndex) });
        _size.ValueChanged += (_, _) => Save(s => s with { MagnifiedSize = (int)Math.Round(_size.Value) });
        _opens.SelectionChanged += (_, _) => Save(s => s with { NotchOpensOnHover = _opens.SelectedIndex != 1 });

        Loaded += (_, _) => { module.Changed += Refresh; Refresh(); };
        Unloaded += (_, _) => module.Changed -= Refresh;
        Refresh();
    }

    private static ToggleButton Switch(string name)
    {
        var toggle = new ToggleButton { VerticalAlignment = VerticalAlignment.Center };
        toggle.SetResourceReference(StyleProperty, "SwitchWithLabelStyle");
        AutomationProperties.SetName(toggle, name);
        return toggle;
    }

    private void Hook(ToggleButton toggle, Func<bool, Func<MacDesktopSettings, MacDesktopSettings>> change)
    {
        toggle.Checked += (_, _) => Save(change(true));
        toggle.Unchecked += (_, _) => Save(change(false));
    }

    private void Save(Func<MacDesktopSettings, MacDesktopSettings> change)
    {
        if (_updating)
            return;
        var next = change(_module.Settings);
        if (next == _module.Settings)
            return;
        try
        {
            _ = _module.ApplyAsync(next);
            _error.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            Chippi.Core.Diagnostics.Log.Warn("Mac-style desktop: saving settings failed", ex);
            _error.Text = "Save failed";
            _error.Visibility = Visibility.Visible;
        }
        Refresh();
    }

    private void Refresh()
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(Refresh); return; }
        _updating = true;
        try
        {
            var s = _module.Settings;
            _style.SelectedIndex = s.IsMac ? 1 : 0;
            Set(_menuBar, s.MenuBar);
            Set(_dock, s.Dock);
            Set(_notch, s.Notch);
            Set(_magnify, s.Magnify);
            _hide.SelectedIndex = (int)s.DockHide;
            _position.SelectedIndex = (int)s.DockPosition;
            _size.Value = s.MagnifiedSize;
            _size.IsEnabled = s.Magnify;
            _opens.SelectedIndex = s.NotchOpensOnHover ? 0 : 1;
            Dim(_surfaces, s.IsMac);
            Dim(_dockRows, s.IsMac && s.Dock);
            Dim(_notchRows, s.IsMac && s.Notch);
            if (_module.Error is { } error)
            {
                _error.Text = error;
                _error.Visibility = Visibility.Visible;
            }
        }
        finally { _updating = false; }
    }

    private static void Set(ToggleButton toggle, bool on)
    {
        toggle.IsChecked = on;
        toggle.Content = on ? "On" : "Off";
    }

    private static void Dim(FrameworkElement rows, bool enabled)
    {
        rows.IsEnabled = enabled;
        rows.Opacity = enabled ? 1 : 0.45;
    }
}
