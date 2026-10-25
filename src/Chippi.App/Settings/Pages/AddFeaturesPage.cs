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
    private readonly List<(FeatureEntry Entry, Grid Row, TextBlock Caption, IconChip Chip, ToggleButton Switch, ContentControl Action)> _rows = [];
    private readonly Dictionary<string, IFeatureRequirement> _requirements = [];
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
            var heading = new WrapPanel();
            heading.Children.Add(SettingsUi.Styled(new TextBlock { Text = entry.Descriptor.Name, VerticalAlignment = VerticalAlignment.Center }, "BodyTextStyle"));
            foreach (var badge in entry.Descriptor.Badges.Where(b => b.Kind != BadgeKind.Source))
                heading.Children.Add(new Badge { Text = badge.Text, Kind = badge.Kind, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
            var caption = SettingsUi.Styled(new TextBlock { Text = entry.Descriptor.Description, TextWrapping = TextWrapping.Wrap }, "CaptionTextStyle");
            var labels = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 16, 0), Children = { heading, caption } };
            var row = new Grid { Margin = new Thickness(0, 10, 16, 10) };
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            // The switch, or what a feature with an outside requirement shows instead (Install Driver).
            var action = new ContentControl { Content = toggle, VerticalAlignment = VerticalAlignment.Center, Focusable = false };
            Grid.SetColumn(action, 1);
            row.Children.Add(labels);
            row.Children.Add(action);
            AutomationProperties.SetName(toggle, entry.Descriptor.Name);
            AutomationProperties.SetHelpText(toggle, entry.Descriptor.Description);
            var layout = new DockPanel { Margin = new Thickness(16, 0, 0, 0) };
            DockPanel.SetDock(chip, Dock.Left);
            layout.Children.Add(chip);
            layout.Children.Add(row);
            _available.Children.Add(layout);
            _rows.Add((entry, row, caption, chip, toggle, action));
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
        _coming = new ExpanderRow { Header = "Coming later", Description = $"{placeholders.Count} features", Content = names };
        var laterCard = SettingsUi.Group(_coming);
        laterCard.Margin = new Thickness(0, 16, 0, 0);
        page.Children.Add(laterCard);
        _search.TextChanged += (_, _) => Filter();
        Content = page;
        Loaded += (_, _) => { shell.Registry.Changed += OnRegistryChanged; WatchRequirements(); Refresh(); };
        Unloaded += (_, _) => { shell.Registry.Changed -= OnRegistryChanged; DropRequirements(); };
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
            SettingsUi.Show(_message, $"Couldn't change {entry.Descriptor.Name}", problem: true);
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
        foreach (var (entry, _, caption, chip, toggle, action) in _rows)
        {
            toggle.IsChecked = entry.IsInstalled;
            toggle.Content = entry.IsInstalled ? "On" : "Off";
            chip.IsOn = entry.IsInstalled;
            caption.Text = entry.LastError is not null ? "Couldn't start" : entry.Descriptor.Description;
            ShowRequirement(entry, caption, toggle, action);
        }
        _count.Text = $"{_shell.Registry.InstalledCount} on";
        _syncing = false;
        Filter();
    }

    // A requirement watches devices only while this page is shown (architecture §6).
    private void WatchRequirements()
    {
        foreach (var (entry, _, _, _, _, _) in _rows)
        {
            if (entry.Descriptor.Requirement is not { } create || _requirements.ContainsKey(entry.Id))
                continue;
            try
            {
                var requirement = create();
                requirement.StateChanged += OnRequirementChanged;
                _requirements[entry.Id] = requirement;
            }
            catch (Exception ex)
            {
                Log.Error($"Checking what {entry.Id} needs failed", ex);
            }
        }
    }

    private void DropRequirements()
    {
        foreach (var requirement in _requirements.Values)
        {
            requirement.StateChanged -= OnRequirementChanged;
            requirement.Dispose();
        }
        _requirements.Clear();
    }

    private void OnRequirementChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(Refresh);

    /// <summary>
    /// Brief §2, Install Driver: the switch only once the requirement is ready. Before that the row
    /// offers Install Driver, shows it busy, asks for a restart, or offers Sound settings when the
    /// driver is there but disabled.
    /// </summary>
    private void ShowRequirement(FeatureEntry entry, TextBlock caption, ToggleButton toggle, ContentControl action)
    {
        if (!_requirements.TryGetValue(entry.Id, out var requirement) || requirement.State == RequirementState.Ready)
        {
            action.Content = toggle;
            return;
        }
        switch (requirement.State)
        {
            case RequirementState.Missing:
                action.Content = RequirementButton("Install Driver", entry, () => InstallAsync(entry, requirement), primary: true);
                break;
            case RequirementState.Installing:
                var busy = SettingsUi.Button("Installing");
                busy.IsEnabled = false;
                action.Content = busy;
                break;
            case RequirementState.RestartNeeded:
                action.Content = SettingsUi.Styled(new TextBlock { Text = "Restart to finish", VerticalAlignment = VerticalAlignment.Center }, "CaptionTextStyle");
                break;
            case RequirementState.Disabled:
                caption.Text = "Driver disabled";
                action.Content = RequirementButton("Sound settings", entry, () => { requirement.OpenSettings(); return Task.CompletedTask; });
                break;
            default:
                action.Content = SettingsUi.Styled(new TextBlock { Text = "Not supported", VerticalAlignment = VerticalAlignment.Center }, "CaptionTextStyle");
                break;
        }
    }

    private Button RequirementButton(string text, FeatureEntry entry, Func<Task> click, bool primary = false)
    {
        var button = SettingsUi.Button(text, primary);
        AutomationProperties.SetName(button, $"{text}, {entry.Descriptor.Name}");
        button.Click += async (_, _) =>
        {
            button.IsEnabled = false;
            try
            {
                await click();
                _message.Visibility = Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                Log.Error($"{text} for {entry.Id} failed", ex);
                SettingsUi.Show(_message, ex is InvalidOperationException or Chippi.Features.Sound.Processing.DriverPackageException ? ex.Message : $"Couldn't run {text}", problem: true);
            }
            finally
            {
                button.IsEnabled = true;
                Refresh();
            }
        };
        return button;
    }

    private static Task InstallAsync(FeatureEntry entry, IFeatureRequirement requirement)
    {
        Log.Info($"Install Driver for {entry.Id}");
        return requirement.InstallAsync(CancellationToken.None);
    }

#if DEBUG
    /// <summary>Snapshot runs: shows a requirement's state on a row without touching devices.</summary>
    internal void ShowRequirementSnapshot(string id, IFeatureRequirement requirement)
    {
        DropRequirements();
        _requirements[id] = requirement;
        Refresh();
    }

    internal void VerifySearchSnapshot()
    {
        _search.Text = "no-such-feature-9a845";
        if (_empty.Visibility != Visibility.Visible || ((FrameworkElement)_coming.Parent).Visibility != Visibility.Collapsed)
            throw new InvalidOperationException("Feature search did not show its empty state.");
        _search.Text = "App volume";
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
        foreach (var (entry, row, _, _, _, _) in _rows)
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
