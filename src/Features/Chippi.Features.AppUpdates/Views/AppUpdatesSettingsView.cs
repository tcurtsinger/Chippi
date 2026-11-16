// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Chippi.Core.Modules;
using Chippi.UI.Controls;

namespace Chippi.Features.AppUpdates.Views;

/// <summary>App updates configuration, skipped versions and the last fifty results.</summary>
public sealed class AppUpdatesSettingsView : ContentControl, IDisposable
{
    private readonly AppUpdatesEngine _engine;
    private readonly ModuleContext _context;
    private readonly StackPanel _page = new() { MaxWidth = 1000 };
    private readonly Card _skipped = new(), _history = new();
    private readonly FrameworkElement _skippedHeading, _historyHeading;
    private readonly string? _section;
    private readonly ToggleButton _unknown;
    private readonly TextBlock _failure = new() { TextWrapping = TextWrapping.Wrap, Margin = new(0, 8, 0, 0) };
    private readonly List<(string Name, ShortcutSetting View)> _shortcuts = [];
    private bool _refreshing, _following, _disposed;
    private string? _skippedSignature, _historySignature;

    public AppUpdatesSettingsView(AppUpdatesEngine engine, ModuleContext context, string? section = null)
    {
        _engine = engine; _context = context; _section = section;
        var preferences = new Card();
        _unknown = new ToggleButton { IsChecked = engine.Preferences.ShowUnknownVersions, VerticalAlignment = VerticalAlignment.Center };
        _unknown.SetResourceReference(StyleProperty, "SwitchWithLabelStyle");
        var unknownRow = new SettingRow
        {
            Header = "Show apps with an unknown version", Description = "Include apps whose installed version can’t be read.",
            Icon = LucideIcons.Info, Content = _unknown,
        };
        preferences.Children.Add(unknownRow); _page.Children.Add(preferences);
        _skippedHeading = SettingsLayout.Section("Skipped"); _historyHeading = SettingsLayout.Section("History");
        _page.Children.Add(_skippedHeading); _page.Children.Add(_skipped);
        _page.Children.Add(_historyHeading); _page.Children.Add(_history);
        _page.Children.Add(SettingsLayout.Section("Shortcuts"));
        var shortcuts = new Card();
        foreach (string name in new[] { "Check app updates", "Update all apps" })
        {
            var view = new ShortcutSetting(name, () => context.EditShortcut(name));
            shortcuts.Children.Add(new SettingRow { Header = name, Icon = LucideIcons.Keyboard, Content = view });
            _shortcuts.Add((name, view));
        }
        _page.Children.Add(shortcuts);
        _failure.SetResourceReference(StyleProperty, "CaptionTextStyle");
        _failure.SetResourceReference(TextBlock.ForegroundProperty, "WarningTextBrush");
        AutomationProperties.SetLiveSetting(_failure, AutomationLiveSetting.Polite); _page.Children.Add(_failure);
        _unknown.Checked += ChangedUnknown; _unknown.Unchecked += ChangedUnknown;
        Loaded += OnLoaded; Unloaded += OnUnloaded;
        Content = new ScrollViewer
        {
            Content = _page, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        Refresh();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Follow(true); Refresh();
        if (_section is "skipped" or "history")
            Dispatcher.BeginInvoke(() => { if (IsLoaded && !_disposed) (_section == "skipped" ? _skippedHeading : _historyHeading).BringIntoView(); });
    }
    private void OnUnloaded(object sender, RoutedEventArgs e) => Follow(false);
    private void Follow(bool enabled)
    {
        if (_following == enabled || _disposed && enabled) return;
        if (enabled) _engine.Changed += Refresh; else _engine.Changed -= Refresh;
        _following = enabled;
    }
    private void ChangedUnknown(object sender, RoutedEventArgs e)
    {
        if (_refreshing) return;
        _engine.SetShowUnknownVersions(_unknown.IsChecked == true); Refresh();
    }
    private void Refresh()
    {
        if (_disposed) return;
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(Refresh); return; }
        _refreshing = true;
        try
        {
            _unknown.IsChecked = _engine.Preferences.ShowUnknownVersions;
            _unknown.Content = _unknown.IsChecked == true ? "On" : "Off";
            _unknown.IsEnabled = !_engine.IsRunning;
            var skipped = _engine.Preferences.Skipped;
            string skippedSignature = string.Join("|", skipped.Select(s => s.Key + ":" + s.Version)) + ":" + _engine.IsRunning;
            if (_skippedSignature != skippedSignature)
            {
                _skippedSignature = skippedSignature; _skipped.Children.Clear();
                foreach (var skip in skipped)
                {
                    var restore = SettingsButton("Restore", () => _engine.Restore(skip)); restore.IsEnabled = !_engine.IsRunning;
                    AutomationProperties.SetName(restore, "Restore updates for " + skip.Name);
                    var line = new Grid();
                    line.ColumnDefinitions.Add(new() { Width = new(26) }); line.ColumnDefinitions.Add(new()); line.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
                    AppUpdatesUi.Add(line, AppUpdatesUi.AppMark(skip.Name, null, loadIcon: false));
                    var copy = new StackPanel { Margin = new(12, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
                    var name = new TextBlock { Text = skip.Name, TextWrapping = TextWrapping.Wrap }; name.SetResourceReference(StyleProperty, "BodyTextStyle");
                    var detail = new TextBlock { Text = skip.Version is null ? "Always skipped" : skip.Version + " · This version", TextWrapping = TextWrapping.Wrap };
                    detail.SetResourceReference(StyleProperty, "CaptionTextStyle"); copy.Children.Add(name); copy.Children.Add(detail);
                    AppUpdatesUi.Add(line, copy, column: 1); AppUpdatesUi.Add(line, restore, column: 2);
                    _skipped.Children.Add(new Border { Child = line, MinHeight = SettingsLayout.CaptionRowHeight, Padding = SettingsLayout.RowPadding });
                }
                if (skipped.Count == 0) _skipped.Children.Add(new SettingRow { Header = "No skipped apps", Icon = LucideIcons.Check });
            }
            var history = _engine.Preferences.History.Take(50).ToArray();
            string historySignature = string.Join("|", history.Select(h => h.Id + ":" + h.At.Ticks + ":" + h.State + ":" + h.Reason));
            if (_historySignature != historySignature)
            {
                _historySignature = historySignature; _history.Children.Clear();
                foreach (var entry in history)
                {
                    bool success = entry.State is UpdateState.Updated or UpdateState.RestartRequired;
                    string result = entry.State switch
                    {
                        UpdateState.Updated => "Updated", UpdateState.RestartRequired => "Restart to finish",
                        UpdateState.NeedsAdmin => "Needs admin", UpdateState.Skipped => "Skipped", UpdateState.Cancelled => "Cancelled", _ => "Update failed",
                    };
                    var status = new TextBlock { Text = result, VerticalAlignment = VerticalAlignment.Center, ToolTip = entry.Reason ?? result };
                    status.SetResourceReference(StyleProperty, "CaptionTextStyle");
                    status.SetResourceReference(TextBlock.ForegroundProperty, success ? "SuccessBrush" : entry.State is UpdateState.Cancelled or UpdateState.Skipped ? "TextSecondaryBrush" : "WarningTextBrush");
                    _history.Children.Add(new SettingRow
                    {
                        Header = entry.Name, Description = $"{entry.FromVersion} → {entry.ToVersion} · {entry.At.LocalDateTime:g}",
                        Icon = success ? LucideIcons.Check : LucideIcons.TriangleAlert, Content = status,
                    });
                }
                if (history.Length == 0) _history.Children.Add(new SettingRow { Header = "No update history", Icon = LucideIcons.Clock });
            }
            foreach (var (name, view) in _shortcuts)
            {
                var slot = _context.Shortcuts.List.FirstOrDefault(s => s.Owner == "app-updates" && s.Name == name);
                view.Show(slot?.Shortcut, slot?.Error);
            }
            _failure.Text = _engine.Error ?? "";
            _failure.Visibility = _failure.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        }
        finally { _refreshing = false; }
    }
    private static Button SettingsButton(string name, Action action)
    {
        var button = new Button { Content = name, MinWidth = 68, VerticalAlignment = VerticalAlignment.Center };
        button.SetResourceReference(StyleProperty, "SecondaryButtonStyle"); button.Click += (_, _) => action();
        return button;
    }
    public void Dispose()
    {
        if (_disposed) return;
        Follow(false); _disposed = true;
        Loaded -= OnLoaded; Unloaded -= OnUnloaded;
        _unknown.Checked -= ChangedUnknown; _unknown.Unchecked -= ChangedUnknown;
    }
}
