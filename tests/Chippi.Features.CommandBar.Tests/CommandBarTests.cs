// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.IO;
using System.Windows.Input;
using System.Windows.Media;
using Chippi.Core.Island;
using Chippi.Core.Modules;
using Chippi.Features.CommandBar.Search;
using Chippi.Features.CommandBar.Views;
using Chippi.Features.Sound.Mute;
using Chippi.Island.Tests;
using Microsoft.Extensions.Logging.Abstractions;

namespace Chippi.Features.CommandBar.Tests;

public sealed class CommandBarTests
{
    // ----- Math and conversions -----

    [Theory]
    [InlineData("12*7", "84", "12 × 7")]
    [InlineData("12 x 7", "84", "12 × 7")]
    [InlineData("(3+4)/2", "3.5", "(3 + 4) ÷ 2")]
    [InlineData("2^10", "1,024", "2^10")]
    [InlineData("-2^2", "-4", "−2^2")]
    [InlineData("(-2)^2", "4", "(−2)^2")]
    [InlineData("2^-2", "0.25", "2^−2")]
    [InlineData("2^3^2", "512", "2^3^2")]
    [InlineData("200*15%", "30", "200 × 15%")]
    [InlineData("10/3", "3.333", "10 ÷ 3")]
    [InlineData("5 km in mi", "3.107 mi", "5 km")]
    [InlineData("72 f in c", "22.222 °C", "72 °F")]
    [InlineData("5 in to cm", "12.7 cm", "5 in")]
    [InlineData("2 gallons in l", "7.571 l", "2 gal")]
    [InlineData("90 min in hours", "1.5 hr", "90 min")]
    public void A_sum_or_a_conversion_is_answered(string text, string name, string detail)
    {
        var answer = Calculator.Solve(text);
        Assert.NotNull(answer);
        Assert.Equal((name, detail), (answer.Name, answer.Detail));
    }

    [Theory]
    [InlineData("12")]
    [InlineData("-5")]
    [InlineData("1/0")]
    [InlineData("5 km in kg")]
    [InlineData("chrome")]
    [InlineData("12*")]
    [InlineData("keep awake 2 hr")]
    public void Text_that_isnt_a_sum_has_no_answer(string text) => Assert.Null(Calculator.Solve(text));

    [Fact]
    public void Copy_copies_the_number_without_its_grouping() => Assert.Equal("1024", Calculator.Solve("2^10")!.Copy);

    // ----- The lists -----

    [Fact]
    public void Emoji_are_named_and_have_no_skin_tones()
    {
        var all = EmojiList.All;
        Assert.InRange(all.Count, 1500, 2500);
        Assert.Contains(all, e => e.Name == "Party popper" && e.Text == "\U0001F389");
        Assert.DoesNotContain(all, e => e.Text.Any(c => c == '\uD83C') && e.Text.Contains("\U0001F3FB", StringComparison.Ordinal));
    }

    [Fact]
    public void Each_Windows_Settings_page_has_its_own_address() =>
        Assert.Equal(SettingsPages.All.Count, SettingsPages.All.Select(p => p.Uri).Distinct().Count());

    // ----- Finding -----

    [Fact]
    public void Matches_rank_by_names_that_start_with_the_text_then_by_kind_with_the_web_last()
    {
        var f = new Fixture();
        var rows = f.Search.Find("chr", f.Pc.OpenWindows, f.Files("chr", new FoundFile(@"C:\Users\t\Downloads\ChromeSetup.exe", "ChromeSetup.exe", "Downloads", false)));
        // The app, whose name starts with "chr", then the window, found by its app; then the long tail, the file before the emoji.
        Assert.Equal(["Chrome", "Pull requests · Chippi", "ChromeSetup.exe", "Christmas tree"], rows.Take(4).Select(r => r.Name));
        Assert.Equal(["Open", "Switch", "Open", "Copy"], rows.Take(4).Select(r => r.Verb));
        Assert.Equal("Downloads", rows[2].Detail);
        Assert.Equal(("Search the web for \u201cchr\u201d", "Search", "Google"), (rows[^1].Name, rows[^1].Verb, rows[^1].Detail));
    }

