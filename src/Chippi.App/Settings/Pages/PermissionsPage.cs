// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Controls;
using Chippi.App.Shell;
using Chippi.Core.Permissions;
using Chippi.UI.Controls;

namespace Chippi.App.Settings.Pages;

/// <summary>
/// Permissions (brief §4.5): each capability any feature declares, the installed features using
/// it, and an "Unused" flag with Dismiss when something needed it before and nothing does now.
/// </summary>
internal sealed class PermissionsPage : UserControl
{
    private readonly ShellContext _shell;
    private readonly Card _list = new();

    public PermissionsPage(ShellContext shell)
    {
        _shell = shell;
        var page = new StackPanel();
        page.Children.Add(SettingsUi.Header("Permissions",
            ""));
        page.Children.Add(_list);
        Content = page;
        Build();

        Loaded += (_, _) =>
        {
            shell.Registry.Changed += OnRegistryChanged;
            Build();
        };
        Unloaded += (_, _) => shell.Registry.Changed -= OnRegistryChanged;
    }

    private void OnRegistryChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(Build);

    private void Build()
    {
        _list.Children.Clear();
        var usages = _shell.Registry.GetPermissions().Where(u => u.UsedBy.Count > 0 || u.IsUnused).ToList();
        if (usages.Count == 0)
        {
            _list.Children.Add(SettingsLayout.Empty("Nothing needed"));
            return;
        }
        foreach (var usage in usages)
            _list.Children.Add(Row(usage));
    }

    private Border Row(CapabilityUsage usage)
    {
        var capability = usage.Capability;
        string caption = string.Join(", ", usage.UsedBy.Select(d => d.Name));

        FrameworkElement side;
        if (usage.UsedBy.Count > 0)
        {
            side = State(StatusKind.Active, "In use");
        }
        else if (usage.IsUnused)
        {
            var dismiss = SettingsUi.Button("Dismiss");
            System.Windows.Automation.AutomationProperties.SetName(dismiss, $"Dismiss unused {capability.Name}");
            dismiss.Click += (_, _) => _shell.Registry.DismissUnusedCapability(capability.Id);
            var panel = new StackPanel { Orientation = Orientation.Horizontal };
            panel.Children.Add(State(StatusKind.Attention, "Unused"));
            panel.Children.Add(dismiss);
            side = panel;
        }
        else
        {
            side = State(StatusKind.Offline, "Not in use");
        }
        var row = SettingsUi.Row(capability.Name, caption, side);
        row.MinHeight = SettingsLayout.CaptionRowHeight;
        return row;
    }

    private static StackPanel State(StatusKind kind, string text)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        panel.Children.Add(new StatusDot { Kind = kind, VerticalAlignment = VerticalAlignment.Center });
        panel.Children.Add(SettingsUi.Styled(new TextBlock { Text = text, Margin = new Thickness(7, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center }, "CaptionTextStyle"));
        return panel;
    }
}
