// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Chippi.Core.Clipboard;
using Chippi.UI.Controls;
using Chippi.UrlCleaning;

namespace Chippi.Features.Clipboard.Views;

/// <summary>
/// The manual Clean (design notes #23 to #26): one link field, the clean link as you type with what was
/// removed, and Copy. Opening fills the field from the copy when it's one web link not marked private;
/// otherwise the field is empty and has the keyboard. Sites to skip don't apply here (notes #27).
/// </summary>
internal sealed class CleanUrlView : UserControl
{
    private readonly ClipboardFeatures _features;
    private readonly TextBox _field = new() { TextWrapping = TextWrapping.Wrap, AcceptsReturn = false, MaxLines = 3, Tag = "Link" };
    private readonly TextBlock _clean = Ui.Text("", 13);
    private readonly TextBlock _removed = Ui.Muted();
    private readonly Border _result = new() { Margin = new Thickness(0, 8, 0, 0) };
    private readonly StackPanel _caution = Ui.Caution();
    private readonly StackPanel _copied = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
    private readonly Button _copy = Ui.Button("Copy", "PrimaryButtonStyle");
    private int _opening;

    public CleanUrlView(ClipboardFeatures features)
    {
        _features = features;
        _field.SetResourceReference(StyleProperty, "TextFieldStyle");
        AutomationProperties.SetName(_field, "Link");
        _field.TextChanged += (_, _) => Refresh();
        _field.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && _copy.IsEnabled)
            {
                Copy();
                e.Handled = true;
            }
        };
        _clean.TextWrapping = TextWrapping.Wrap;
        _clean.TextTrimming = TextTrimming.None;
        _removed.Margin = new Thickness(0, 4, 0, 0);
        _result.SetResourceReference(StyleProperty, "IslandCardStyle");
        _result.Child = new StackPanel { Children = { _clean, _removed } };
        _caution.Margin = new Thickness(0, 8, 0, 0);

        var check = Ui.Glyph(LucideIcons.Check, 14, "SuccessBrush");
        check.Margin = new Thickness(0, 0, 6, 0);
        _copied.Children.Add(check);
        _copied.Children.Add(Ui.Muted("Copied", 12));
        AutomationProperties.SetLiveSetting(_copied, AutomationLiveSetting.Polite);
        _copy.Click += (_, _) => Copy();
        DockPanel.SetDock(_copy, Dock.Right);
        var footer = new DockPanel { Margin = new Thickness(0, 8, 0, 0), LastChildFill = true, Children = { _copy, _copied } };

        Content = new StackPanel { Children = { _field, _caution, _result, footer } };
        Refresh();
    }

    /// <summary>The field, which takes the keyboard when the view opens (notes #58).</summary>
    public TextBox Field => _field;

    /// <summary>Each time the view opens: the copy's link in the field when it qualifies, else an empty field (notes #24).</summary>
    public async void Open()
    {
        int opening = ++_opening;
        _field.Text = "";
        if (_features.CleanUrl?.Hub is not { } hub)
            return;
        ClipRead read;
        try
        {
            read = await hub.ReadAsync();
        }
        catch (Exception)
        {
            return;
        }
        // A newer open, or typing meanwhile, wins.
        if (opening != _opening || _field.Text.Length > 0)
            return;
        if (read is { Status: ClipStatus.Ok, Privacy: ClipPrivacy.None, Content: { Text: { } text, Files: null, Image: null } } && UrlCleaner.Clean(text) is not null)
        {
            _field.Text = text.Trim();
            _field.CaretIndex = _field.Text.Length;
        }
    }

    private void Refresh()
    {
        _copied.Visibility = Visibility.Collapsed;
        string text = _field.Text;
        var result = UrlCleaner.Clean(text);
        Ui.Show(_caution, text.Trim().Length > 0 && result is null ? "Not a web link" : null);
        _result.Visibility = result is null ? Visibility.Collapsed : Visibility.Visible;
        if (result is not null && result.RemovedParameters.Count > 0)
        {
            _clean.Text = result.Url;
            _clean.SetResourceReference(TextBlock.ForegroundProperty, "IslandTextBrush");
            _removed.Text = "Removed · " + string.Join(", ", result.RemovedParameters);
            _removed.Visibility = Visibility.Visible;
        }
        else if (result is not null)
        {
            _clean.Text = "Nothing to remove";
            _clean.SetResourceReference(TextBlock.ForegroundProperty, "IslandTextTertiaryBrush");
            _removed.Visibility = Visibility.Collapsed;
        }
        _copy.IsEnabled = result is { RemovedParameters.Count: > 0 };
    }

    private async void Copy()
    {
        if (UrlCleaner.Clean(_field.Text) is not { RemovedParameters.Count: > 0 } result || _features.CleanUrl?.Hub is not { } hub)
            return;
        var status = await hub.WriteAsync(ClipContent.FromText(result.Url));
        if (status == ClipStatus.Ok)
        {
            Ui.Show(_caution, null);
            _copied.Visibility = Visibility.Visible;
            // Chippi's own write isn't recorded by the listener; History lists the cleaned link (notes #26).
            if (_features.History?.History is { } history)
                await history.RecordAsync(result.Url);
        }
        else
            Ui.Show(_caution, "Clipboard busy");
    }
}

/// <summary>Clean URL's part of the Clipboard page: the whole page while Clipboard history isn't running, else nothing (notes #23).</summary>
internal sealed class CleanUrlSection : ContentControl
{
    private readonly ClipboardFeatures _features;
    private readonly CleanUrlView _view;

    public CleanUrlSection(ClipboardFeatures features)
    {
        _features = features;
        _view = new CleanUrlView(features);
        Focusable = false;
        Loaded += (_, _) =>
        {
            _features.Changed += OnChanged;
            Refresh(opening: true);
        };
        Unloaded += (_, _) => _features.Changed -= OnChanged;
    }

    /// <summary>The link field while this section is the page (notes #58); null while History runs.</summary>
    public UIElement? InitialFocus => _features.History is null ? _view.Field : null;

    private void OnChanged() => Dispatcher.BeginInvoke(() => Refresh(opening: false));

    private void Refresh(bool opening)
    {
        bool alone = _features.History is null;
        Content = alone ? _view : null;
        Visibility = alone ? Visibility.Visible : Visibility.Collapsed;
        if (alone && opening)
            _view.Open();
    }
}
