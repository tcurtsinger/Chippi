// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Chippi.App.Shell;
using Chippi.Core.Modules;
using Chippi.Core.Notifications;
using Chippi.UI.Controls;

namespace Chippi.App.Settings.Pages;

/// <summary>
/// Settings › Notifications (DESIGN.md §5, approved 2026-10-22): Windows and Island tiles with
/// master switches, then one group per running feature that declares events, in hub order, with a
/// Windows and an Island checkbox per event. A place switched off dims its column to 60% and keeps
/// every choice editable.
/// </summary>
internal sealed class NotificationsPage : UserControl
{
    private const double DimmedOpacity = 0.6;
    private const double CheckColumn = 96;

    private readonly ShellContext _shell;
    private readonly StackPanel _groups = new();
    private readonly List<FrameworkElement> _windowsColumn = [];
    private readonly List<FrameworkElement> _islandColumn = [];

    public NotificationsPage(ShellContext shell)
    {
        _shell = shell;
        var app = SettingsUi.ReadApp(shell.Config);

        var page = new StackPanel();
        page.Children.Add(SettingsUi.Styled(new TextBlock { Text = "Notifications", Margin = new Thickness(0, 0, 0, 18) }, "PageTitleStyle"));

        var windows = Destination("Windows notifications", app.WindowsNotices, on =>
        {
            SettingsUi.SaveApp(shell.Config, s => s.WindowsNotices = on);
            Dim(_windowsColumn, on);
        });
        var island = Destination("Island", app.IslandNotices, on =>
        {
            SettingsUi.SaveApp(shell.Config, s => s.IslandNotices = on);
            Dim(_islandColumn, on);
        });
        page.Children.Add(SettingsUi.Group(windows, island));

        // The backend is known to be missing: say so once, claim nothing about Focus or Do not disturb.
        if (shell.Services.Toasts is null)
            page.Children.Add(Unavailable());

        page.Children.Add(_groups);
        Content = page;

        BuildGroups();
        Dim(_windowsColumn, app.WindowsNotices);
        Dim(_islandColumn, app.IslandNotices);

        // Features installed or removed while the page is open change the groups.
        Loaded += (_, _) => shell.Registry.Changed += OnRegistryChanged;
        Unloaded += (_, _) => shell.Registry.Changed -= OnRegistryChanged;
    }

    private void OnRegistryChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(() =>
    {
        var app = SettingsUi.ReadApp(_shell.Config);
        BuildGroups();
        Dim(_windowsColumn, app.WindowsNotices);
        Dim(_islandColumn, app.IslandNotices);
    });

    private void BuildGroups()
    {
        _groups.Children.Clear();
        _windowsColumn.Clear();
        _islandColumn.Clear();
        var running = _shell.Registry.Features
            .Where(f => f.Notifier is { Kinds.Count: > 0 })
            .ToList();
        if (running.Count == 0)
        {
            _groups.Children.Add(SettingsUi.Styled(new TextBlock { Text = "No features with notifications are on", Margin = new Thickness(0, 22, 0, 0) }, "CaptionTextStyle"));
            return;
        }
        foreach (var feature in running)
            _groups.Children.Add(Group(feature));
    }

    private FrameworkElement Group(FeatureEntry feature)
    {
        var notifier = feature.Notifier!;
        var header = Columns();
        header.Margin = new Thickness(15, 22, 15, 8);
        var eyebrow = SettingsUi.Section(feature.Descriptor.Name);
        eyebrow.Margin = new Thickness(0);
        eyebrow.VerticalAlignment = VerticalAlignment.Center;
        header.Children.Add(eyebrow);
        header.Children.Add(Column(ColumnHeader("Windows"), 1, _windowsColumn));
        header.Children.Add(Column(ColumnHeader("Island"), 2, _islandColumn));

        var rows = new Card();
        var kinds = notifier.Kinds;
        for (int i = 0; i < kinds.Count; i++)
        {
            var kind = kinds[i];
            var row = Columns();
            row.Height = 48;
            var name = SettingsUi.Styled(new TextBlock { Text = kind.Name, VerticalAlignment = VerticalAlignment.Center }, "RowTitleStyle");
            row.Children.Add(name);
            row.Children.Add(Column(Box(feature, kind, NoticePlaces.Windows, "Windows"), 1, _windowsColumn));
            row.Children.Add(Column(Box(feature, kind, NoticePlaces.Island, "Island"), 2, _islandColumn));
            row.Margin = new Thickness(16, 0, 16, 0);
            rows.Children.Add(row);
        }
        var group = new StackPanel();
        group.Children.Add(header);
        group.Children.Add(rows);
        AutomationProperties.SetName(group, feature.Descriptor.Name);
        return group;
    }

    /// <summary>A checkbox bound to one event's place. It stays editable while its column is dimmed.</summary>
    private static CheckBox Box(FeatureEntry feature, NoticeKind kind, NoticePlaces place, string placeName)
    {
        var notifier = feature.Notifier!;
        var box = new CheckBox { IsChecked = notifier.PlacesFor(kind.Id).HasFlag(place), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(box, $"{feature.Descriptor.Name}, {kind.Name}, {placeName}");
        void Changed(object sender, RoutedEventArgs e)
        {
            // Read the stored places fresh, so the other column's choice is never overwritten.
            var current = feature.Notifier;
            if (current is null)
                return;
            var places = current.PlacesFor(kind.Id);
            current.SetPlaces(kind.Id, box.IsChecked == true ? places | place : places & ~place);
        }
        box.Checked += Changed;
        box.Unchecked += Changed;
        // Hover brings a dimmed box back to full strength; leaving restores the column's dim.
        box.MouseEnter += (_, _) => box.Opacity = 1;
        box.MouseLeave += (_, _) => box.Opacity = box.Tag is double dimmed ? dimmed : 1;
        return box;
    }

    private static FrameworkElement Column(FrameworkElement element, int column, List<FrameworkElement> list)
    {
        Grid.SetColumn(element, column);
        list.Add(element);
        return element;
    }

    private static TextBlock ColumnHeader(string text) =>
        SettingsUi.Styled(new TextBlock { Text = text, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }, "CaptionTextStyle");

    private static Grid Columns()
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(CheckColumn) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(CheckColumn) });
        return grid;
    }

    /// <summary>A place switched off: its column dims in every group; a dimmed box is still a live checkbox.</summary>
    private static void Dim(List<FrameworkElement> column, bool on)
    {
        foreach (var element in column)
        {
            element.Opacity = on ? 1 : DimmedOpacity;
            element.Tag = element.Opacity; // what a hovered box returns to
        }
    }

    private static SettingRow Destination(string title, bool on, Action<bool> changed)
    {
        var toggle = SettingsUi.Switch(on);
        var row = new SettingRow { Header = title, Description = on ? "" : "Off \u00B7 choices kept", Content = toggle };
        SettingsUi.OnSwitched(toggle, value =>
        {
            row.Description = value ? "" : "Off \u00B7 choices kept";
            changed(value);
        });
        return row;
    }
    private static FrameworkElement Unavailable()
    {
        var dot = new StatusDot { Kind = StatusKind.Attention, VerticalAlignment = VerticalAlignment.Center };
        var text = SettingsUi.Styled(new TextBlock { Text = "Windows notifications unavailable", Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center }, "CaptionTextStyle");
        text.SetResourceReference(TextBlock.ForegroundProperty, "WarningTextBrush");
        var line = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(2, 10, 0, 0) };
        line.Children.Add(dot);
        line.Children.Add(text);
        AutomationProperties.SetLiveSetting(text, AutomationLiveSetting.Polite);
        return line;
    }
}
