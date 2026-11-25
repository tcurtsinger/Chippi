// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using Chippi.Features.MacDesktop.Shell;
using Chippi.UI.Themes;

namespace Chippi.Features.MacDesktop.MenuBar;

/// <summary>
/// A question of the menu bar or the dock: the verb and its object ("Restart?", "Empty Recycle Bin?") with Cancel and the
/// verb, on the menus' card beside what asked. It holds the keyboard while it's open, as a menu does, so Esc and a click
/// elsewhere answer Cancel. One at a time: a new one replaces the one that's open.
/// </summary>
internal static class BarQuestion
{
    private static Popup? _open;

    public static bool IsOpen => _open is { IsOpen: true };

    /// <summary>Closes the open question as Cancel does.</summary>
    public static void Close()
    {
        if (_open is { } open)
            open.IsOpen = false;
    }

    /// <summary>
    /// Asks <paramref name="question"/> beside <paramref name="anchor"/>; <paramref name="yes"/> runs on
    /// <paramref name="verb"/>. Without a verb it's a notice ("End task failed") with OK alone.
    /// </summary>
    public static void Ask(FrameworkElement anchor, PlacementMode placement, string question, string? verb, Action? yes = null)
    {
        if (_open is { } replaced)
        {
            _open = null; // replaced, not answered: the keyboard stays held for the new one
            replaced.IsOpen = false;
        }
        var cancel = new Button { Content = verb is null ? "OK" : "Cancel" };
        cancel.SetResourceReference(FrameworkElement.StyleProperty, "SecondaryButtonStyle");
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0), Children = { cancel } };
        Button? act = null;
        if (verb is not null)
        {
            act = new Button { Content = verb, Margin = new Thickness(8, 0, 0, 0) };
            act.SetResourceReference(FrameworkElement.StyleProperty, "CriticalButtonStyle");
            buttons.Children.Add(act);
        }
        var text = new TextBlock { Text = question, FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis };
        text.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
        var card = new Border
        {
            Width = 260, CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Margin = new Thickness(16),
            Effect = new DropShadowEffect { BlurRadius = 32, ShadowDepth = 8, Direction = 270, Opacity = ThemeManager.IsLight ? 0.14 : 0.28 },
            Child = new StackPanel { Margin = new Thickness(16), Children = { text, buttons } },
        };
        card.SetResourceReference(Border.BackgroundProperty, "FlyoutBrush");
        card.SetResourceReference(Border.BorderBrushProperty, "BorderStrongBrush");
        var popup = new Popup
        {
            PlacementTarget = anchor,
            Placement = placement,
            StaysOpen = false, AllowsTransparency = true, PopupAnimation = PopupAnimation.None, Child = card,
        };
        cancel.Click += (_, _) => popup.IsOpen = false;
        if (act is not null)
            act.Click += (_, _) =>
            {
                popup.IsOpen = false;
                yes?.Invoke();
            };
        card.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape)
                return;
            e.Handled = true;
            popup.IsOpen = false;
        };
        var focus = MenuFocus.Shared;
        popup.Closed += (_, _) =>
        {
            if (_open != popup)
                return;
            _open = null;
            focus.Release(popup);
        };
        _open = popup;
        focus.Hold(popup, Native.Cursor(), () => popup.IsOpen = false);
        popup.IsOpen = true;
        anchor.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => { if (popup.IsOpen) cancel.Focus(); });
    }
}
