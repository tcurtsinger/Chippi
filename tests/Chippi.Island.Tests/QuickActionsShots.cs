// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

#if DEBUG
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Chippi.App.Island;
using Chippi.App.Settings.Pages;
using Chippi.Core.Island;
using Chippi.Core.Modules;
using Chippi.Features.QuickActions;
using Chippi.Features.QuickActions.Tests;
using Chippi.Features.Sound.Mute;
using Microsoft.Extensions.Logging.Abstractions;
using static Chippi.Island.Tests.IslandPanelTests;

namespace Chippi.Island.Tests;

/// <summary>
/// Renders the Actions page and Settings › Quick actions off screen on a fake PC, in the states the Phase 5.5 mockup
/// shows: the picker, all 18 picked, 6 picked and Quit apps. Runs only when CHIPPI_ISLAND_SHOTS names an output folder.
/// </summary>
public sealed class QuickActionsShots
{
    private static readonly MonitorArea Area = new(default, new Rect(-20000, 0, 1920, 1080), new Rect(-20000, 0, 1920, 1032), 1);

    [Fact]
    public void Render()
    {
        if (Environment.GetEnvironmentVariable("CHIPPI_ISLAND_SHOTS") is not { Length: > 0 } folder)
            return;
        Directory.CreateDirectory(folder);
        WpfTestThread.Run(() =>
        {
            var styles = new ResourceDictionary { Source = new Uri("pack://application:,,,/Chippi;component/Settings/SettingsStyles.xaml") };
            Application.Current.Resources.MergedDictionaries.Add(styles);
            var pc = new FakePc { State = MockState() };
            var settings = new Settings();
            var ui = new Pages();
            var module = new QuickActionsModule(() => pc, () => new NoHotkey());
            module.StartAsync(new ModuleContext(QuickActionsModule.Id, settings, NullLogger.Instance, new IslandActivityHub().CreatePublisher(QuickActionsModule.Id), ui), default).GetAwaiter().GetResult();
            try
            {
                var tiles = IslandTileContribution.Group([ui.Tile!]);
                Shoot(tiles, Path.Combine(folder, "actions-picker.png"));
                module.Pick(QuickAction.All.Select(a => a.Id));
                Shoot(tiles, Path.Combine(folder, "actions-all.png"));
                Shoot(tiles, Path.Combine(folder, "actions-quit.png"), panel => Click(panel, "quit"));
                module.Pick([QuickActionId.Dark, QuickActionId.Hidden, QuickActionId.Icons, QuickActionId.Bluetooth, QuickActionId.Lock, QuickActionId.Recycle]);
                Shoot(tiles, Path.Combine(folder, "actions-few.png"));
                module.Pick([QuickActionId.Dark, QuickActionId.Hidden, QuickActionId.Icons, QuickActionId.Extend, QuickActionId.Bluetooth, QuickActionId.WiFi,
                    QuickActionId.Plan, QuickActionId.Lock, QuickActionId.Displays, QuickActionId.Recycle, QuickActionId.Eject, QuickActionId.Quit,
                    QuickActionId.Explorer, QuickActionId.Power]);
                ShootSettings((FrameworkElement)ui.Page!(null), Path.Combine(folder, "settings-quick-actions.png"));
            }
            finally { module.StopAsync(default).GetAwaiter().GetResult(); }
            Time(Path.Combine(folder, "actions-timings.txt"));
        });
    }

    /// <summary>How long the page takes to build and lay out with all 18 picked: the first open (cold), later ones, and Quit apps stepping in.</summary>
    private static void Time(string path)
    {
        var ui = new Pages();
        var module = new QuickActionsModule(() => new FakePc { State = MockState() }, () => new NoHotkey());
        module.StartAsync(new ModuleContext(QuickActionsModule.Id, new Settings(), NullLogger.Instance, new IslandActivityHub().CreatePublisher(QuickActionsModule.Id), ui), default).GetAwaiter().GetResult();
        module.Pick(QuickAction.All.Select(a => a.Id));
        var panel = new IslandPanelWindow();
        var lines = new List<string>();
        try
        {
            panel.Initialize();
            panel.SetMedia(new FakeMedia());
            panel.SetTiles(IslandTileContribution.Group([ui.Tile!]));
            panel.SetLastPage(null);
            panel.Open(IslandGeometry.Start(Area.Work, 1), Area);
            Pump(TimeSpan.FromMilliseconds(400));
            for (int i = 1; i <= 3; i++)
            {
                var watch = System.Diagnostics.Stopwatch.StartNew();
                panel.ShowPage(QuickActionsModule.TileKey);
                panel.UpdateLayout();
                lines.Add($"Actions page, open {i}: {watch.Elapsed.TotalMilliseconds:0.0} ms");
                Pump(TimeSpan.FromMilliseconds(300));
                if (i < 3)
                {
                    panel.ShowPage(null, remember: false);
                    Pump(TimeSpan.FromMilliseconds(300));
                }
            }
            var list = System.Diagnostics.Stopwatch.StartNew();
            Click(panel, "quit");
            panel.UpdateLayout();
            lines.Add($"Quit apps stepping in (9 apps): {list.Elapsed.TotalMilliseconds:0.0} ms");
            File.WriteAllLines(path, lines);
        }
        finally
        {
            panel.ShowPage(null, remember: false);
            panel.Close();
            module.StopAsync(default).GetAwaiter().GetResult();
        }
    }