    [Fact]
    public void Files_named_like_the_text_never_crowd_out_an_app_or_its_windows()
    {
        // Travis's live check: "chrome" filled every row with chrome… files from a browser tool's caches.
        var f = new Fixture();
        var files = Enumerable.Range(1, 8).Select(i => new FoundFile($@"C:\Tools\chrome-win64-{i}\chrome_{i}.dll", $"chrome_{i}.dll", $"chrome-win64-{i}", false)).ToArray();
        var rows = f.Search.Find("chrome", f.Pc.OpenWindows, f.Files("chrome", files));
        Assert.Equal(["Chrome", "Pull requests · Chippi"], rows.Take(2).Select(r => r.Name));
        Assert.Equal(ResultKind.File, rows[2].Kind);
        Assert.Equal(ResultKind.Web, rows[^1].Kind);
    }

    [Fact]
    public void What_you_pick_for_the_same_letters_ranks_higher()
    {
        var f = new Fixture();
        var window = f.Search.Find("chr", f.Pc.OpenWindows, []).Single(r => r.Kind == ResultKind.Window);
        f.Search.Picked("chr", window);
        Assert.Equal("Pull requests · Chippi", f.Search.Find("chr", f.Pc.OpenWindows, [])[0].Name);
        Assert.Equal("Pull requests · Chippi", f.Search.Find("ch", f.Pc.OpenWindows, [])[0].Name); // its first letters count too
        Assert.Equal("Chrome", f.Search.Find("chrom", f.Pc.OpenWindows, [])[0].Name); // not letters it was picked for
    }

    [Fact]
    public void Initials_and_words_inside_a_name_find_it()
    {
        var f = new Fixture();
        Assert.Equal("Visual Studio Code", f.Search.Find("vsc", f.Pc.OpenWindows, [])[0].Name);
        Assert.Equal("Visual Studio Code", f.Search.Find("code", f.Pc.OpenWindows, [])[0].Name);
        Assert.Contains(f.Search.Find("party", f.Pc.OpenWindows, []), r => r.Kind == ResultKind.Emoji && r.Name == "Party popper");
        Assert.Contains(f.Search.Find("blue", f.Pc.OpenWindows, []), r => r.Kind == ResultKind.Settings && r.Name == "Bluetooth & devices");
    }

    [Fact]
    public void Values_and_answers_come_first()
    {
        var f = new Fixture();
        var rows = f.Search.Find("keep awake 2 hr", f.Pc.OpenWindows, []);
        Assert.Equal(("Keep awake for 2 hr", ResultKind.Value, "Run"), (rows[0].Name, rows[0].Kind, rows[0].Verb));
        Assert.False(rows[0].CanHide);
        rows = f.Search.Find("12*7", f.Pc.OpenWindows, []);
        Assert.Equal(("84", "12 × 7", "Copy"), (rows[0].Name, rows[0].Detail, rows[0].Verb));
        Assert.False(rows[0].CanHide);
    }

    [Fact]
    public void Eight_rows_at_most_with_the_web_row_last()
    {
        var f = new Fixture();
        var rows = f.Search.Find("e", f.Pc.OpenWindows, []);
        Assert.Equal(CommandSearch.Rows, rows.Count);
        Assert.Equal(ResultKind.Web, rows[^1].Kind);
        Assert.False(rows[^1].CanHide);
    }

    [Fact]
    public void Hidden_results_and_kinds_switched_off_never_show()
    {
        var f = new Fixture();
        var chrome = f.Search.Find("chr", f.Pc.OpenWindows, [])[0];
        f.Settings = f.Settings with { Hidden = [new HiddenResult(chrome.Key, chrome.Name, chrome.HiddenAs)] };
        Assert.DoesNotContain(f.Search.Find("chr", f.Pc.OpenWindows, []), r => r.Key == chrome.Key);
        f.Settings = f.Settings with { Off = [ResultSwitch.Windows, ResultSwitch.Web] };
        var rows = f.Search.Find("chr", f.Pc.OpenWindows, []);
        Assert.DoesNotContain(rows, r => r.Kind is ResultKind.Window or ResultKind.Web);
        Assert.Equal("App", chrome.HiddenAs);
    }

