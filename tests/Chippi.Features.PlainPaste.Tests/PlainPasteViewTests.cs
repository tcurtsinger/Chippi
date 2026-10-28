// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Chippi.Island.Tests;
using Chippi.Features.Sound.Mute;

namespace Chippi.Features.PlainPaste.Tests;

public sealed class PlainPasteViewTests
{
    private static IEnumerable<DependencyObject> Find(DependencyObject root)
    {
        yield return root;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach (var nested in Find(child)) yield return nested;
    }
    private static Button Button(DependencyObject root, string label) => Find(root).OfType<Button>().Single(b => AutomationProperties.GetName(b) == label);
    [Fact]
    public void Not_set_capture_cancel_and_remove_follow_the_approved_row() => WpfTestThread.Run(() =>
    {
        var f = new PlainPasteTests.Fixture(); f.Start().GetAwaiter().GetResult();
        try
        {
            var view = Assert.IsType<PlainPasteView>(f.Ui.Page(null));
            Assert.Contains(Find(view).OfType<TextBlock>(), b => b.Text == "Not set");
            Button(view, "Set").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            Assert.Contains(Find(view).OfType<Chippi.UI.Controls.SettingRow>(), r => r.Header == "New shortcut");
            Button(view, "Cancel").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            Assert.Null(f.Module.Settings.Shortcut); Assert.Equal(0, f.Creates);
            f.Configure();
            view = new PlainPasteView(f.Module);
            Assert.Contains(Find(view).OfType<TextBlock>(), b => b.Text == "Ctrl+Alt+V");
            Assert.NotNull(Button(view, "Change"));
            Button(view, "Remove").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            Assert.Null(f.Module.Settings.Shortcut);
            Assert.Contains(Find(view).OfType<TextBlock>(), b => b.Text == "Not set");
        }
        finally { f.Module.StopAsync(default).GetAwaiter().GetResult(); }
    });
    [Fact]
    public void Conflict_shows_its_reason_and_keeps_the_configured_keys() => WpfTestThread.Run(() =>
    {
        var f = new PlainPasteTests.Fixture(); f.Start().GetAwaiter().GetResult();
        try
        {
            f.Configure(); f.Hotkey.Available = false;
            Assert.False(f.Module.ChangeShortcut(new MicShortcut(ShortcutModifiers.Control, 0x42)));
            var view = new PlainPasteView(f.Module);
            Assert.Contains(Find(view).OfType<TextBlock>(), b => b.Text == "Shortcut in use");
            Assert.Contains(Find(view).OfType<TextBlock>(), b => b.Text == "Ctrl+Alt+V");
            Assert.NotNull(Button(view, "Change"));
        }
        finally { f.Module.StopAsync(default).GetAwaiter().GetResult(); }
    });
}