    /// <summary>The mockup's PC: dark mode on, two drives, nine apps with Chrome in front and Spotify kept.</summary>
    private static QuickState MockState() => new(
        new Dictionary<QuickActionId, bool>
        {
            [QuickActionId.Dark] = true, [QuickActionId.Hidden] = false, [QuickActionId.Icons] = true, [QuickActionId.AutoHide] = false,
            [QuickActionId.Extend] = true, [QuickActionId.Bluetooth] = true, [QuickActionId.WiFi] = true, [QuickActionId.Camera] = true,
        },
        new HashSet<QuickActionId>(),
        [new(Guid.NewGuid(), "Balanced", true), new(Guid.NewGuid(), "High performance", false), new(Guid.NewGuid(), "Ultimate Performance", false), new(Guid.NewGuid(), "Power saver", false)],
        [new("e", "Samsung T7 (E:)", 1_000_204_886_016), new("f", "SanDisk (F:)", 62_008_590_336)],
        [
            new(@"C:\Apps\chrome.exe", "Chrome", true), new(@"C:\Apps\steam.exe", "Steam", false), new(@"C:\Apps\Discord.exe", "Discord", false),
            new(@"C:\Apps\outlook.exe", "Outlook", false), new(@"C:\Apps\Zoom.exe", "Zoom Workplace", false), new(@"C:\Apps\claude.exe", "Claude", false),
            new(@"C:\Apps\Spotify.exe", "Spotify", false), new(@"C:\Apps\WindowsTerminal.exe", "Windows Terminal", false), new(@"C:\Apps\game.exe", "007 First Light", false),
        ],
        0,
        12);

    private static void Click(IslandPanelWindow panel, string key)
    {
        var button = Find<ToggleButton>(panel).First(b => Equals(b.Tag, key));
        // As a click or UI Automation's Toggle does: the tiles act on Checked and Unchecked.
        ((System.Windows.Automation.Provider.IToggleProvider)System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement(button)).Toggle();
    }

    private static void Shoot(IReadOnlyList<IslandTileGroup> tiles, string path, Action<IslandPanelWindow>? arrange = null)
    {
        var panel = new IslandPanelWindow();
        try
        {
            panel.Initialize();
            panel.SetMedia(new FakeMedia());
            panel.SetTiles(tiles);
            panel.SetLastPage(null);
            panel.Open(IslandGeometry.Start(Area.Work, 1), Area);
            Pump(TimeSpan.FromMilliseconds(400));
            panel.ShowPage(QuickActionsModule.TileKey);
            Pump(TimeSpan.FromMilliseconds(400));
            if (arrange is not null)
            {
                arrange(panel);
                Pump(TimeSpan.FromMilliseconds(300));
            }
            IslandPanelShots.Save(panel, path);
        }
        finally
        {
            panel.ShowPage(null, remember: false); // lets go of the page, which its module keeps for the next shot
            panel.Close();
        }
    }

    private static void ShootSettings(FrameworkElement view, string path)
    {
        view.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
        Pump(TimeSpan.FromMilliseconds(200));
        var page = new Border { Width = 824, Padding = new Thickness(32, 24, 32, 32), Child = new ModulePageHost("Quick actions", view) };
        page.SetResourceReference(Border.BackgroundProperty, "WindowBackgroundBrush");
        page.Measure(new Size(824, double.PositiveInfinity));
        page.Arrange(new Rect(page.DesiredSize));
        page.UpdateLayout();
        var bitmap = new RenderTargetBitmap(1236, (int)Math.Ceiling(page.ActualHeight * 1.5), 144, 144, PixelFormats.Pbgra32);
        bitmap.Render(page);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(path);
        encoder.Save(file);
    }

    private static IEnumerable<T> Find<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T value) yield return value;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in Find<T>(VisualTreeHelper.GetChild(root, i)))
                yield return child;
    }

    private sealed class Settings : IModuleSettings
    {
        private QuickActionsSettings _value = new() { Kept = [@"C:\Apps\Spotify.exe", @"C:\Apps\Discord.exe"] };
        public T Get<T>() where T : class, new() => (T)(object)_value;
        public void Save<T>(T value) where T : class => _value = (QuickActionsSettings)(object)value;
    }

    private sealed class Pages : IModuleUi
    {
        public IslandTileContribution? Tile;
        public Func<string?, object>? Page;
        public IIslandTile AddIslandTile(IslandTileSpec spec) => Tile = new IslandTileContribution(QuickActionsModule.Id, spec);
        public void SetSettingsPage(Func<string?, object> factory) => Page = factory;
    }

    private sealed class NoHotkey : IMicHotkey
    {
        public bool Register(MicShortcut shortcut, Action pressed) => true;
        public void Dispose() { }
    }
}
#endif
