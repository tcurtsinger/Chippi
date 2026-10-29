// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using Chippi.Core.Clipboard;
using Chippi.Features.Clipboard.CleanUrl;
using Chippi.UI.Controls;
using Chippi.UrlCleaning;

namespace Chippi.Features.Clipboard.Views;

/// <summary>
/// Settings › Clean URL (revised 2026-10-28, brief F3 #5): the island's cleaner at the top (the link field,
/// then the clean link and Copy), then Automatic cleaning, and sites to skip. Add site opens an inline row at
/// the top of the list. While automatic cleaning is off the list dims and stays editable (notes #27).
/// </summary>
public sealed class CleanUrlSettingsPage : UserControl
{
    private readonly CleanUrlModule _module;
    private readonly Card _sites = new();
    private readonly Action _syncAutomatic;
    private readonly TextBox _field = new() { MinWidth = 240, VerticalContentAlignment = VerticalAlignment.Center };
    private readonly Button _addConfirm = SettingsUi.Button("Add", "PrimaryButtonStyle");
    private readonly TextBox _link = new() { VerticalContentAlignment = VerticalAlignment.Center };
    private readonly TextBlock _clean = SettingsUi.Text("");
    private readonly Button _copy = SettingsUi.Button("Copy", "PrimaryButtonStyle");
    private DockPanel? _addRow;
    private bool _adding;

    public CleanUrlSettingsPage(CleanUrlModule module)
    {
        _module = module;
        var automatic = SettingsUi.Switch("Automatic cleaning", () => _module.Settings.Automatic, on => _module.Save(_module.Settings with { Automatic = on }), out _syncAutomatic);
        Button? add = null;
        add = SettingsLayout.AddButton("site", () =>
        {
            _adding = true;
            _field.Text = "";
            Refresh();
            Dispatcher.BeginInvoke(() => _field.Focus(), System.Windows.Threading.DispatcherPriority.Input);
        });

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

        Content = new StackPanel
        {
            Children =
            {
                Cleaner(),
                Spaced(SettingsLayout.Card(SettingsLayout.Row("Automatic cleaning", automatic))),
                SettingsLayout.Section("Sites to skip", add),
                _sites,
            },
        };
        Loaded += (_, _) =>
        {
            _module.Changed += Refresh;
            Refresh();
        };
        Unloaded += (_, _) => _module.Changed -= Refresh;
        Refresh();
    }

    /// <summary>The island's cleaner as a card: the link field, then the clean link as you type and Copy (accent).</summary>
    private Card Cleaner()
    {
        _link.SetResourceReference(StyleProperty, "TextFieldStyle");
        AutomationProperties.SetName(_link, "Link");
        _link.TextChanged += (_, _) => ShowClean();
        _link.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && _copy.IsEnabled)
            {
                Copy();
                e.Handled = true;
            }
        };
        _copy.Click += (_, _) => Copy();
        _copy.Margin = new Thickness(12, 0, 0, 0);
        DockPanel.SetDock(_copy, Dock.Right);
        AutomationProperties.SetLiveSetting(_clean, AutomationLiveSetting.Polite);
        ShowClean();
        return SettingsLayout.Card(
            new Border { Padding = SettingsLayout.RowPadding, MinHeight = SettingsLayout.RowHeight, Child = _link },
            new Border { Padding = SettingsLayout.RowPadding, MinHeight = SettingsLayout.RowHeight, Child = new DockPanel { Children = { _copy, _clean } } });
    }

    /// <summary>The clean link, or why there is none; Copy only when something was removed.</summary>
    private void ShowClean()
    {
        string text = _link.Text;
        var result = UrlCleaner.Clean(text);
        _clean.Text = result is { RemovedParameters.Count: > 0 } ? result.Url
            : result is not null ? "Nothing to remove"
            : text.Trim().Length > 0 ? "Not a web link" : "";
        _clean.SetResourceReference(TextBlock.ForegroundProperty, result is { RemovedParameters.Count: > 0 } ? "TextPrimaryBrush" : "TextSecondaryBrush");
        _clean.Visibility = _clean.Text.Length > 0 ? Visibility.Visible : Visibility.Hidden;
        _copy.IsEnabled = result is { RemovedParameters.Count: > 0 };
        _copy.Content = "Copy";
    }

    private async void Copy()
    {
        if (UrlCleaner.Clean(_link.Text) is not { RemovedParameters.Count: > 0 } result || _module.Hub is not { } hub)
            return;
        var status = await hub.WriteAsync(ClipContent.FromText(result.Url));
        if (status != ClipStatus.Ok)
        {
            _clean.Text = "Clipboard busy";
            _clean.SetResourceReference(TextBlock.ForegroundProperty, "WarningTextBrush");
            return;
        }
        _copy.Content = "Copied";
        // Chippi's own write isn't recorded by the listener; History lists the cleaned link (notes #26).
        if (_module.History?.History is { } history)
            await history.RecordAsync(result.Url);
    }

    private static Card Spaced(Card card)
    {
        card.Margin = new Thickness(0, 8, 0, 0);
        return card;
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
        if (_sites.Children.Count == 0)
            _sites.Children.Add(SettingsLayout.Empty("No sites"));
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
        return _addRow = new DockPanel { MinHeight = SettingsLayout.RowHeight, Margin = new Thickness(16, 8, 16, 8), Children = { buttons, _field } };
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
