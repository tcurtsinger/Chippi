// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Chippi.App.Shell;
using Chippi.Core.Diagnostics;
using Chippi.Core.Modules;
using Chippi.UI.Controls;

namespace Chippi.App.Settings.Pages;

internal sealed class AddFeaturesPage : UserControl
{
    private readonly ShellContext _shell;
    private readonly SearchBox _search = new() { Placeholder = "Search features" };
    private readonly TextBlock _count = SettingsUi.Styled(new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 0, 0) }, "NumberTextStyle");
    private readonly TextBlock _message = SettingsUi.Message();
    private readonly TextBlock _empty = SettingsUi.Styled(new TextBlock { Text = "No features match", Visibility = Visibility.Collapsed, Margin = new Thickness(0, 16, 0, 0) }, "CaptionTextStyle");
    private readonly List<(FeatureEntry Entry, SettingRow Row, IconChip Chip, ToggleButton Switch)> _rows = [];
    private readonly List<(FeatureEntry Entry, TextBlock Label, FrameworkElement Heading)> _later = [];
    private readonly ExpanderRow _coming;
    private readonly FrameworkElement _availableHeading;
    private readonly Card _available = new();
    private bool _syncing;

    public AddFeaturesPage(ShellContext shell)
    {
        _shell = shell;
        var page = new StackPanel();
        page.Children.Add(SettingsUi.Header("Add features", ""));
        var search = new DockPanel();
        DockPanel.SetDock(_count, Dock.Right);
        search.Children.Add(_count);
        search.Children.Add(_search);
        page.Children.Add(search);
        page.Children.Add(_message);
        _availableHeading = SettingsUi.Section("Available");
        page.Children.Add(_availableHeading);
        page.Children.Add(_available);
        page.Children.Add(_empty);
        AutomationProperties.SetName(_search, "Search features");
        AutomationProperties.SetLiveSetting(_count, AutomationLiveSetting.Polite);
        foreach (var entry in shell.Registry.Features.Where(f => !f.IsComingSoon))
        {
            var toggle = SettingsUi.Switch(entry.IsInstalled);
            var chip = new IconChip { Icon = LucideIcons.Get(entry.Descriptor.Icon), IsOn = entry.IsInstalled, Margin = new Thickness(0, 0, 14, 0) };
            var right = new StackPanel { Orientation = Orientation.Horizontal };
            foreach (var badge in entry.Descriptor.Badges.Where(b => b.Kind != BadgeKind.Source))
                right.Children.Add(new Badge { Text = badge.Text, Kind = badge.Kind, Margin = new Thickness(0, 0, 14, 0), VerticalAlignment = VerticalAlignment.Center });
            right.Children.Add(toggle);
            var row = new SettingRow { Header = entry.Descriptor.Name, Description = entry.Descriptor.Description, Content = right };
            AutomationProperties.SetName(toggle, entry.Descriptor.Name);
            var layout = new DockPanel { Margin = new Thickness(16, 0, 0, 0) };
            DockPanel.SetDock(chip, Dock.Left);
            layout.Children.Add(chip);
            row.Padding = new Thickness(0, 10, 16, 10);
            layout.Children.Add(row);
            _available.Children.Add(layout);
            _rows.Add((entry, row, chip, toggle));
            SettingsUi.OnSwitched(toggle, on => SetInstalled(entry, toggle, on));
        }
        var names = new StackPanel();
        var placeholders = shell.Registry.Features.Where(f => f.IsComingSoon).ToList();
        foreach (var group in placeholders.GroupBy(f => f.Descriptor.Group))
        {
            var heading = SettingsUi.Section(group.Key.DisplayName());
            names.Children.Add(heading);
            foreach (var entry in group)
            {
                var name = SettingsUi.Styled(new TextBlock { Text = entry.Descriptor.Name, Margin = new Thickness(0, 4, 0, 4) }, "BodyTextStyle");
                names.Children.Add(name);
                _later.Add((entry, name, heading));
            }
        }
        names.Margin = new Thickness(50, 0, 16, 16);
        _coming = new ExpanderRow { Header = "Coming later", Description = $"{placeholders.Count} features on the way", Content = names };
        var laterCard = SettingsUi.Group(_coming);
        laterCard.Margin = new Thickness(0, 16, 0, 0);
        page.Children.Add(laterCard);
        _search.TextChanged += (_, _) => Filter();
        Content = page;
        Loaded += (_, _) => { shell.Registry.Changed += OnRegistryChanged; Refresh(); };
        Unloaded += (_, _) => shell.Registry.Changed -= OnRegistryChanged;
        Refresh();
    }

    private async void SetInstalled(FeatureEntry entry, ToggleButton toggle, bool on)
    {
        if (_syncing)
            return;
        toggle.IsEnabled = false;
        try
        {
            await _shell.Registry.SetInstalledAsync(entry.Id, on);
            _message.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            Log.Error($"Changing {entry.Id} install state failed", ex);
            SettingsUi.Show(_message, $"Couldn't change {entry.Descriptor.Name}. Try again.", problem: true);
        }
        finally
        {
            toggle.IsEnabled = true;
            Refresh();
        }
    }

    private void OnRegistryChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(Refresh);

    private void Refresh()
    {
        _syncing = true;
        foreach (var (entry, row, chip, toggle) in _rows)
        {
            toggle.IsChecked = entry.IsInstalled;
            toggle.Content = entry.IsInstalled ? "On" : "Off";
            chip.IsOn = entry.IsInstalled;
            row.Description = entry.LastError is not null ? "Couldn't start. Turn off and try again." : entry.Descriptor.Description;
        }
        _count.Text = $"{_shell.Registry.InstalledCount} on";
        _syncing = false;
        Filter();
    }

#if DEBUG
    internal void VerifySearchSnapshot()
    {
        _search.Text = "no-such-feature-9a845";
        if (_empty.Visibility != Visibility.Visible || ((FrameworkElement)_coming.Parent).Visibility != Visibility.Collapsed)
            throw new InvalidOperationException("Feature search did not show its empty state.");
        _search.Text = "Audio routing";
        if (!_coming.IsExpanded || _later.Where(item => item.Label.Visibility == Visibility.Visible).Count() != 1)
            throw new InvalidOperationException("Feature search did not reveal the matching upcoming feature.");
        _search.Clear();
        _coming.IsExpanded = false;
    }
#endif

    private void Filter()
    {
        string query = _search.Text.Trim();
        int visible = 0;
        foreach (var (entry, row, _, _) in _rows)
        {
            bool match = entry.Descriptor.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase)
                || entry.Descriptor.Description.Contains(query, StringComparison.CurrentCultureIgnoreCase);
            ((FrameworkElement)row.Parent).Visibility = match ? Visibility.Visible : Visibility.Collapsed;
            if (match) visible++;
        }
        _available.Visibility = _availableHeading.Visibility = visible > 0 ? Visibility.Visible : Visibility.Collapsed;
        int later = 0;
        foreach (var (entry, label, _) in _later)
        {
            bool match = entry.Descriptor.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase);
            label.Visibility = match ? Visibility.Visible : Visibility.Collapsed;
            if (match) later++;
        }
        foreach (var heading in _later.Select(item => item.Heading).Distinct())
            heading.Visibility = _later.Any(item => item.Heading == heading && item.Label.Visibility == Visibility.Visible) ? Visibility.Visible : Visibility.Collapsed;
        ((FrameworkElement)_coming.Parent).Visibility = later > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (query.Length > 0)
            _coming.IsExpanded = true;
        _empty.Visibility = visible + later == 0 ? Visibility.Visible : Visibility.Collapsed;
    }
}
