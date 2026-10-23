// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Chippi.Features.Downloads.Engine;
using Chippi.Features.Downloads.Model;
using Chippi.UI.Controls;

namespace Chippi.Features.Downloads.Views.Tidy;

/// <summary>
/// Tidy in the island (DESIGN.md §5 Island): the first watched folder, its mode line and Organize
/// now, in one inner card. With no folder there is nothing to show, so it is left out.
/// </summary>
public sealed class TidyIslandView : UserControl
{
    private readonly DownloadsModule _module;
    private readonly Action<DownloadsRequest> _request;
    private readonly IconChip _chip = new() { Icon = LucideIcons.Folder, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _name = new() { TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock _mode = new() { TextTrimming = TextTrimming.CharacterEllipsis };
    // Compact, so the mode line keeps its room: 30px tall, 13px text, 10px sides.
    private readonly Button _organize = new() { Content = "Organize now", Height = 30, FontSize = 13, Padding = new Thickness(10, 0, 10, 0), Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
    private DownloadsEngine? _subscribed;
    private string? _folder;

    public TidyIslandView(DownloadsModule module, Action<DownloadsRequest> request)
    {
        _module = module;
        _request = request;
        _mode.SetResourceReference(StyleProperty, "CaptionTextStyle");
        _organize.SetResourceReference(StyleProperty, "SecondaryButtonStyle");
        System.Windows.Automation.AutomationProperties.SetName(_organize, "Organize now");
        _organize.Click += (_, _) => Organize();

        // The folder chip, the name over the mode line, and Organize now on the right.
        var text = new StackPanel { Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Children = { _name, _mode } };
        Grid.SetColumn(text, 1);
        Grid.SetColumn(_organize, 2);
        var row = new Grid { ColumnDefinitions = { new() { Width = GridLength.Auto }, new(), new() { Width = GridLength.Auto } }, Children = { _chip, text, _organize } };
        var card = new Border { Child = row };
        card.SetResourceReference(StyleProperty, "IslandCardStyle");
        Content = card;
        System.Windows.Automation.AutomationProperties.SetName(this, "Tidy");

        Loaded += (_, _) =>
        {
            _subscribed = _module.Engine;
            if (_subscribed is not null)
                _subscribed.PendingChanged += OnChanged;
            Refresh();
        };
        Unloaded += (_, _) =>
        {
            if (_subscribed is not null)
                _subscribed.PendingChanged -= OnChanged;
            _subscribed = null;
        };
        Refresh();
    }

    private void OnChanged(object? sender, EventArgs e)
    {
        // Engine events can come from inside a file operation; never wait on the UI.
        if (!Dispatcher.HasShutdownStarted)
            Dispatcher.BeginInvoke(() => { if (IsLoaded) Refresh(); }, DispatcherPriority.Background);
    }

    private void Refresh()
    {
        try
        {
            var folder = _module.Settings.Folders.FirstOrDefault();
            _folder = folder?.Path;
            Visibility = folder is null ? Visibility.Collapsed : Visibility.Visible;
            if (folder is null)
                return;
            int ready = (_module.Engine?.Pending ?? []).Count(p => p.Status == PendingStatus.Ready
                && string.Equals(Path.TrimEndingDirectorySeparator(p.Folder), Path.TrimEndingDirectorySeparator(folder.Path), StringComparison.OrdinalIgnoreCase));
            _name.Text = TidyProjection.FolderName(folder.Path);
            _name.ToolTip = folder.Path;
            _mode.Text = TidyProjection.ModeLine(folder, ready);
            _chip.IsOn = folder.Mode != FolderMode.Paused;
            _organize.IsEnabled = _module.Engine is not null;
        }
        catch (ObjectDisposedException)
        {
            _organize.IsEnabled = false; // the organizer stopped
        }
    }

    private void Organize()
    {
        if (_module.Engine is null || _folder is null)
            return;
        try
        {
            _request(new DownloadsRequest(DownloadsRequestKind.Organize, Folder: _folder));
        }
        catch (Exception ex)
        {
            _mode.Text = ex.Message;
        }
    }
}
