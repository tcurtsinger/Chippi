// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using Chippi.Core.Actions;
using Chippi.Features.RadialMenu.Views;
using Chippi.Island.Tests;
using Chippi.UI.Controls;

namespace Chippi.Features.RadialMenu.Tests;

/// <summary>The shared action picker (Chippi.UI) as Settings › Radial menu shows it, with the Radial menu's Submenu.</summary>
public sealed class PickerTests
{
    [Fact]
    public void Every_glyph_the_catalog_names_is_drawn()
    {
        var names = ActionCatalog.Glyphs
            .Concat(ActionCatalog.Media.Select(m => m.Glyph))
            .Concat(ActionCatalog.Window.Select(m => m.Glyph))
            .Concat(ActionCatalog.Selection.Select(m => m.Glyph))
            .Concat(Enum.GetValues<ActionKind>().Select(k => ActionCatalog.GlyphOf(new ChippiAction { Kind = k })))
            .Concat(["File", "Folder", "LayoutGrid"]);
        Assert.All(names, name => Assert.True(name == nameof(LucideIcons.LayoutGrid) || LucideIcons.Get(name) != LucideIcons.LayoutGrid, name));
    }

    [Fact]
    public void Picking_a_kind_an_action_a_name_and_a_submenu_edits_the_selected_slice() => WpfTestThread.Run(() =>
    {
        var f = new Fixture { Slices = [new Slice { Kind = SliceKind.Media, Value = "PlayPause", Name = "Play/pause" }] };
        f.StartAsync().GetAwaiter().GetResult();
        var page = new RadialMenuSettingsPage(f.Module);
        page.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
        try
        {
            var kind = Find<ComboBox>(page, "Kind");
            Assert.Equal([.. ActionCatalog.Kinds.Select(k => k.Name), "Submenu"], kind.ItemsSource.Cast<string>());

            Pick(Find<ComboBox>(page, "Kind"), "Window");
            Assert.Equal(new ChippiAction { Kind = ActionKind.Window, Value = "SnapLeft", Name = "Snap left" }, Only(f).ToAction());

            Pick(Find<ComboBox>(page, "Action"), "Close");
            Assert.Equal(new ChippiAction { Kind = ActionKind.Window, Value = "Close", Name = "Close" }, Only(f).ToAction());

            // A name typed by hand stays when the action changes.
            Type(Find<TextBox>(page, "Name"), "Bye");
            Pick(Find<ComboBox>(page, "Action"), "Minimize");
            Assert.Equal(new ChippiAction { Kind = ActionKind.Window, Value = "Minimize", Name = "Bye" }, Only(f).ToAction());

            Pick(Find<ComboBox>(page, "Kind"), "Submenu");
            Assert.Equal(SliceKind.Submenu, Only(f).Kind);
            Assert.Equal("Submenu", Only(f).Name);
            Assert.NotNull(Find<Button>(page, "Open Submenu"));

            // A submenu's name is its own; cleared, it's the kind's again.
            Type(Find<TextBox>(page, "Name"), "More");
            Assert.Equal("More", Only(f).Name);
            Type(Find<TextBox>(page, "Name"), "");
            Assert.Equal("Submenu", Only(f).Name);

            Pick(Find<ComboBox>(page, "Kind"), "Chippi");
            Assert.Equal(new ChippiAction { Kind = ActionKind.Chippi, Value = "keep-awake/toggle", Name = "Keep awake" }, Only(f).ToAction());
        }
        finally
        {
            page.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
            f.Module.StopAsync(default).GetAwaiter().GetResult();
        }
    });

    private static Slice Only(Fixture f) => Assert.Single(f.Settings.Value.Slices);

    private static void Pick(ComboBox combo, string item) => combo.SelectedItem = item;

    /// <summary>Types the text and lets go of the field, as a click elsewhere does.</summary>
    private static void Type(TextBox box, string text)
    {
        box.Text = text;
        box.RaiseEvent(new KeyboardFocusChangedEventArgs(Keyboard.PrimaryDevice, 0, box, null) { RoutedEvent = Keyboard.LostKeyboardFocusEvent });
    }

    private static T Find<T>(DependencyObject root, string name) where T : FrameworkElement =>
        All(root).OfType<T>().First(e => AutomationProperties.GetName(e) == name);

    private static IEnumerable<DependencyObject> All(DependencyObject parent)
    {
        yield return parent;
        foreach (var child in LogicalTreeHelper.GetChildren(parent).OfType<DependencyObject>())
            foreach (var found in All(child))
                yield return found;
    }
}
