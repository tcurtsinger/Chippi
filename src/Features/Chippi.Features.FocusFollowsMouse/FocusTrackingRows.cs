// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Chippi.UI.Controls;

namespace Chippi.Features.FocusFollowsMouse;

// Content contribution for Settings > Windows > Mouse. The shell owns the page title.
public sealed class FocusTrackingRows : UserControl
{
    private readonly ToggleButton _toggle = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _error = new() { Text = "Focus change failed", Visibility = Visibility.Collapsed };
    private readonly Border _row;
    private bool _updating;

    public FocusTrackingRows(FocusFollowsMouseModule module)
    {
        _toggle.SetResourceReference(StyleProperty, "SwitchWithLabelStyle");
        AutomationProperties.SetName(_toggle, "Focus follows mouse");
        var label = new TextBlock { Text = "Focus follows mouse" };
        label.SetResourceReference(StyleProperty, "BodyTextStyle");
        _error.SetResourceReference(StyleProperty, "CaptionTextStyle");
        _error.SetResourceReference(TextBlock.ForegroundProperty, "WarningTextBrush");
        AutomationProperties.SetLiveSetting(_error, AutomationLiveSetting.Polite);
        var labels = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { label, _error } };
        DockPanel.SetDock(_toggle, Dock.Right);
        _row = new Border
        {
            MinHeight = SettingsLayout.RowHeight,
            Padding = SettingsLayout.RowPadding,
            Child = new DockPanel { Children = { _toggle, labels } }
        };
        Content = SettingsLayout.Card(_row);
        // UIA Toggle raises Checked/Unchecked, not Click.
        _toggle.Checked += (_, _) => Change(true);
        _toggle.Unchecked += (_, _) => Change(false);
        Loaded += (_, _) => { module.Changed += Refresh; Refresh(); };
        Unloaded += (_, _) => module.Changed -= Refresh;
        Refresh();

        void Change(bool enabled)
        {
            if (_updating) return;
            module.SetEnabled(enabled);
            Refresh();
        }

        void Refresh()
        {
            if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(Refresh); return; }
            _updating = true;
            try
            {
                _toggle.IsChecked = module.Settings.Enabled;
                _toggle.Content = module.Settings.Enabled ? "On" : "Off";
                AutomationProperties.SetItemStatus(_toggle, module.Error ?? (module.Settings.Enabled ? "On" : "Off"));
                _error.Visibility = module.Error is null ? Visibility.Collapsed : Visibility.Visible;
                _row.MinHeight = module.Error is null ? SettingsLayout.RowHeight : SettingsLayout.CaptionRowHeight;
            }
            finally { _updating = false; }
        }
    }
}
