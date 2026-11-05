// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.IO;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Chippi.Core.Island;
using Chippi.Core.Modules;
using Chippi.Features.WindowLayouts.Views;
using Chippi.Island.Tests;
using Microsoft.Extensions.Logging.Abstractions;

namespace Chippi.Features.WindowLayouts.Tests;

/// <summary>Settings › Snap and the Shift+drag zones against the approved boards (wave-3.html: snap, sheet-snap-editor, sheet-snap-zones).</summary>
public sealed class SnapPageTests
{
    private sealed class Store : IModuleSettings
    {
        public WindowLayoutsSettings Value { get; set; } = new();

        public bool Fails { get; set; }

        public T Get<T>() where T : class, new() => (T)(object)Value;

        public void Save<T>(T settings) where T : class =>
            Value = Fails ? throw new IOException("Settings locked") : (WindowLayoutsSettings)(object)settings;
    }

    private sealed class Ui : IModuleUi
    {
        public Func<string?, object>? Page { get; private set; }

        public IIslandTile AddIslandTile(IslandTileSpec spec) => throw new InvalidOperationException("Snap has no tile of its own");

        public void SetSettingsPage(Func<string?, object> page) => Page = page;

        public void AddCommands(Func<IReadOnlyList<ChippiCommand>> commands) { }
    }

    internal static readonly SnapDisplay Dell = new(@"\\?\DISPLAY#DEL4321#1", "DELL U2723QE", 1, true, new(0, 0, 3840, 2160), new(0, 0, 3840, 2112));
    internal static readonly SnapDisplay Laptop = new(@"\\?\DISPLAY#BOE0900#2", "Built-in display", 2, false, new(3840, 0, 2560, 1600), new(3840, 0, 2560, 1552));

    private static ZoneNode Leaf(string id, string name) => new() { Id = id, Name = name };

    internal static SnapLayout Coding() => new("coding", "Coding",
        ZoneNode.Split(Leaf("main", "Main"), ZoneNode.Split(Leaf("docs", "Docs"), Leaf("terminal", "Terminal"), SplitDirection.Stacked) with { Id = "right" }, SplitDirection.SideBySide, .6) with { Id = "root" });

    private static SnapLayout Reading() => new("reading", "Reading",
        ZoneNode.Split(Leaf("r1", "Notes"), ZoneNode.Split(Leaf("r2", "Reading"), Leaf("r3", "Links"), SplitDirection.SideBySide, .75), SplitDirection.SideBySide, .2));

    private sealed class Fixture
    {
        public readonly Store Store = new();
        public readonly Ui Ui = new();
        public readonly WindowLayoutsModule Module = new(() => [Dell, Laptop]);

        public SnapPage Start(WindowLayoutsSettings settings)
        {
            Store.Value = settings;
            Module.StartAsync(new ModuleContext(WindowLayoutsModule.Id, Store, NullLogger.Instance, new IslandActivityHub().CreatePublisher(WindowLayoutsModule.Id), Ui), default).GetAwaiter().GetResult();
            return (SnapPage)Ui.Page!(null);
        }
    }

    /// <summary>The board's settings: Shift+drag on, Coding on the DELL, seven layouts, an excluded app, an 8 px gap.</summary>
    private static WindowLayoutsSettings Board() => new()
    {
        ShiftDragEnabled = true,
        Layouts = [.. SnapLayouts.Defaults(), Coding(), Reading()],
        DisplayLayouts = new(StringComparer.OrdinalIgnoreCase) { [Dell.Id] = "coding" },
        ExcludedApps = [Path.Combine(Environment.SystemDirectory, "notepad.exe")],
    };

