// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Reflection;
using System.Windows;
using Chippi.Features.PlainPaste;
using Chippi.Features.Sound.Mute;
using Chippi.UI.Controls;

namespace Chippi.Island.Tests;

public sealed class ShortcutCaptureStyleTests
{
    [Fact]
    public void Both_shortcut_capture_rows_keep_the_shared_card_borderless() => WpfTestThread.Run(() =>
    {
        FrameworkElement[] views = [new PlainPasteView(new PlainPasteModule(), "Paste as plain text"), new MicMuteView(new MicMuteModule(), MicMuteSurface.ShortcutList)];
        foreach (var view in views)
        {
            view.GetType().GetMethod("BeginCapture", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(view, null);
            var card = Assert.Single(Find<Card>(view));
            Assert.Same(view.FindResource("CardRowsStyle"), card.Style);
            view.GetType().GetMethod("CancelCapture", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(view, null);
        }
    });
    private static IEnumerable<T> Find<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T match) yield return match;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach (var item in Find<T>(child)) yield return item;
    }
}
