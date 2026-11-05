// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls.Primitives;
using Chippi.Core.Diagnostics;
using Chippi.Core.Modules;
using Chippi.UI.Controls;

namespace Chippi.Features.QuickActions.Views;

/// <summary>Tiles other features add (docs/phase-6/architecture.md §3): their menus, and running their commands.</summary>
internal sealed partial class ActionsPage
{
    private readonly HashSet<string> _commandsRunning = [];
    private readonly HashSet<string> _commandsFailed = [];

    /// <summary>The open added menu's key ("input"), else null.</summary>
    internal string? Menu => _menu;

    /// <summary>An added menu: a row per command, a switch for each that turns something on and off.</summary>
    private void RenderMenu(AddedTile tile)
    {
        if (tile.Tile.View is { } view)
        {
            // Made once per opening (PressedAdded clears _built), so it reads the window in front as the menu opens.
            ListPageCount = 1;
            _listPage = 0;
            _listFailed.Visibility = Visibility.Collapsed;
            if (_built == "menu:" + tile.Key)
                return;
            _built = "menu:" + tile.Key;
            _body.Children.Clear();
            _firstRow = null;
            if (view() is UIElement element)
            {
                _body.Children.Add(element);
                // Opening it puts the keyboard on its first control, as a menu of rows does.
                _firstRow = Buttons(element).FirstOrDefault(b => b.IsEnabled);
            }
            return;
        }
        int count = tile.Commands.Count;
        ListPageCount = Math.Max(1, (count + ListRows * MaxListColumns - 1) / (ListRows * MaxListColumns));
        _listPage = Math.Clamp(_listPage, 0, ListPageCount - 1);
        string built = $"menu:{tile.Key}:{_listPage}:" + string.Join(",", tile.Commands.Select(c =>
            $"{c.Key}|{c.Name}|{c.IsOn}|{_commandsRunning.Contains(c.Key)}|{_commandsFailed.Contains(c.Key)}"));
        _listFailed.Visibility = Visibility.Collapsed;
        if (_built == built)
            return;
        var focused = CurrentListFocus();
        string? focusKey = Buttons(_body).FirstOrDefault(b => ReferenceEquals(b, focused) || b.IsKeyboardFocusWithin)?.Tag as string;
        _built = built;
        _body.Children.Clear();
        _firstRow = null;
        Rows(tile.Commands, MenuRow, "", "rows");
        if (focusKey is not null && Buttons(_body).FirstOrDefault(b => Equals(b.Tag, focusKey)) is { } target)
            SetListFocus(target);
    }

    private FrameworkElement MenuRow(ChippiCommand command)
    {
        bool failed = _commandsFailed.Contains(command.Key);
        string detail = failed ? $"{command.Name} failed" : "";
        var icon = Ui.Glyph(LucideIcons.Get(command.Icon));
        if (command.IsOn is not { } on)
        {
            var labels = Labels(command.Name, detail, failed);
            labels.Margin = new Thickness(8, 0, 0, 0);
            var button = ListButton(new System.Windows.Controls.DockPanel { Children = { icon, labels } }, command.Name);
            button.Tag = "command:" + command.Key;
            button.IsEnabled = !_commandsRunning.Contains(command.Key);
            button.Click += (_, _) => RunCommand(command);
            return button;
        }
        var toggle = new ToggleButton { IsChecked = on, VerticalAlignment = VerticalAlignment.Center, Tag = "command:" + command.Key };
        toggle.SetResourceReference(StyleProperty, "SwitchStyle");
        toggle.IsEnabled = !_commandsRunning.Contains(command.Key);
        AutomationProperties.SetName(toggle, command.Name);
        AutomationProperties.SetItemStatus(toggle, detail);
        // UI Automation's Toggle raises Checked and Unchecked, never Click.
        toggle.Checked += (_, _) => RunCommand(command);
        toggle.Unchecked += (_, _) => RunCommand(command);
        var row = Row(icon, command.Name, [toggle], detail, failed);
        // The whole row is the switch's target, as in Settings.
        row.MouseLeftButtonUp += (_, e) =>
        {
            if (toggle.IsEnabled && !toggle.IsMouseOver)
            {
                toggle.IsChecked = toggle.IsChecked != true;
                e.Handled = true;
            }
        };
        return row;
    }

    /// <summary>Runs an added command once at a time; a failure stays on its row or tile until it succeeds.</summary>
    private async void RunCommand(ChippiCommand command)
    {
        if (!_commandsRunning.Add(command.Key))
            return;
        try
        {
            await command.Run();
            _commandsFailed.Remove(command.Key);
        }
        catch (Exception ex)
        {
            Log.Warn($"Quick actions: {command.Key} failed", ex);
            _commandsFailed.Add(command.Key);
        }
        finally
        {
            _commandsRunning.Remove(command.Key);
            Render();
        }
    }
}
