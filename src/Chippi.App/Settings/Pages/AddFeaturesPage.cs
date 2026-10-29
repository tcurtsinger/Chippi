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
    private readonly List<(FeatureEntry Entry, SettingRow Row, ToggleButton Switch, ContentControl Action)> _rows = [];
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
            var action = new ContentControl { Content = toggle, VerticalAlignment = VerticalAlignment.Center, Focusable = false };
            var controls = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            foreach (var badge in entry.Descriptor.Badges.Where(b => b.Kind != BadgeKind.Source))
                controls.Children.Add(new Badge { Text = badge.Text, Kind = badge.Kind, Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center });
            controls.Children.Add(action);
            var row = SettingsLayout.Row(entry.Descriptor.Name, controls, entry.Descriptor.Description);
            row.Icon = LucideIcons.Get(entry.Descriptor.Icon);
            AutomationProperties.SetName(toggle, entry.Descriptor.Name);
            AutomationProperties.SetHelpText(toggle, entry.Descriptor.Description);
            _available.Children.Add(row);
            _rows.Add((entry, row, toggle, action));
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
        // A feature with something to lose asks first (Clipboard history, Phase 5 notes #56); Cancel leaves it installed.
        if (!on && entry.UninstallQuestion() is { } question && !await AskAsync(toggle, question))
        {
            Refresh();
            return;
        }
        toggle.IsEnabled = false;
        try
        {
            await _shell.Registry.SetInstalledAsync(entry.Id, on);
            _message.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            Log.Error($"Changing {entry.Id} install state failed", ex);
            SettingsUi.Show(_message, $"{entry.Descriptor.Name} failed", problem: true);
        }
        finally
        {
            toggle.IsEnabled = true;
            Refresh();
        }
    }

    /// <summary>A popover under the switch: the question, then Cancel and Uninstall (Critical). Closing it any other way cancels.</summary>
    private static Task<bool> AskAsync(ToggleButton toggle, string question)
    {
        var answer = new TaskCompletionSource<bool>();
        var popup = new Popup { PlacementTarget = toggle, Placement = PlacementMode.Bottom, StaysOpen = false, AllowsTransparency = true, HorizontalOffset = -220 };
        var cancel = SettingsUi.Button("Cancel");
        cancel.Click += (_, _) => popup.IsOpen = false;
        var uninstall = SettingsUi.Styled(new Button { Content = "Uninstall", Margin = new Thickness(8, 0, 0, 0) }, "CriticalButtonStyle");
        uninstall.Click += (_, _) =>
        {
            answer.TrySetResult(true);
            popup.IsOpen = false;
        };
        var body = new StackPanel
        {
            Margin = new Thickness(16),
            Children =
            {
                SettingsUi.Styled(new TextBlock { Text = question }, "BodyTextStyle"),
                new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0), Children = { cancel, uninstall } },
            },
        };
        var panel = new Border { MinWidth = 260, CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Child = body };
        panel.SetResourceReference(Border.BackgroundProperty, "FlyoutBrush");
        panel.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        popup.Child = panel;
        popup.Opened += (_, _) => cancel.Focus();
        popup.Closed += (_, _) => answer.TrySetResult(false);
        popup.IsOpen = true;
        return answer.Task;
    }

    private void OnRegistryChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(Refresh);

    private static string? FailureReason(Exception error) => error switch
    {
        UnauthorizedAccessException => "Access denied",
        System.IO.IOException => "File unavailable",
        _ => null,
    };

    private void Refresh()
    {
        _syncing = true;
        foreach (var (entry, row, toggle, action) in _rows)
        {
            toggle.IsChecked = entry.IsInstalled;
            toggle.Content = entry.IsInstalled ? "On" : "Off";
            row.Description = entry.LastError is { } error ? FailureReason(error) is { } reason ? "Start failed \u00b7 " + reason : "Start failed" : entry.Descriptor.Description;
            ShowRequirement(entry, row, toggle, action);
            row.ApplyTemplate();
            if (row.Template?.FindName("Caption", row) is TextBlock caption)
                caption.SetResourceReference(TextBlock.ForegroundProperty, entry.LastError is not null || _requirements.TryGetValue(entry.Id, out var requirement) && requirement.State == RequirementState.Disabled ? "WarningTextBrush" : "TextSecondaryBrush");
        }
        _count.Text = $"{_shell.Registry.InstalledCount} on";
        _syncing = false;
        Filter();
    }

    // A requirement watches devices only while this page is shown (architecture §6).
    private void WatchRequirements()
    {
        foreach (var (entry, _, _, _) in _rows)
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
    private void ShowRequirement(FeatureEntry entry, SettingRow row, ToggleButton toggle, ContentControl action)
    {
        if (!_requirements.TryGetValue(entry.Id, out var requirement) || requirement.State == RequirementState.Ready)
        {
            action.Content = toggle;
            return;
        }
        switch (requirement.State)
        {
            case RequirementState.Missing:
                action.Content = RequirementButton("Install driver", entry, () => InstallAsync(entry, requirement), primary: true);
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
                row.Description = "Driver disabled";
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
                SettingsUi.Show(_message, ex is InvalidOperationException or Chippi.Features.Sound.Processing.DriverPackageException ? ex.Message : $"{text} failed", problem: true);
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

#endif

    internal void VerifySearchSnapshot()
    {
        _search.Text = "no-such-feature-9a845";
        if (_empty.Visibility != Visibility.Visible || ((FrameworkElement)_coming.Parent).Visibility != Visibility.Collapsed)
            throw new InvalidOperationException("Feature search did not show its empty state.");
        _search.Text = "Quick toggles"; // Still upcoming; Clipboard history shipped in Phase 5.
        if (!_coming.IsExpanded || _later.Where(item => item.Label.Visibility == Visibility.Visible).Count() != 1)
            throw new InvalidOperationException("Feature search did not reveal the matching upcoming feature.");
        _search.Clear();
        _coming.IsExpanded = false;
    }

    private void Filter()
    {
        string query = _search.Text.Trim();
        int visible = 0;
        foreach (var (entry, row, _, _) in _rows)
        {
            bool match = entry.Descriptor.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase)
                || entry.Descriptor.Description.Contains(query, StringComparison.CurrentCultureIgnoreCase);
            row.Visibility = match ? Visibility.Visible : Visibility.Collapsed;
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