    [Fact]
    public void The_empty_field_shows_your_most_used_then_Chippis_pages()
    {
        var f = new Fixture();
        var claude = f.Search.Find("cla", f.Pc.OpenWindows, []).First(r => r.Name == "Claude");
        f.Search.Picked("cla", claude);
        f.Search.Picked("cla", claude);
        var keep = f.Search.Find("keep", f.Pc.OpenWindows, []).First(r => r.Name == "Keep awake");
        f.Search.Picked("keep", keep);
        var rows = f.Search.Find("", f.Pc.OpenWindows, []);
        Assert.Equal(["Claude", "Keep awake", "Clipboard"], rows.Select(r => r.Name));
        Assert.Equal("Open", rows[2].Verb); // a page
    }

    [Fact]
    public void Clipboard_copies_paste_and_rank_after_files()
    {
        var f = new Fixture();
        var rows = f.Search.Find("pull", f.Pc.OpenWindows, []);
        var copy = Assert.Single(rows, r => r.Kind == ResultKind.Clipboard);
        Assert.Equal(("Paste", "Copied today, 4:12 PM"), (copy.Verb, copy.Detail));
        Assert.True(rows.ToList().IndexOf(copy) > rows.ToList().FindIndex(r => r.Kind == ResultKind.Window));
    }

    [Fact]
    public async Task Rows_run_what_they_say()
    {
        var f = new Fixture();
        await f.Search.Find("chr", f.Pc.OpenWindows, [])[0].Run();
        Assert.Equal(@"shell:AppsFolder\Chrome", f.Pc.Opened[^1]);
        await f.Search.Find("display", f.Pc.OpenWindows, []).First(r => r.Kind == ResultKind.Settings).Run();
        Assert.Equal("ms-settings:display", f.Pc.Opened[^1]);
        await f.Search.Find("party popper", f.Pc.OpenWindows, []).First(r => r.Kind == ResultKind.Emoji).Run();
        Assert.Equal("\U0001F389", f.Copied[^1]);
        await f.Search.Find("5 km in mi", f.Pc.OpenWindows, [])[0].Run();
        Assert.Equal("3.107", f.Copied[^1]);
        await f.Search.Find("chippi pr", f.Pc.OpenWindows, [])[^1].Run();
        Assert.Equal("https://www.google.com/search?q=chippi%20pr", f.Pc.Opened[^1]);
        f.Settings = f.Settings with { WebSearch = WebEngine.DuckDuckGo };
        await f.Search.Find("chippi", f.Pc.OpenWindows, [])[^1].Run();
        Assert.Equal("https://duckduckgo.com/?q=chippi", f.Pc.Opened[^1]);
        await f.Search.Find("pull", f.Pc.OpenWindows, []).First(r => r.Kind == ResultKind.Window).Run();
        Assert.Equal(42, f.Pc.Switched);
    }

