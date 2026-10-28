// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using Chippi.Features.Clipboard.CleanUrl;
using Chippi.UI.Controls;

namespace Chippi.Features.Clipboard.Views;

/// <summary>
/// Settings › Clean URL (design notes #54): Automatic cleaning, and sites to skip. Add site opens an
/// inline row at the top of the list. While automatic cleaning is off the list dims and stays editable (notes #27).
/// </summary>
public sealed class CleanUrlSettingsPage : UserControl
{
    private readonly CleanUrlModule _module;
    private readonly Card _sites = new();
    private readonly Action _syncAutomatic;
    private readonly TextBox _field = new() { MinWidth = 240, VerticalContentAlignment = VerticalAlignment.Center };
    private readonly Button _addConfirm = SettingsUi.Button("Add", "PrimaryButtonStyle");
    private DockPanel? _addRow;
    private bool _adding;

    public CleanUrlSettingsPage(CleanUrlModule module)
    {
        _module = module;
        var automatic = SettingsUi.Switch("Automatic cleaning", () => _module.Settings.Automatic, on => _module.Save(_module.Settings with { Automatic = on }), out _syncAutomatic);
        var first = new Card { Children = { new SettingRow { Header = "Automatic cleaning", Content = automatic } } };
        var add = SettingsUi.Button("Add site");
        add.Click += (_, _) =>
        {
            _adding = true;
            _field.Text = "";
            Refresh();
            Dispatcher.BeginInvoke(() => _field.Focus(), System.Windows.Threading.DispatcherPriority.Input);
        };

        _field.SetResourceReference(StyleProperty, "TextFieldStyle");
        AutomationProperties.SetName(_field, "Site");
        _field.TextChanged += (_, _) => _addConfirm.IsEnabled = Sites.Normalize(_field.Text) is not null;
        _field.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
                Add();
            else if (e.Key == Key.Escape)
                Cancel();
            else
                return;
            e.Handled = true;
        };
        _addConfirm.IsEnabled = false;
        _addConfirm.Click += (_, _) => Add();

        Content = new StackPanel { Children = { first, SettingsUi.Heading("Sites to skip", add), _sites } };
        Loaded += (_, _) =>
        {
            _module.Changed += Refresh;
            Refresh();
        };
        Unloaded += (_, _) => _module.Changed -= Refresh;
        Refresh();
    }

    private void Refresh()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(Refresh);
            return;
        }
        _syncAutomatic();
        _sites.Children.Clear();
        if (_adding)
            _sites.Children.Add(AddRow());
        foreach (var site in _module.Settings.SitesToSkip)
            _sites.Children.Add(SettingsUi.ListRow(null, site, null, () => Save(_module.Settings.SitesToSkip.Where(s => s != site))));
        _sites.Visibility = _sites.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        _sites.Opacity = _module.Settings.Automatic ? 1 : 0.6;
    }

    /// <summary>One row for the page's life: Refresh clears the list it sits in, so it can always go back.</summary>
    private DockPanel AddRow()
    {
        if (_addRow is not null)
            return _addRow;
        var cancel = SettingsUi.Button("Cancel");
        cancel.Margin = new Thickness(8, 0, 0, 0);
        cancel.Click += (_, _) => Cancel();
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Children = { _addConfirm, cancel } };
        DockPanel.SetDock(buttons, Dock.Right);
        _field.Margin = new Thickness(0, 0, 12, 0);
        return _addRow = new DockPanel { MinHeight = 60, Margin = new Thickness(16, 0, 16, 0), Children = { buttons, _field } };
    }

    private void Add()
    {
        if (Sites.Normalize(_field.Text) is not { } site)
            return;
        _adding = false;
        Save([site, .. _module.Settings.SitesToSkip]);
    }

    private void Cancel()
    {
        _adding = false;
        Refresh();
    }

    private void Save(IEnumerable<string> sites) => _module.Save(_module.Settings with { SitesToSkip = [.. sites] });
}
