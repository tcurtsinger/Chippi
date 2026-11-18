// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Chippi.Core.Modules;
using Chippi.UI.Controls;

namespace Chippi.Features.Cleaner.Views;

/// <summary>
/// Settings › Apps › Cleaner (docs/phase-7/design/cleaner.html): one row per category (Check first badge, the admin shield's own narrow column, an
/// On/Off switch that is the list's remembered tick), then the shortcuts, read-only with Edit.
/// </summary>
public sealed class CleanerSettingsView : ContentControl, IDisposable
{
    private readonly CleanerEngine _engine;
    private readonly ModuleContext _context;
    private readonly StackPanel _page = new() { MaxWidth = 1000 };
    private readonly Dictionary<string, ToggleButton> _switches = [];
    private readonly List<(string Name, ShortcutSetting View)> _shortcuts = [];
    private bool _refreshing, _following, _disposed;

    public CleanerSettingsView(CleanerEngine engine, ModuleContext context)
    {
        _engine = engine;
        _context = context;
        _page.Children.Add(SettingsLayout.Section("Categories"));
        var categories = new Card();
        foreach (var category in CleanerCatalog.All)
        {
            var toggle = new ToggleButton { VerticalAlignment = VerticalAlignment.Center };
            toggle.SetResourceReference(StyleProperty, "SwitchWithLabelStyle");
            var id = category.Id;
            RoutedEventHandler changed = (_, _) => Changed(id, toggle);
            toggle.Checked += changed;
            toggle.Unchecked += changed;
            _switches[id] = toggle;
            var controls = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            if (category.Tag == CleanerTag.CheckFirst)
                controls.Children.Add(new Badge { Text = "Check first", Kind = BadgeKind.Overlap, Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center });
            var shield = new Grid { Width = 20, Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
            if (category.NeedsAdmin) shield.Children.Add(new UacShield(16) { HorizontalAlignment = HorizontalAlignment.Center });
            controls.Children.Add(shield);
            controls.Children.Add(toggle);
            categories.Children.Add(new SettingRow { Header = category.Name, Content = controls });
        }
        _page.Children.Add(categories);
        _page.Children.Add(SettingsLayout.Section("Shortcuts"));
        var shortcuts = new Card();
        foreach (var (name, icon) in new[] { (CleanerModule.ScanName, LucideIcons.Search), (CleanerModule.CleanName, LucideIcons.Trash2) })
        {
            var view = new ShortcutSetting(name, () => context.EditShortcut(name));
            shortcuts.Children.Add(new SettingRow { Header = name, Icon = icon, Content = view });
            _shortcuts.Add((name, view));
        }
        _page.Children.Add(shortcuts);
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        Content = new ScrollViewer { Content = _page, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Refresh();
    }

    internal IReadOnlyDictionary<string, ToggleButton> Switches => _switches;

    private void OnLoaded(object sender, RoutedEventArgs e) { Follow(true); Refresh(); }
    private void OnUnloaded(object sender, RoutedEventArgs e) => Follow(false);

    private void Follow(bool enabled)
    {
        if (_following == enabled || _disposed && enabled) return;
        if (enabled) _engine.Changed += OnEngineChanged; else _engine.Changed -= OnEngineChanged;
        _following = enabled;
    }

    private void OnEngineChanged()
    {
        if (!Dispatcher.CheckAccess()) Dispatcher.BeginInvoke(OnEngineChanged); else Refresh();
    }

    /// <summary>A tick that can't be saved goes back to what it was.</summary>
    private void Changed(string id, ToggleButton toggle)
    {
        if (_refreshing) return;
        bool on = toggle.IsChecked == true;
        if (!_engine.SetTicked(id, on)) Refresh();
        else toggle.Content = on ? "On" : "Off";
    }

    private void Refresh()
    {
        if (_disposed) return;
        _refreshing = true;
        try
        {
            foreach (var (id, toggle) in _switches)
            {
                bool on = _engine.IsTicked(id);
                toggle.IsChecked = on;
                toggle.Content = on ? "On" : "Off";
                toggle.IsEnabled = !_engine.IsCleaning;
            }
            foreach (var (name, view) in _shortcuts)
            {
                var slot = _context.Shortcuts.List.FirstOrDefault(s => s.Owner == CleanerModule.Id && s.Name == name);
                view.Show(slot?.Shortcut, slot?.Error);
            }
        }
        finally { _refreshing = false; }
    }

    public void Dispose()
    {
        if (_disposed) return;
        Follow(false);
        _disposed = true;
        Loaded -= OnLoaded;
        Unloaded -= OnUnloaded;
    }
}
