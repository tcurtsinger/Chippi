// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

#if DEBUG
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Chippi.App.Island;
using Chippi.Core.Island;
using Chippi.Core.Modules;
using Chippi.Features.CommandBar;
using Chippi.Features.CommandBar.Search;
using Chippi.Features.Sound.Mute;
using Chippi.UI.Controls;
using Microsoft.Extensions.Logging.Abstractions;
using static Chippi.Island.Tests.IslandPanelTests;

namespace Chippi.Island.Tests;

/// <summary>
/// Renders the real Command bar page off screen, with the mockup's invented apps, windows, files and copies, in the states
/// its artboards show: your most used, "chr", "keep awake 2 hr", "volume 40", "5 km in mi", "pull" and "party". Runs only
/// when CHIPPI_ISLAND_SHOTS names an output folder; the page's keyboard never reaches the real foreground.
/// </summary>
public sealed class CommandBarShots
{
    private static readonly MonitorArea Area = new(default, new Rect(-20000, 0, 1920, 1080), new Rect(-20000, 0, 1920, 1032), 1);

    [Fact]
    public void Render()
    {
        if (Environment.GetEnvironmentVariable("CHIPPI_ISLAND_SHOTS") is not { Length: > 0 } folder)
            return;
        Directory.CreateDirectory(folder);
        string data = Path.Combine(Path.GetTempPath(), "chippi-tests", "command-bar-shots-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(data);
        // Your most used, as the mockup has them.
        File.WriteAllText(Path.Combine(data, "uses.json"), """
            {"Total":{"chippi:keep-awake/toggle":9,"app:Chrome":8,"app:Claude":7,"app:Spotify":6,"chippi:page/sound":5,"app:Discord":4,"window:Windows Terminal|pwsh in X:\\code\\chippi":3,"chippi:quick-actions/dark":2},"Typed":{}}
            """);
        WpfTestThread.Run(() =>
        {
            var ui = new Ui();
            var module = new CommandBarModule(() => new Pc(), () => new Hotkey());
            try
            {
                module.StartAsync(new ModuleContext(CommandBarModule.Id, new Settings(), NullLogger.Instance, new IslandActivityHub().CreatePublisher(CommandBarModule.Id), ui)
                {
                    Commands = Commands,
                    SearchCommands = Values,
                    DataDirectory = data,
                }, default).GetAwaiter().GetResult();
                var tiles = IslandTileContribution.Group([new IslandTileContribution(CommandBarModule.Id, ui.Tile!)]);
                var field = (SearchBox)ui.Tile!.CreateHeader!();
                foreach (var (name, text) in new[] { ("empty", ""), ("chr", "chr"), ("value", "keep awake 2 hr"), ("volume", "volume 40"), ("math", "5 km in mi"), ("paste", "pull"), ("emoji", "party") })
                    Shoot(tiles, Path.Combine(folder, $"command-bar-{name}.png"), () => field.Text = text);
            }
            finally
            {
                module.StopAsync(default).GetAwaiter().GetResult();
                Directory.Delete(data, recursive: true);
            }
        });
    }

    private static void Shoot(IReadOnlyList<IslandTileGroup> tiles, string path, Action type)
    {
        var panel = new IslandPanelWindow { ActivateWindow = _ => { } };
        try
        {
            panel.Initialize();
            panel.SetMedia(new FakeMedia());
            panel.SetTiles(tiles);
            panel.SetLastPage(null);
            panel.Open(IslandGeometry.Start(Area.Work, 1), Area);
            Pump(TimeSpan.FromMilliseconds(400));
            panel.ShowPage(CommandBarModule.TileKey);
            Pump(TimeSpan.FromMilliseconds(400));
            type();
            Pump(TimeSpan.FromMilliseconds(300));
            IslandPanelShots.Save(panel, path);
        }
        finally
        {
            panel.ShowPage(null, remember: false);
            panel.Close();
        }
    }

    private static IReadOnlyList<ChippiCommand> Commands() =>
    [
        new("keep-awake/toggle", "Keep awake", "Coffee", Done) { Words = "keep awake coffee" },
        new("quick-actions/dark", "Dark mode", "Moon", Done) { Words = "Dark" },
        new("quick-actions/lock", "Lock", "Lock", Done),
        new("mic-mute/toggle", "Mute microphone", "MicOff", Done) { Words = "mic microphone mute" },
        new("page/sound", "Volume", "Sliders", Done) { Kind = ChippiCommandKind.Page, Detail = "Chippi · page", Words = "sound audio" },
        new("page/clipboard", "Clipboard", "ClipboardList", Done) { Kind = ChippiCommandKind.Page, Detail = "Chippi · page" },
        new("page/system-monitor", "System", "Activity", Done) { Kind = ChippiCommandKind.Page, Detail = "Chippi · page" },
    ];

    private static IReadOnlyList<ChippiCommand> Values(string text) => text switch
    {
        "keep awake 2 hr" => [new("keep-awake/value", "Keep awake for 2 hr", "Coffee", Done) { Kind = ChippiCommandKind.Value }],
        "volume 40" => [new("audio-routing/volume", "Volume to 40%", "Volume", Done) { Kind = ChippiCommandKind.Value, Level = 0.4 }],
        "pull" =>
        [
            new("clipboard/1", "https://github.com/tcurtsinger/Chippi/pull/178", "Clipboard", Done) { Kind = ChippiCommandKind.Paste, Detail = "Copied today, 4:12 PM" },
            new("clipboard/2", "Pull the release notes from the wiki", "Clipboard", Done) { Kind = ChippiCommandKind.Paste, Detail = "Copied yesterday" },
        ],
        _ => [],
    };

    private static Task Done() => Task.CompletedTask;

    private sealed class Pc : IPc
    {
        public IReadOnlyList<StartApp> Apps { get; } = [.. new[]
        {
            "Calculator", "Chrome", "Claude", "Discord", "File Explorer", "Microsoft Edge", "Notepad", "Outlook", "Paint", "Snipping Tool",
            "Spotify", "Steam", "Task Manager", "Visual Studio Code", "Windows Terminal", "Zoom Workplace",
        }.Select(name => new StartApp(name, name))];

        public event Action? AppsChanged { add { } remove { } }

        public void RefreshApps() { }

        public IReadOnlyList<OpenWindow> Windows() =>
        [
            new(1, "Knightfall Walkthrough - IGN", "Chrome"),
            new(2, "Pull requests · tcurtsinger/Chippi", "Chrome"),
            new(3, "Calendar - Travis Curtsinger", "Outlook"),
            new(4, @"pwsh in X:\code\chippi", "Windows Terminal"),
            new(5, "Downloads", "File Explorer"),
        ];

        public Task<IReadOnlyList<FoundFile>> FindFilesAsync(string text, int count, CancellationToken ct) => Task.FromResult<IReadOnlyList<FoundFile>>(
            text == "chr" ? [new FoundFile(@"C:\Users\Travis\Downloads\ChromeSetup.exe", "ChromeSetup.exe", "Downloads", false)] : []);

        public void Open(string target) { }

        public void Switch(nint window) { }

        public Task<ImageSource?> AppIconAsync(StartApp app, int pixels) => Task.FromResult<ImageSource?>(null);

        public void Dispose() { }
    }

    private sealed class Settings : IModuleSettings
    {
        private CommandBarSettings _value = new();
        public T Get<T>() where T : class, new() => (T)(object)_value;
        public void Save<T>(T value) where T : class => _value = (CommandBarSettings)(object)value;
    }

    private sealed class Ui : IModuleUi
    {
        public IslandTileSpec? Tile;
        public IIslandTile AddIslandTile(IslandTileSpec spec)
        {
            Tile = spec;
            return new IslandTileContribution(CommandBarModule.Id, spec);
        }

        public void SetSettingsPage(Func<string?, object> page) { }
    }

    private sealed class Hotkey : IMicHotkey
    {
        public bool Register(MicShortcut shortcut, Action pressed) => true;
        public void Dispose() { }
    }
}
#endif
