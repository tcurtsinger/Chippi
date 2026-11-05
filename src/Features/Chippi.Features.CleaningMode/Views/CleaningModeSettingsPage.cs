// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.Core.Shortcuts;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Chippi.Features.Sound.Mute;
using Chippi.UI.Controls;

namespace Chippi.Features.CleaningMode.Views;

/// <summary>
/// Settings › Cleaning mode (docs/phase-5.5/cleaning-mode.md, Start): the droplet in the accent, "Cleaning mode" and Start;
/// then its shortcut read-only with Edit, which records it in Settings › Shortcuts.
/// </summary>
public sealed class CleaningModeSettingsPage : UserControl
{
    public CleaningModeSettingsPage(CleaningModeModule module)
    {
        var glyph = new LucideIcon { Data = LucideIcons.Droplet, Size = 16, Margin = new Thickness(0, 0, 14, 0), VerticalAlignment = VerticalAlignment.Center };
        glyph.SetResourceReference(LucideIcon.BrushProperty, "AccentBrush");
        var start = new Button { Content = "Start", VerticalAlignment = VerticalAlignment.Center };
        start.SetResourceReference(StyleProperty, "PrimaryButtonStyle");
        AutomationProperties.SetName(start, "Start Cleaning mode");
        var name = new TextBlock { Text = "Cleaning mode" };
        name.SetResourceReference(StyleProperty, "BodyTextStyle");
        var failed = new TextBlock { Text = "Cleaning mode failed", Visibility = Visibility.Collapsed };
        failed.SetResourceReference(StyleProperty, "CaptionTextStyle");
        failed.SetResourceReference(TextBlock.ForegroundProperty, "WarningTextBrush");
        AutomationProperties.SetLiveSetting(failed, AutomationLiveSetting.Polite);
        start.Click += (_, _) =>
        {
            try
            {
                module.Start();
                failed.Visibility = Visibility.Collapsed;
            }
            catch (Exception)
            {
                failed.Visibility = Visibility.Visible;
            }
        };
        DockPanel.SetDock(glyph, Dock.Left);
        DockPanel.SetDock(start, Dock.Right);
        var label = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { name, failed } };
        var row = new Border { MinHeight = SettingsLayout.RowHeight, Padding = SettingsLayout.RowPadding, Child = new DockPanel { Children = { glyph, start, label } } };
        var shortcut = new ShortcutSetting(module.Descriptor.Name, module.EditShortcut);
        void Refresh()
        {
            if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(Refresh); return; }
            shortcut.Show(module.Settings.Shortcut, module.ShortcutError);
        }
        Loaded += (_, _) => { module.Changed += Refresh; Refresh(); };
        Unloaded += (_, _) => module.Changed -= Refresh;
        Refresh();
        var shortcutRow = SettingsLayout.Row("Shortcut", shortcut);
        shortcutRow.Icon = LucideIcons.Keyboard;
        Content = SettingsLayout.Card(row, shortcutRow);
    }
}
