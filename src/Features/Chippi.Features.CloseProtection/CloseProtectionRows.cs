// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Chippi.UI.Controls;
using Microsoft.Win32;

namespace Chippi.Features.CloseProtection;

public sealed class CloseProtectionRows : UserControl
{
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> Names = new(StringComparer.OrdinalIgnoreCase);
    private readonly CloseProtectionModule _module;
    private readonly ToggleButton _toggle = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _error = new() { Visibility = Visibility.Collapsed };
    private readonly Border _toggleRow;
    private readonly Card _apps = SettingsLayout.Card();
    private bool _updating;
    private bool _loaded;
    private int _menuGeneration;
    private string _paths = "";

    public CloseProtectionRows(CloseProtectionModule module)
    {
        _module = module;
        _toggle.SetResourceReference(StyleProperty, "SwitchWithLabelStyle");
        AutomationProperties.SetName(_toggle, "Close protection");
        var label = new TextBlock { Text = "Close protection" };
        label.SetResourceReference(StyleProperty, "BodyTextStyle");
        _error.SetResourceReference(StyleProperty, "CaptionTextStyle");
        _error.SetResourceReference(TextBlock.ForegroundProperty, "WarningTextBrush");
        AutomationProperties.SetLiveSetting(_error, AutomationLiveSetting.Polite);
        DockPanel.SetDock(_toggle, Dock.Right);
        _toggleRow = new Border
        {
            MinHeight = SettingsLayout.RowHeight, Padding = SettingsLayout.RowPadding,
            Child = new DockPanel { Children = { _toggle, new StackPanel
                { VerticalAlignment = VerticalAlignment.Center, Children = { label, _error } } } }
        };
        _apps.Margin = new Thickness(0, 8, 0, 0);
        Content = new StackPanel { Children = { SettingsLayout.Card(_toggleRow), _apps } };
        _toggle.Checked += (_, _) => Change(true);
        _toggle.Unchecked += (_, _) => Change(false);
        Loaded += (_, _) => { if (_loaded) return; _loaded = true; module.Changed += Refresh; Refresh(); };
        Unloaded += (_, _) => { _loaded = false; module.Changed -= Refresh; _menuGeneration++; };
        Refresh();
    }

    private void Change(bool enabled)
    {
        if (_updating) return;
        _module.SetEnabled(enabled);
        Refresh();
    }

    private void Refresh()
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(Refresh); return; }
        _updating = true;
        try
        {
            _toggle.IsChecked = _module.Settings.Enabled;
            _toggle.Content = _module.Settings.Enabled ? "On" : "Off";
            AutomationProperties.SetItemStatus(_toggle, _module.Error ?? (_module.Settings.Enabled ? "On" : "Off"));
            _error.Text = _module.Error ?? "";
            _error.Visibility = _module.Error is null ? Visibility.Collapsed : Visibility.Visible;
            _toggleRow.MinHeight = _module.Error is null ? SettingsLayout.RowHeight : SettingsLayout.CaptionRowHeight;
            _apps.IsEnabled = _module.Settings.Enabled;
            _apps.Opacity = _module.Settings.Enabled ? 1 : 0.45;
            string paths = string.Join('\n', _module.Settings.ControlWApps);
            if (_apps.Children.Count > 0 && paths == _paths) return;
            _paths = paths;
            _apps.Children.Clear();
            var add = Button("Add app");
            add.Click += async (_, _) => await AddMenu(add);
            int count = _module.Settings.ControlWApps.Count;
            _apps.Children.Add(SettingsLayout.Row("Ctrl+W in these apps", add, count == 0 ? null : $"{count} {(count == 1 ? "app" : "apps")}"));
            foreach (string path in _module.Settings.ControlWApps)
            {
                string name = Names.GetOrAdd(path, static exe =>
                {
                    try
                    {
                        if (System.Diagnostics.FileVersionInfo.GetVersionInfo(exe).FileDescription is { Length: > 0 } description) return description;
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { }
                    return Path.GetFileNameWithoutExtension(exe);
                });
                var fallback = new TextBlock { Text = name.Length == 0 ? "?" : name[..1].ToUpperInvariant(), FontSize = 12,
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                fallback.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
                var icon = AppIcon.View(path, 20, fallback);
                icon.Margin = new Thickness(0, 0, 12, 0);
                DockPanel.SetDock(icon, Dock.Left);
                var remove = Button("Remove");
                AutomationProperties.SetName(remove, $"Remove {name}");
                remove.Click += (_, _) => { _module.RemoveApp(path); Refresh(); };
                DockPanel.SetDock(remove, Dock.Right);
                var title = new TextBlock { Text = name, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = path, VerticalAlignment = VerticalAlignment.Center };
                title.SetResourceReference(StyleProperty, "BodyTextStyle");
                _apps.Children.Add(new Border { MinHeight = SettingsLayout.RowHeight, Padding = SettingsLayout.DependentPadding,
                    Child = new DockPanel { Children = { icon, remove, title } } });
            }
        }
        finally { _updating = false; }
    }

    private async Task AddMenu(Button anchor)
    {
        int generation = ++_menuGeneration;
        anchor.IsEnabled = false;
        try
        {
            var apps = await _module.OpenAppsAsync();
            if (generation != _menuGeneration || !_module.Settings.Enabled) return;
            var menu = new ContextMenu { PlacementTarget = anchor, Placement = PlacementMode.Bottom };
            foreach (var app in apps)
            {
                var item = new MenuItem { Header = app.Name, Icon = AppIcon.View(app.Path, 20, new TextBlock { Text = (app.Name.Length > 0 ? app.Name[..1] : "?") }) };
                item.Click += (_, _) => { _module.AddApp(app.Path); Refresh(); };
                menu.Items.Add(item);
            }
            if (apps.Count > 0) menu.Items.Add(new Separator());
            var browse = new MenuItem { Header = "Browse…" };
            browse.Click += (_, _) =>
            {
                var picker = new OpenFileDialog { Filter = "Apps (*.exe)|*.exe", CheckFileExists = true };
                if (picker.ShowDialog(Window.GetWindow(this)) == true) { _module.AddApp(picker.FileName); Refresh(); }
            };
            menu.Items.Add(browse);
            menu.IsOpen = true;
        }
        catch (Exception ex)
        {
            Chippi.Core.Diagnostics.Log.Warn("Close protection: listing apps failed", ex);
            _error.Text = "App list failed";
            _error.Visibility = Visibility.Visible;
        }
        finally { anchor.IsEnabled = _module.Settings.Enabled; }
    }

    private static Button Button(string label)
    {
        var button = new Button { Content = label, VerticalAlignment = VerticalAlignment.Center };
        button.SetResourceReference(StyleProperty, "SecondaryButtonStyle");
        return button;
    }
}
