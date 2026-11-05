// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors
using Chippi.Core.Shortcuts;
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
    public void The_shortcut_is_read_only_with_Edit_to_Settings_Shortcuts() => WpfTestThread.Run(() =>
    {
        var f = new PlainPasteTests.Fixture(); f.Start().GetAwaiter().GetResult();
        try
        {
            var view = Assert.IsType<PlainPasteView>(f.Ui.Page(null));
            Assert.Equal("Not set", view.Shortcut.KeysText);
            Assert.DoesNotContain(Find(view).OfType<Button>(), b => AutomationProperties.GetName(b) is "Set" or "Change" or "Remove");
            Button(view, "Edit Paste as plain text shortcut").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            Assert.Equal(["Paste as plain text"], f.ShortcutEdits);
            f.Configure();
            view = new PlainPasteView(f.Module);
            Assert.Equal("Ctrl+Alt+V", view.Shortcut.KeysText);
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
            Assert.False(f.Module.ChangeShortcut(new KeyShortcut(ShortcutModifiers.Control, 0x42)));
            var view = new PlainPasteView(f.Module);
            Assert.Contains(Find(view).OfType<TextBlock>(), b => b.Text == "Shortcut in use");
            Assert.Equal("Ctrl+Alt+V", view.Shortcut.KeysText);
        }
        finally { f.Module.StopAsync(default).GetAwaiter().GetResult(); }
    });
}