    private static void Theme()
    {
        if (Application.Current.Resources.MergedDictionaries.Any(d => d.Source?.OriginalString.EndsWith("SettingsStyles.xaml", StringComparison.Ordinal) == true))
            return;
        Application.Current.Resources.MergedDictionaries.Add(new ResourceDictionary
        { Source = new Uri("pack://application:,,,/Chippi;component/Settings/SettingsStyles.xaml") });
        bool light = WpfTestThread.ShotsTheme == "Light";
        Application.Current.Resources["AccentBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(light ? "#6B4FBB" : "#B79CFF"));
        Application.Current.Resources["OnAccentBrush"] = light ? Brushes.White : Brushes.Black;
    }

    [Fact]
    public void ThePageListsDisplaysLayoutsAndGaps()
    {
        WpfTestThread.Run(() =>
        {
            Theme();
            UI.Controls.AppIcon.LoadAsync(Path.Combine(Environment.SystemDirectory, "notepad.exe"), 80).GetAwaiter().GetResult();
            var f = new Fixture();
            var page = f.Start(Board());
            Layout(page, 768);
            var texts = Texts(page);
            Assert.Contains("DELL U2723QE", texts);
            Assert.Contains("Main display · 3840 × 2160", texts);
            Assert.Contains("2560 × 1600", texts);
            Assert.Contains("7 layouts", texts);
            Assert.Contains("3 zones", texts);
            Assert.Contains("3 zones · not in the Snap menu", texts);
            Assert.Equal(5, texts.Count(t => t == "Built in"));
            Assert.Contains("8 px", texts);
            Assert.Equal("Coding", Find<ComboBox>(page).First(c => System.Windows.Automation.AutomationProperties.GetName(c) == "Layout on DELL U2723QE").SelectedItem);
            Shot(page, "snap", 768);
            Shot(page, "snap-narrow", 640);

            // The switch, through UI Automation as Narrator would.
            var toggle = Find<ToggleButton>(page).First();
            toggle.ApplyTemplate();
            ((IToggleProvider)UIElementAutomationPeer.CreatePeerForElement(toggle)).Toggle();
            Assert.False(f.Store.Value.ShiftDragEnabled);
            page = (SnapPage)f.Ui.Page!(null);
            Layout(page, 768);
            Assert.Contains("Off", Find<ToggleButton>(page).Select(t => t.Content as string));
            Shot(page, "snap-off", 768);

            // A display's layout, and a gap.
            Find<ComboBox>(page).First(c => System.Windows.Automation.AutomationProperties.GetName(c) == "Layout on Built-in display").SelectedIndex = 1;
            Assert.Equal("thirds", f.Store.Value.DisplayLayouts[Laptop.Id]);
            Find<Slider>(page).Single(s => System.Windows.Automation.AutomationProperties.GetName(s) == "Window gap").Value = 12;
            Assert.Equal(12, f.Store.Value.WindowGap);
            f.Module.StopAsync(default).GetAwaiter().GetResult();
        });
    }

    [Fact]
    public void TheEditorSplitsNamesResizesAndSavesOnlyOnSave()
    {
        WpfTestThread.Run(() =>
        {
            Theme();
            var f = new Fixture();
            var page = f.Start(Board());
            page.StartEdit(SnapPage.From(Coding()));
            Layout(page, 768);
            Assert.Contains("Edit layout", Texts(page));
            Assert.Contains("2304 × 2112 on DELL U2723QE".Replace("2304 × 2112", Size(Dell, .6, 1)), Texts(page));
            Shot(page, "editor-edit", 768, around: "Edit layout");

            var canvas = Find<ZoneCanvas>(page).Single();
            canvas.ShowTipFor("root");
            Layout(page, 768);
            Assert.Contains("60% · 40%", Texts(canvas));
            Shot(page, "editor-drag", 768, around: "Edit layout");

            // A divider moves by whole percents from the keyboard; nothing is saved until Save.
            canvas.Nudge("root", 5);
            Assert.Equal(.6, f.Store.Value.Layouts.Single(l => l.Id == "coding").Root.Ratio);
            // Split the selected zone (Main) side by side: the new zone is named and selected.
            Find<Button>(page).Single(b => System.Windows.Automation.AutomationProperties.GetName(b) == "Side by side").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Assert.Equal("Zone 4", Field(page, "Zone name").Text);
            Field(page, "Zone name").Text = "Chat";
            Find<Button>(page).Single(b => Equals(b.Content, "Save")).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            var saved = f.Store.Value.Layouts.Single(l => l.Id == "coding").Root;
            Assert.Equal(.65, saved.Ratio, 3);
            Assert.Equal(["Main", "Chat", "Docs", "Terminal"], saved.Flatten().Zones.Select(z => z.Name));
            Assert.DoesNotContain("Edit layout", Texts(page));
            f.Module.StopAsync(default).GetAwaiter().GetResult();
        });
    }

    [Fact]
    public void ANewLayoutNeedsANameNoOtherLayoutHas()
    {
        WpfTestThread.Run(() =>
        {
            Theme();
            var f = new Fixture();
            var page = f.Start(Board());
            Find<Button>(page).Single(b => System.Windows.Automation.AutomationProperties.GetName(b) == "Add layout").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Layout(page, 768);
            var save = Find<Button>(page).Single(b => Equals(b.Content, "Save"));
            Assert.Contains("New layout", Texts(page));
            Assert.Equal("Zone 1", Field(page, "Zone name").Text);
            Assert.False(save.IsEnabled);
            Assert.False(Find<Button>(page).Single(b => Equals(b.Content, "Remove") && b.Parent is StackPanel).IsEnabled);
            Shot(page, "editor-new", 768, around: "New layout");
            Field(page, "Name").Text = "coding ";
            Assert.Contains(SnapPage.NameTakenMessage, Texts(page));
            Assert.False(save.IsEnabled);
            Shot(page, "editor-refused", 768, around: "New layout");
            Field(page, "Name").Text = "Writing";
            Assert.True(save.IsEnabled);

            // A save that fails keeps the draft, with the reason by Save.
            f.Store.Fails = true;
            save.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Assert.Contains(SnapPage.SavingFailed, Texts(page));
            Assert.Equal("Writing", Field(page, "Name").Text);
            f.Store.Fails = false;
            save.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Assert.Equal("Writing", f.Store.Value.Layouts[^1].Name);
            Assert.Contains("8 layouts", Texts(page));
            f.Module.StopAsync(default).GetAwaiter().GetResult();
        });
    }

    [Fact]
    public void TheZonesOverTheDisplayAndTheDeleteQuestion()
    {
        WpfTestThread.Run(() =>
        {
            Theme();
            Assert.Equal("Zone 3", SnapPage.NextName(Coding().Root.Flatten().Zones));
            Assert.Equal("Built in · not in the Snap menu", SnapPage.Caption(SnapLayouts.Defaults()[0], 6));

            using var overlay = new ZoneOverlay();
            var zones = Coding().Root.Flatten().Zones;
            overlay.Build(Dell, zones);
            overlay.Point(0);
            var content = overlay.Content;
            content.Width = 960;
            content.Height = 540;
            Layout(content, 960);
            Shot(content, "zones-main", 960);
            overlay.Point(-1);
            overlay.Build(Laptop, SnapLayouts.Defaults()[0].Root.Flatten().Zones);
            overlay.Point(1);
            content.Height = 600;
            Layout(content, 960);
            Shot(content, "zones-laptop", 960);

            bool deleted = false;
            var question = SnapPage.ConfirmPanel(() => { }, () => deleted = true, out _);
            Layout(question, double.PositiveInfinity);
            Assert.Contains("Delete this layout?", Texts(question));
            Find<Button>(question).Single(b => Equals(b.Content, "Delete")).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Assert.True(deleted);
        });
    }

    [Fact]
    public void TheIslandMenuPlacesTheWindowThatWasInFrontOrSaysWhyNot()
    {
        WpfTestThread.Run(() =>
        {
            Theme();
            var f = new Fixture();
            f.Start(Board());
            var photos = new Chippi.Core.Actions.ActionWindow(10, 20, 30, 0, "ApplicationFrameWindow", @"C:\Fixtures\Photos.exe", "Photos", "IMG_0042.jpg", false);
            var menu = new SnapMenu(f.Module, photos);
            Layout(menu, 496);
            Assert.Contains("Photos", Texts(menu));
            Assert.Equal(6, Find<Border>(menu).Count(b => b.Child is StackPanel { Children.Count: 2 } p && p.Children[0] is Panel));
            Assert.True(Find<Button>(menu).Single(b => System.Windows.Automation.AutomationProperties.GetName(b) == "Next monitor").IsEnabled);
            Assert.False(Find<Button>(menu).Single(b => System.Windows.Automation.AutomationProperties.GetName(b) == "Restore").IsEnabled);
            IslandShot(menu, "menu");

            // Pressing a zone runs it; with no PC to move windows here it fails, and the line says which.
            Find<SnapMenu.MenuZone>(menu).First(z => System.Windows.Automation.AutomationProperties.GetName(z) == "Halves: Left half").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Assert.Equal("Left half failed", menu.Failure);
            IslandShot(menu, "menu-failed");

            var admin = new SnapMenu(f.Module, photos with { App = "Task Manager", Elevated = true });
            Layout(admin, 496);
            Assert.Contains("Task Manager runs as admin, so it can't be moved", Texts(admin));
            Assert.All(Find<Button>(admin), b => Assert.False(b.IsEnabled));
            IslandShot(admin, "menu-admin");

            var none = new SnapMenu(f.Module, null);
            Layout(none, 496);
            Assert.Contains("No window to move", Texts(none));
            f.Module.StopAsync(default).GetAwaiter().GetResult();
        });
    }

    /// <summary>The menu as the island shows it: on the island's own fill, at its page width.</summary>
    private static void IslandShot(FrameworkElement view, string name)
    {
        // The island's page body: 496 by 304 inside its 12 inset.
        var host = new Border { Padding = new Thickness(12), Child = view, Width = 520 };
        host.SetResourceReference(Border.BackgroundProperty, "IslandBrush");
        host.SetResourceReference(TextElement.ForegroundProperty, "TextPrimaryBrush");
        Shot(host, name, 520);
        Assert.InRange(view.ActualHeight, 0, 304);
        host.Child = null;
    }

    private static string Size(SnapDisplay display, double width, double height)
    {
        var r = SnapGeometry.PlaceWithGaps(display.WorkArea, new SnapZone("z", "z", 0, 0, width, height), 8, 0);
        return $"{r.Width} × {r.Height}";
    }

    private static TextBox Field(DependencyObject root, string name) =>
        Find<TextBox>(root).First(t => System.Windows.Automation.AutomationProperties.GetName(t) == name);

    private static List<string> Texts(DependencyObject root) => [.. Find<TextBlock>(root).Select(t => t.Text)];

    private static IEnumerable<T> Find<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is UIElement { Visibility: not Visibility.Visible }) yield break;
        if (root is T item) yield return item;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach (var found in Find<T>(child)) yield return found;
    }

    private static void Layout(FrameworkElement view, double width)
    {
        view.Measure(new Size(width, double.PositiveInfinity));
        double w = double.IsInfinity(width) ? view.DesiredSize.Width : width;
        view.Arrange(new Rect(0, 0, w, view.DesiredSize.Height));
        view.UpdateLayout();
    }

    /// <summary>The view at 2x on the window's background when CHIPPI_ISLAND_SHOTS names a folder; around a heading, just its card's part.</summary>
    private static void Shot(FrameworkElement view, string name, double width, string? around = null)
    {
        if (Environment.GetEnvironmentVariable("CHIPPI_ISLAND_SHOTS") is not { Length: > 0 } folder) return;
        Directory.CreateDirectory(folder);
        Layout(view, width);
        double top = 0, h = view.ActualHeight, w = view.ActualWidth;
        if (around is not null && Find<TextBlock>(view).FirstOrDefault(t => t.Text == around) is { } heading)
        {
            top = Math.Max(0, heading.TranslatePoint(new Point(), view).Y - 40);
            h = Math.Min(h - top, 720);
        }
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle((Brush)Application.Current.Resources["WindowBackgroundBrush"], null, new Rect(0, 0, w + 32, h + 32));
            dc.DrawRectangle(new VisualBrush(view) { Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top, ViewboxUnits = BrushMappingMode.Absolute, Viewbox = new Rect(0, top, w, h) }, null, new Rect(16, 16, w, h));
        }
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling((w + 32) * 2), (int)Math.Ceiling((h + 32) * 2), 192, 192, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(Path.Combine(folder, $"snap-{name}-{WpfTestThread.ShotsTheme.ToLowerInvariant()}.png"));
        png.Save(file);
    }
}
