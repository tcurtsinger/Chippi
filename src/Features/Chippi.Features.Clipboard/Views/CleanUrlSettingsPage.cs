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
    private readonly TextBox _field = new() { Tag = "example.org", VerticalContentAlignment = VerticalAlignment.Center };
    private readonly Button _addSite;
    private readonly Button _addConfirm = SettingsUi.Button("Add", "PrimaryButtonStyle");
    private readonly TextBlock _siteMessage = Warning();
    private readonly TextBlock _saveError = Warning();
    private readonly CleanLinkField _link = new(14, 20);
    private readonly MiddleTrimmedText _clean = new(14, 20);
    private readonly TextBlock _copyError = SettingsUi.Text("", "CaptionTextStyle");
    private readonly Button _copy = SettingsUi.Button("Copy", "PrimaryButtonStyle");
    private readonly FrameworkElement _addRow;
    private bool _adding;
    private int _copyVersion;

    public CleanUrlSettingsPage(CleanUrlModule module)
    {
        _module = module;
        var automatic = SettingsUi.Switch("Automatic cleaning", () => _module.Settings.Automatic, on => _module.Save(_module.Settings with { Automatic = on }), out _syncAutomatic);
        _addSite = SettingsLayout.AddButton("site", () =>
        {
            if (!_adding) _field.Text = "";
            _adding = true;
            Refresh();
            _field.Focus();
        });

        _field.SetResourceReference(StyleProperty, "TextFieldStyle");
        AutomationProperties.SetName(_field, "Site");
        _field.TextChanged += (_, _) => ValidateSite();
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
        _addRow = AddRow();
        _sites.Children.Add(_addRow);

        Content = new StackPanel
        {
            Children =
            {
                Cleaner(),
                Spaced(SettingsLayout.Card(SettingsLayout.Row("Automatic cleaning", automatic))),
                _saveError,
                SettingsLayout.Section("Sites to skip", _addSite),
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
        _link.Field.TextChanged += (_, _) => ShowClean();
        _link.Field.KeyDown += (_, e) =>
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
        AutomationProperties.SetLiveSetting(_copyError, AutomationLiveSetting.Polite);
        _copyError.SetResourceReference(TextBlock.ForegroundProperty, "WarningTextBrush");
        _copyError.Margin = new Thickness(0, 4, 0, 0);
        ShowClean();
        var output = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { _clean, _copyError } };
        return SettingsLayout.Card(
            new Border { Padding = SettingsLayout.RowPadding, MinHeight = SettingsLayout.RowHeight, Child = _link },
            new Border { Padding = SettingsLayout.RowPadding, MinHeight = SettingsLayout.RowHeight,
                Child = new DockPanel { Children = { _copy, output } } });
    }

    /// <summary>The clean link, or why there is none; Copy only when something was removed.</summary>
    private void ShowClean()
    {
        _copyVersion++;
        _copyError.Visibility = Visibility.Collapsed;
        _copyError.Text = "";
        string text = _link.Field.Text;
        var result = UrlCleaner.Clean(text);
        _clean.FullText = result is { RemovedParameters.Count: > 0 } ? result.Url
            : result is not null ? "Nothing to remove"
            : text.Trim().Length > 0 ? "Not a web link" : "";
        _clean.SetResourceReference(ForegroundProperty, result is { RemovedParameters.Count: > 0 } ? "TextPrimaryBrush" :
            result is null && text.Trim().Length > 0 ? "WarningTextBrush" : "TextSecondaryBrush");
        _clean.Visibility = _clean.FullText.Length > 0 ? Visibility.Visible : Visibility.Hidden;
        _copy.IsEnabled = result is { RemovedParameters.Count: > 0 };
        _copy.Content = "Copy";
    }

    private async void Copy()
    {
        if (UrlCleaner.Clean(_link.Field.Text) is not { RemovedParameters.Count: > 0 } result || _module.Hub is not { } hub)
            return;
        int version = ++_copyVersion;
        var status = await hub.WriteAsync(ClipContent.FromText(result.Url));
        if (version == _copyVersion)
        {
            _copy.Content = status == ClipStatus.Ok ? "Copied" : "Copy";
            _copyError.Text = status == ClipStatus.Ok ? "" : ClipboardFeedback.ForCopy(status);
            _copyError.Visibility = status == ClipStatus.Ok ? Visibility.Collapsed : Visibility.Visible;
        }
        // Chippi's own write isn't recorded by the listener; History lists the cleaned link (notes #26).
        if (status == ClipStatus.Ok && _module.History?.History is { } history)
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
        _saveError.Text = _module.SettingsError ?? "";
        _saveError.Visibility = _saveError.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        _addRow.Visibility = _adding ? Visibility.Visible : Visibility.Collapsed;
        ValidateSite();
        // Leave the editor in the tree so a settings notification cannot take its focus or selection.
        if (_sites.Children.Count > 1)
            _sites.Children.RemoveRange(1, _sites.Children.Count - 1);
        double opacity = _module.Settings.Automatic ? 1 : 0.6;
        foreach (var site in _module.Settings.SitesToSkip)
        {
            var row = SettingsUi.ListRow(null, site, null, () => Save(_module.Settings.SitesToSkip.Where(s => s != site)));
            row.Opacity = opacity;
            _sites.Children.Add(row);
        }
        if (!_adding && _module.Settings.SitesToSkip.Count == 0)
        {
            var empty = SettingsLayout.Empty("No sites");
            empty.Opacity = opacity;
            _sites.Children.Add(empty);
        }
    }

    private FrameworkElement AddRow()
    {
        var cancel = SettingsUi.Button("Cancel");
        cancel.Margin = new Thickness(8, 0, 0, 0);
        cancel.Click += (_, _) => Cancel();
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Children = { _addConfirm, cancel } };
        DockPanel.SetDock(buttons, Dock.Right);
        _field.Margin = new Thickness(0, 0, 12, 0);
        var fields = new DockPanel { Children = { buttons, _field } };
        return new Border { MinHeight = SettingsLayout.RowHeight, Padding = SettingsLayout.RowPadding,
            Child = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { fields, _siteMessage } } };
    }

    private static TextBlock Warning()
    {
        var message = SettingsUi.Text("", "CaptionTextStyle");
        message.SetResourceReference(TextBlock.ForegroundProperty, "WarningTextBrush");
        AutomationProperties.SetLiveSetting(message, AutomationLiveSetting.Polite);
        message.Margin = new Thickness(0, 4, 0, 0);
        return message;
    }

    private void ValidateSite()
    {
        string? site = Sites.Normalize(_field.Text);
        bool duplicate = site is not null && _module.Settings.SitesToSkip.Contains(site, StringComparer.OrdinalIgnoreCase);
        _siteMessage.Text = duplicate ? "Site already listed" : site is null && _field.Text.Trim().Length > 0 ? "Enter a host, like example.org" : "";
        _siteMessage.Visibility = _siteMessage.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        AutomationProperties.SetHelpText(_field, _siteMessage.Text);
        _addConfirm.IsEnabled = site is not null && !duplicate;
    }

    private void Add()
    {
        if (!_addConfirm.IsEnabled || Sites.Normalize(_field.Text) is not { } site)
            return;
        if (Save([site, .. _module.Settings.SitesToSkip])) Cancel();
    }

    private void Cancel()
    {
        _adding = false;
        Refresh();
        _addSite.Focus();
    }

    private bool Save(IEnumerable<string> sites) => _module.Save(_module.Settings with { SitesToSkip = [.. sites] });
}