    [Fact]
    public async Task Uses_are_kept_across_restarts()
    {
        string dir = Path.Combine(Path.GetTempPath(), "chippi-tests", "command-bar-" + Guid.NewGuid().ToString("N"));
        try
        {
            string path = Path.Combine(dir, "uses.json");
            var uses = new Uses(path);
            uses.Picked("Chr", "app:Chrome");
            uses.Picked("chr", "app:Chrome");
            await uses.Saved;
            var again = new Uses(path);
            Assert.Equal(2, again.For("chr", "app:Chrome"));
            Assert.Equal(2, again.For("c", "app:Chrome"));
            Assert.Equal(["app:Chrome"], again.MostUsed());
            again.Forget("app:Chrome");
            Assert.Equal(0, again.For("chr", "app:Chrome"));
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
    }

    // ----- The module -----

    [Fact]
    public async Task Its_page_takes_the_keyboard_and_Ctrl_Alt_Space_summons_it_even_while_recording_none()
    {
        var f = new Fixture();
        await f.StartAsync();
        var tile = Assert.Single(f.Ui.Tiles);
        Assert.Equal((CommandBarModule.TileKey, "Search", 110, true), (tile.Key, tile.Name, tile.Order, tile.TakesKeyboard));
        Assert.Equal("Ctrl+Alt+Space", f.Hotkey.Shortcut?.ToString());
        Assert.True(f.Hotkey.Shortcut!.IsValid);
        f.Hotkey.Pressed!();
        Assert.Equal([CommandBarModule.TileKey], f.Summoned);
        f.Module.CaptureShortcut(true);
        f.Hotkey.Pressed!();
        Assert.Single(f.Summoned);
        f.Module.RemoveShortcut();
        Assert.Null(f.Settings.Shortcut);
        Assert.Null(f.Hotkey.Pressed);
        Assert.False(f.Module.ChangeShortcut(new MicShortcut(ShortcutModifiers.Shift, 0x20))); // Shift alone would eat typing
        Assert.Equal("Shortcut unavailable", f.Module.ShortcutError);
    }

    [Fact]
    public async Task Hiding_and_switches_are_saved()
    {
        var f = new Fixture();
        await f.StartAsync();
        var chrome = f.Module.Search!.Find("chr", f.Pc.OpenWindows, [])[0];
        f.Module.Hide(chrome);
        Assert.Equal([new HiddenResult(chrome.Key, "Chrome", "App")], f.Settings.Hidden);
        f.Module.Hide(f.Module.Search.Find("chr", f.Pc.OpenWindows, [])[^1]); // the web row can't be hidden
        Assert.Single(f.Settings.Hidden);
        f.Module.Unhide(chrome.Key);
        Assert.Empty(f.Settings.Hidden);
        f.Module.SetOn(ResultSwitch.Emoji, false);
        f.Module.SetOn(ResultSwitch.Files, false);
        f.Module.SetOn(ResultSwitch.Emoji, true);
        Assert.Equal([ResultSwitch.Files], f.Settings.Off);
        f.Module.SetWebSearch(WebEngine.Bing);
        Assert.Equal(WebEngine.Bing, f.Settings.WebSearch);
    }

    [Fact]
    public void The_page_moves_the_selection_runs_hides_and_clears_with_Esc() => WpfTestThread.Run(() =>
    {
        var f = new Fixture();
        f.StartAsync().GetAwaiter().GetResult();
        var page = (CommandBarPage)f.Ui.Tiles[0].CreateSection();
        typeof(CommandBarPage).GetMethod("Opened", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(page, null);
        page.Field.Text = "12*"; // only the web row: it's selected
        page.Field.Text = "12*7"; // typing selects the answer that appears, not the web row still showing
        Assert.Equal(ResultKind.Answer, page.Shown[page.Selected].Kind);
        page.Field.Text = "chr";
        Assert.Equal(0, page.Selected);
        Press(page, Key.Down);
        Assert.Equal(1, page.Selected);
        Press(page, Key.Up);
        Press(page, Key.Up); // stays on the first
        Assert.Equal(0, page.Selected);
        Press(page, Key.Enter);
        Assert.Equal(@"shell:AppsFolder\Chrome", f.Pc.Opened[^1]);
        Press(page, Key.Delete, ModifierKeys.Shift);
        Assert.Contains(f.Settings.Hidden, h => h.Name == "Chrome");
        Assert.DoesNotContain(page.Shown, r => r.Name == "Chrome");
        Assert.True(page.Escape()); // the first Esc clears
        Assert.Equal("", page.Field.Text);
        Assert.False(page.Escape()); // the next closes the island
    });

    // Keyboard.Modifiers reads the real keyboard, so the page's modifiers stand in for Shift.
    private static void Press(CommandBarPage page, Key key, ModifierKeys modifiers = ModifierKeys.None)
    {
        page.Modifiers = () => modifiers;
        page.Field.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, new FakeSource(page), 0, key) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
    }

    private sealed class FakeSource(System.Windows.Media.Visual root) : System.Windows.PresentationSource
    {
        public override System.Windows.Media.Visual RootVisual { get => root; set { } }
        public override bool IsDisposed => false;
        protected override CompositionTarget GetCompositionTargetCore() => null!;
    }

    private sealed class Fixture
    {
        public FakePc Pc { get; } = new();
        public List<string> Copied { get; } = [];
        public List<string> Summoned { get; } = [];
        public FakeHotkey Hotkey { get; } = new();
        public FakeUi Ui { get; } = new();
        private readonly FakeSettings _settings = new();
        private readonly Uses _uses = new();

        public CommandBarSettings Settings
        {
            get => _settings.Value;
            set => _settings.Value = value;
        }

        public CommandBarModule Module { get; }

        public CommandSearch Search { get; }

        public Fixture()
        {
            Module = new CommandBarModule(() => Pc, () => Hotkey);
            Search = new CommandSearch(Pc, Commands, Values, () => Settings, _uses, text =>
            {
                Copied.Add(text);
                return Task.CompletedTask;
            });
        }

        public IReadOnlyList<FoundFile> Files(string text, params FoundFile[] files) => files;

        public Task StartAsync() => Module.StartAsync(new ModuleContext(CommandBarModule.Id, _settings, NullLogger.Instance, new IslandActivityHub().CreatePublisher(CommandBarModule.Id), Ui)
        {
            Commands = Commands,
            SearchCommands = Values,
            SummonIslandPage = key =>
            {
                Summoned.Add(key);
                return true;
            },
            DataDirectory = Path.Combine(Path.GetTempPath(), "chippi-tests", "command-bar-unused"),
        }, default);

        private IReadOnlyList<ChippiCommand> Commands() =>
        [
            new("keep-awake/toggle", "Keep awake", "Coffee", () => Task.CompletedTask),
            new("page/clipboard", "Clipboard", "ClipboardList", () => Task.CompletedTask) { Kind = ChippiCommandKind.Page, Detail = "Chippi · page" },
        ];

        private IReadOnlyList<ChippiCommand> Values(string text) => text switch
        {
            "keep awake 2 hr" => [new("keep-awake/value", "Keep awake for 2 hr", "Coffee", () => Task.CompletedTask) { Kind = ChippiCommandKind.Value }],
            "pull" => [new("clipboard/7", "https://github.com/tcurtsinger/Chippi/pull/178", "Clipboard", () => Task.CompletedTask) { Kind = ChippiCommandKind.Paste, Detail = "Copied today, 4:12 PM" }],
            _ => [],
        };
    }

    private sealed class FakePc : IPc
    {
        public List<string> Opened { get; } = [];
        public nint Switched { get; private set; }

        public IReadOnlyList<OpenWindow> OpenWindows { get; } = [new OpenWindow(42, "Pull requests · Chippi", "Chrome")];

        public IReadOnlyList<StartApp> Apps { get; } =
        [
            new("Calculator", "Microsoft.WindowsCalculator_8wekyb3d8bbwe!App"),
            new("Chrome", "Chrome"),
            new("Claude", "Claude"),
            new("Spotify", "Spotify"),
            new("Visual Studio Code", "Microsoft.VisualStudioCode"),
        ];

        public event Action? AppsChanged { add { } remove { } }

        public void RefreshApps() { }

        public IReadOnlyList<OpenWindow> Windows() => OpenWindows;

        public Task<IReadOnlyList<FoundFile>> FindFilesAsync(string text, int count, CancellationToken ct) => Task.FromResult<IReadOnlyList<FoundFile>>([]);

        void IPc.Open(string target) => Opened.Add(target);

        public void Switch(nint window) => Switched = window;

        public Task<ImageSource?> AppIconAsync(StartApp app, int pixels) => Task.FromResult<ImageSource?>(null);

        public void Dispose() { }
    }

    private sealed class FakeSettings : IModuleSettings
    {
        public CommandBarSettings Value = new();
        public T Get<T>() where T : class, new() => (T)(object)Value;
        public void Save<T>(T value) where T : class => Value = (CommandBarSettings)(object)value;
    }

    private sealed class FakeUi : IModuleUi
    {
        public List<IslandTileSpec> Tiles { get; } = [];
        public IIslandTile AddIslandTile(IslandTileSpec spec)
        {
            Tiles.Add(spec);
            return new IslandTileContribution(CommandBarModule.Id, spec);
        }

        public void SetSettingsPage(Func<string?, object> page) { }
    }

    private sealed class FakeHotkey : IMicHotkey
    {
        public Action? Pressed;
        public MicShortcut? Shortcut;
        public bool Register(MicShortcut shortcut, Action pressed)
        {
            Shortcut = shortcut;
            Pressed = pressed;
            return true;
        }

        public void Dispose() => Pressed = null;
    }
}
