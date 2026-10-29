// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors
using System.Windows;
using Chippi.Core.Island;
using Chippi.Core.Modules;
using Chippi.Island.Tests;
using Microsoft.Extensions.Logging.Abstractions;

namespace Chippi.Features.Scratchpad.Tests;

public sealed class ScratchpadModuleTests
{
    private sealed class Settings : IModuleSettings
    {
        public T Get<T>() where T : class, new() => throw new InvalidOperationException("Notes must not use settings");
        public void Save<T>(T value) where T : class => throw new InvalidOperationException("Notes must not use settings");
    }
    private sealed class Ui : IModuleUi
    {
        public IslandTileSpec Spec = null!;
        public IslandTileContribution Tile = null!;
        public IIslandTile AddIslandTile(IslandTileSpec spec) { Spec = spec; return Tile = new IslandTileContribution("scratchpad", spec); }
        public Func<string?, object>? Page;
        public void SetSettingsPage(Func<string?, object> view) => Page = view; // Settings › Scratchpad (brief F3 #10)
    }
    private sealed class Store : IScratchpadStore
    {
        public ScratchpadDocument Document = ScratchpadDocument.Empty();
        public ScratchpadDocument Load() => Document;
        public void Save(ScratchpadDocument doc) => Document = doc;
        public void Dispose() { }
    }
    [Fact]
    public void Tile_shows_the_loaded_count_and_updates_after_a_new_note() => WpfTestThread.Run(() =>
    {
        var store = new Store { Document = ScratchpadDocument.Empty().Add() };
        var ui = new Ui();
        var module = new ScratchpadModule(_ => store);
        var context = new ModuleContext("scratchpad", new Settings(), NullLogger.Instance, new IslandActivityHub().CreatePublisher("scratchpad"), ui);
        module.StartAsync(context, default).GetAwaiter().GetResult();
        try
        {
            Assert.Equal("2 notes", ui.Tile.Status);
            var page = Assert.IsType<ScratchpadPage>(ui.Spec.CreateSection());
            using var changed = new ManualResetEventSlim();
            ui.Tile.StatusChanged += (_, _) => { if (ui.Tile.Status == "3 notes") changed.Set(); };
            var add = Descendants(page).OfType<System.Windows.Controls.Button>().Single(b => System.Windows.Automation.AutomationProperties.GetName(b) == "New note");
            add.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            Assert.True(changed.Wait(TimeSpan.FromSeconds(3)));
            Assert.Equal("3 notes", ui.Tile.Status);
        }
        finally { module.StopAsync(default).GetAwaiter().GetResult(); }
    });
    [Fact]
    public void Settings_page_shares_the_islands_notes_so_an_edit_shows_in_both() => WpfTestThread.Run(() =>
    {
        var store = new Store();
        var ui = new Ui();
        var module = new ScratchpadModule(_ => store);
        var context = new ModuleContext("scratchpad", new Settings(), NullLogger.Instance, new IslandActivityHub().CreatePublisher("scratchpad"), ui);
        module.StartAsync(context, default).GetAwaiter().GetResult();
        try
        {
            var island = Assert.IsType<ScratchpadPage>(ui.Spec.CreateSection());
            var settings = Assert.IsType<ScratchpadSettingsPage>(ui.Page!(null));
            island.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
            settings.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
            Flush();
            Assert.Equal("Saved", Assert.IsType<System.Windows.Controls.StackPanel>(Chippi.UI.Controls.SettingsLayout.GetTitleAside(settings)).Children.OfType<System.Windows.Controls.TextBlock>().Single().Text);
            Editor(settings).Text = "Book a room for Thursday";
            Flush();
            Assert.Equal("Book a room for Thursday", Editor(island).Text);
            Editor(island).Text = "Book a room for Friday";
            Flush();
            Assert.Equal("Book a room for Friday", Editor(settings).Text);

            var add = Descendants(settings).OfType<System.Windows.Controls.Button>().Single(b => System.Windows.Automation.AutomationProperties.GetName(b) == "New note");
            add.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            Flush();
            Assert.Equal(2, Descendants(settings).OfType<Chippi.UI.Controls.SegmentedControl>().First().Items.Count);
            var popup = settings.ConfirmDelete();
            Assert.False(popup.IsOpen); // never a popup window on the desktop in tests
            Assert.Equal("Delete \"Note 2\"?", Descendants(popup.Child).OfType<System.Windows.Controls.TextBlock>().First().Text);
        }
        finally { module.StopAsync(default).GetAwaiter().GetResult(); }

        static System.Windows.Controls.TextBox Editor(DependencyObject page) =>
            Descendants(page).OfType<System.Windows.Controls.TextBox>().Single(t => System.Windows.Automation.AutomationProperties.GetName(t) == "Note");
        static void Flush()
        {
            for (int i = 0; i < 20; i++)
            {
                System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background);
                Thread.Sleep(10);
            }
        }
    });

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach (var next in Descendants(child)) yield return next;
    }
    [Fact]
    public void Module_loads_once_for_its_tile_and_supplies_reusable_header_and_focus_factories() => WpfTestThread.Run(() =>
    {
        var ui = new Ui(); int opens = 0;
        var module = new ScratchpadModule(_ => { opens++; return new Store(); });
        var context = new ModuleContext("scratchpad", new Settings(), NullLogger.Instance, new IslandActivityHub().CreatePublisher("scratchpad"), ui);
        module.StartAsync(context, default).GetAwaiter().GetResult();
        Assert.Equal(1, opens);
        Assert.Equal("1 note", ui.Tile.Status);
        Assert.Equal(80, ui.Spec.Order);
        var page = Assert.IsType<ScratchpadPage>(ui.Spec.CreateSection());
        Assert.Same(page.Header, ui.Spec.CreateHeader!()); Assert.Same(page.InitialFocus, ui.Spec.InitialFocus!());
        Assert.Same(page, ui.Spec.CreateSection());
        module.StopAsync(default).GetAwaiter().GetResult();
        Assert.Equal(1, opens);
        ((IFeatureModule)module).UninstallAsync(default).GetAwaiter().GetResult();
    });
}
