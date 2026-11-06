// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Chippi.Core.Actions;
using Chippi.Core.Island;
using Chippi.Core.Modules;
using Chippi.Core.Shortcuts;
using Chippi.Features.WindowLayouts.Views;
using Chippi.Island.Tests;
using Microsoft.Extensions.Logging.Abstractions;
using static Chippi.Features.WindowLayouts.Tests.SnapPageTests;

namespace Chippi.Features.WindowLayouts.Tests;

/// <summary>Settings › Snap › Arrangements against the approved boards (arrangements.html: arrangements, arrangements-review, sheet-arrangements-states).</summary>
public sealed class ArrangementPageTests
{
    private static readonly string Sys = Environment.SystemDirectory, Win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
    private static readonly string Notepad = Path.Combine(Sys, "notepad.exe"), Explorer = Path.Combine(Win, "explorer.exe"),
        Cmd = Path.Combine(Sys, "cmd.exe"), CharMap = Path.Combine(Sys, "charmap.exe"), Mstsc = Path.Combine(Sys, "mstsc.exe"),
        Edge = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), @"Microsoft\Edge\Application\msedge.exe");
    private const string Terminal = "Microsoft.WindowsTerminal_8wekyb3d8bbwe!App", Paint = "Microsoft.Paint_8wekyb3d8bbwe!App",
        Calculator = "Microsoft.WindowsCalculator_8wekyb3d8bbwe!App", Snipping = "Microsoft.ScreenSketch_8wekyb3d8bbwe!App";

    private static ArrangementRow Row(string app, string title, string target, SnapDisplay display, int x, int y, int w, int h,
        bool max = false, bool min = false, string args = "", string folder = "") => new()
    {
        Id = $"{app}-{title}".Replace(' ', '-'),
        AppIdentity = target.Length > 0 ? target : app,
        Target = target,
        Arguments = args,
        WorkingFolder = folder,
        App = app,
        ClassName = app + "Window",
        TitleHint = title,
        DisplayId = display.Id,
        DisplayName = display.Name,
        Dpi = display.IsMain ? 144 : 192,
        Placement = new(display.WorkArea.X + x, display.WorkArea.Y + y, w, h, display.WorkArea.X, display.WorkArea.Y,
            display.WorkArea.Width, display.WorkArea.Height, max, min),
    };

    /// <summary>Twelve windows across both displays, with two of Edge, a Start menu app, a maximized window and minimized ones.</summary>
    internal static List<ArrangementRow> Coding() =>
    [
        Row("Notepad", "notes.txt - Notepad", Notepad, Dell, 0, 0, 1536, 2112, args: @"X:\code\chippi\notes.txt"),
        Row("File Explorer", "chippi", Explorer, Dell, 1536, 0, 768, 1056, args: @"X:\code\chippi"),
        Row("Command Prompt", "Command Prompt", Cmd, Dell, 1536, 1056, 768, 1056, folder: @"X:\code\chippi"),
        Row("Microsoft Edge", "Pull requests · tcurtsinger/Chippi", Edge, Dell, 2304, 0, 1536, 1056),
        Row("Windows Terminal", "pwsh", Terminal, Dell, 2304, 1056, 1536, 1056),
        Row("Microsoft Edge", "DESIGN.md · tcurtsinger/Chippi", Edge, Dell, 2500, 150, 1300, 850, min: true),
        Row("Paint", "Untitled - Paint", Paint, Laptop, 0, 0, 2560, 1552, max: true),
        Row("Calculator", "Calculator", Calculator, Laptop, 80, 380, 640, 900),
        Row("Character Map", "Character Map", CharMap, Laptop, 1280, 120, 1100, 900),
        Row("Remote Desktop Connection", "Remote Desktop Connection", Mstsc, Laptop, 600, 640, 1300, 800),
        Row("Snipping Tool", "Snipping Tool", Snipping, Laptop, 0, 0, 1280, 1540, min: true),
        Row("Fan Control", "Fan Control", @"C:\Program Files\FanControl\FanControl.exe", Laptop, 400, 300, 900, 700, min: true),
    ];

    private static WindowLayoutsSettings Saved(bool shortcut = true) => new()
    {
        Arrangements =
        [
            new() { Id = "coding", Name = "Coding", Rows = Coding(), Shortcut = shortcut ? new KeyShortcut(ShortcutModifiers.Control | ShortcutModifiers.Alt, 0x31) : null },
            new() { Id = "review", Name = "Review", Rows = [.. Coding().Where(r => r.App is "Microsoft Edge" or "Paint" or "Calculator" or "Notepad")] },
            new() { Id = "writing", Name = "Writing", Rows = [.. Coding().Where(r => r.DisplayId == Laptop.Id).Take(3)] },
        ],
    };

    private sealed class Store : IModuleSettings
    {
        public WindowLayoutsSettings Value = new();
        public T Get<T>() where T : class, new() => (T)(object)Value;
        public void Save<T>(T value) where T : class => Value = (WindowLayoutsSettings)(object)value;
    }

    private sealed class Ui : IModuleUi
    {
        public Func<string?, object>? Page;
        public IIslandTile AddIslandTile(IslandTileSpec spec) => new IslandTileContribution(WindowLayoutsModule.Id, spec);
        public void SetSettingsPage(Func<string?, object> page) => Page = page;
        public void AddCommands(Func<IReadOnlyList<ChippiCommand>> commands) { }
    }

    /// <summary>The live windows a capture reads, and a run that places only the windows already open.</summary>
    internal sealed class Desktop : IArrangementDesktop
    {
        public List<ArrangementLiveWindow> Live = [];
        public string? Stop;
        public IReadOnlyList<ArrangementLiveWindow> Windows() => Live;
        public IReadOnlyList<SnapDisplay> Displays() => [Dell, Laptop];
        public string? StopReason(IReadOnlySet<ArrangementProcess> launched) => Stop;
        public ArrangementProcess? Launch(ArrangementRow row) => new(7, 1);
        public void Place(ActionWindow target, SavedWindowPlacement placement, nint monitor) { }
    }

    internal static ArrangementLiveWindow Live(ArrangementRow row, int handle, bool elevated = false, bool fullscreen = false, bool dialog = false)
    {
        var p = row.Placement!;
        var window = new ActionWindow(handle, (uint)handle, 1, 1, dialog ? "#32770" : row.ClassName,
            Path.IsPathFullyQualified(row.Target) ? row.Target : "", row.App, row.TitleHint, elevated);
        return new(new(window, row.AppIdentity, new(p.Left, p.Top, p.Width, p.Height), Eligible: !dialog), row.Target,
            row.DisplayId, row.DisplayName, row.Dpi, p, fullscreen);
    }

    private sealed class Fixture
    {
        public readonly Store Store = new();
        public readonly Ui Ui = new();
        public readonly Desktop Desktop = new();
        public readonly WindowLayoutsModule Module = new(() => [Dell, Laptop]);

        public SnapPage Start(WindowLayoutsSettings settings)
        {
            Store.Value = settings;
            Module.CreateArrangementDesktop = _ => Desktop;
            Module.StartAsync(new ModuleContext(WindowLayoutsModule.Id, Store, NullLogger.Instance, new IslandActivityHub().CreatePublisher(WindowLayoutsModule.Id), Ui)
            { CanShowIslandFeedback = () => true, Shortcuts = new ShortcutService(new Registrar()) }, default).GetAwaiter().GetResult();
            return (SnapPage)Ui.Page!(null);
        }

        public void Stop() => Module.StopAsync(default).GetAwaiter().GetResult();
    }

    private sealed class Registrar : IHotkeyRegistrar
    {
        public bool Register(int id, KeyShortcut keys) => true;
        public void Unregister(int id) { }
        public event Action<int>? Pressed { add { } remove { } }
    }

    /// <summary>Runs the dispatcher until <paramref name="done"/>, at most two seconds.</summary>
    internal static void Pump(Func<bool> done)
    {
        var until = DateTime.UtcNow.AddSeconds(2);
        while (!done() && DateTime.UtcNow < until)
        {
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, () => frame.Continue = false);
            Dispatcher.PushFrame(frame);
        }
        Assert.True(done());
    }

    private static void WarmIcons()
    {
        foreach (string path in new[] { Notepad, Explorer, Cmd, CharMap, Mstsc, Edge }
            .Concat(new[] { Terminal, Paint, Calculator, Snipping }.Select(t => @"shell:AppsFolder\" + t)))
            foreach (int size in new[] { 48, 80 })
                UI.Controls.AppIcon.LoadAsync(path, size).GetAwaiter().GetResult();
        Pump(() => true);
        for (int i = 0; i < 5; i++) Pump(() => true);
    }

    private static Button ButtonNamed(DependencyObject root, string name) =>
        Find<Button>(root).First(b => System.Windows.Automation.AutomationProperties.GetName(b) == name);

    [Fact]
    public void The_list_shows_each_arrangement_with_its_apps_shortcut_and_launch()
    {
        WpfTestThread.Run(() =>
        {
            Theme();
            WarmIcons();
            var f = new Fixture();
            var page = f.Start(Saved());
            Layout(page, 768);
            Pump(() => true);
            var texts = Texts(page);
            Assert.Contains("12 windows · 2 displays", texts);
            Assert.Contains("3 windows · Built-in display", texts);
            Assert.Contains("3 arrangements", texts);
            Assert.Equal(["Coding", "Review", "Writing"], texts.Where(t => t is "Coding" or "Review" or "Writing").Distinct());
            Assert.True(ButtonNamed(page, "Launch Review").IsEnabled);
            Shot(page, "arrangements", 768, around: "Arrangements");
            Shot(page, "arrangements-narrow", 640, around: "Arrangements");
            f.Stop();

            var empty = new Fixture();
            page = empty.Start(new());
            Layout(page, 768);
            Assert.Contains("No arrangements", Texts(page));
            Shot(page, "arrangements-empty", 768, around: "Arrangements");
            empty.Stop();
        });
    }

    [Fact]
    public void New_arrangement_captures_at_once_and_refuses_what_it_cannot_save()
    {
        WpfTestThread.Run(() =>
        {
            Theme();
            WarmIcons();
            var f = new Fixture();
            var page = f.Start(new());
            int handle = 100;
            f.Desktop.Live = [.. Coding().Select(r => Live(r, handle++)),
                Live(Row("Task Manager", "Task Manager", Path.Combine(Sys, "taskmgr.exe"), Dell, 0, 0, 900, 700), handle++, elevated: true),
                Live(Row("Notepad", "Save As", Notepad, Dell, 200, 200, 800, 600), handle++, dialog: true),
                Live(Row("Media Player", "Movies", "Microsoft.ZuneVideo_8wekyb3d8bbwe!Microsoft.ZuneVideo", Laptop, 0, 0, 2560, 1600), handle++, fullscreen: true)];
            // Fan Control's program wasn't found: it has no app to launch until one is picked.
            f.Desktop.Live[11] = f.Desktop.Live[11] with { Target = "" };
            var section = page.Arrangements;
            section.New();
            Pump(() => section.Current is { Capturing: false });
            var draft = section.Current!;
            Assert.Equal(12, draft.Rows.Count);
            Assert.Equal(["Task Manager (admin)", "Save As (dialog)", "Media Player (fullscreen)"], draft.Skipped);
            Assert.Contains(draft.Rows[11].Id, draft.Open); // opened so the missing app is in view
            Assert.Equal("Fan Control needs an app to launch", section.Refusal(draft));
            Layout(page, 768);
            Pump(() => true);
            var texts = Texts(page);
            Assert.Contains("DELL U2723QE · Main display · 6 windows", texts);
            Assert.Contains("Built-in display · 6 windows", texts);
            Assert.Contains("1536 × 2112 at 0, 0", texts);
            Assert.Contains("Minimized · 1300 × 850 at 2500, 150", texts);
            Assert.Contains("Maximized", texts);
            Assert.Contains("3 windows not captured", texts);
            Assert.Contains("No app to launch", texts);
            Assert.Contains("Fan Control needs an app to launch", texts);
            Assert.False(ButtonNamed(page, "Save").IsEnabled);
            Shot(page, "arrangements-refused-app", 768, around: "Snipping Tool");

            // Removing Fan Control and naming it saves; launch details show for a program and a Start menu app.
            ButtonNamed(page, "Remove Fan Control").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            ButtonNamed(page, "Launch details for notes.txt - Notepad").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            ButtonNamed(page, "Launch details for pwsh").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Field(page, "Name").Text = "Coding";
            // Capture never reads a command line: arguments are typed.
            Field(page, "Arguments").Text = @"X:\code\chippi\notes.txt";
            Layout(page, 768);
            Pump(() => true);
            Assert.Equal("Start menu", Texts(page).First(t => t == "Start menu"));
            Assert.True(ButtonNamed(page, "Save").IsEnabled);
            Shot(page, "arrangements-review", 768, around: "Arrangements", tall: 3000);
            Shot(page, "arrangements-review-narrow", 640, around: "Arrangements", tall: 3000);

            // A working folder must be a full path.
            Field(page, "Working folder").Text = "chippi";
            Assert.Equal("Notepad's working folder needs a full path", section.Refusal(draft));
            Field(page, "Working folder").Text = "";
            ButtonNamed(page, "Save").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var saved = Assert.Single(f.Store.Value.Arrangements);
            Assert.Equal("Coding", saved.Name);
            Assert.Equal(11, saved.Rows.Count);
            Assert.Equal(@"X:\code\chippi\notes.txt", saved.Rows[0].Arguments);
            Assert.Null(section.Current);

            // Another with the same name, and one with every window removed.
            section.New();
            Pump(() => section.Current is { Capturing: false });
            Field(page, "Name").Text = "coding ";
            Assert.Equal(ArrangementsSection.NameTaken, section.Refusal(section.Current!));
            foreach (var row in section.Current!.Rows.Skip(1).ToList()) section.Current.Rows.Remove(row);
            section.Current.Skipped = [];
            section.Open(section.Current);
            Layout(page, 768);
            Assert.Contains(ArrangementsSection.NameTaken, Texts(page));
            Shot(page, "arrangements-refused-name", 768, around: "New arrangement");
            ButtonNamed(page, $"Remove {section.Current.Rows[0].TitleHint}").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Field(page, "Name").Text = "Coding 2";
            Layout(page, 768);
            Assert.Contains("No windows", Texts(page));
            Assert.Contains(ArrangementsSection.NoWindows, Texts(page));
            Shot(page, "arrangements-refused-empty", 768, around: "New arrangement");
            f.Stop();
        });
    }

    [Fact]
    public void Edit_shows_the_shortcut_read_only_and_recapture_keeps_launch_details()
    {
        WpfTestThread.Run(() =>
        {
            Theme();
            WarmIcons();
            var f = new Fixture();
            var page = f.Start(Saved());
            var section = page.Arrangements;
            var coding = f.Store.Value.Arrangements[0];
            section.Edit(coding);
            Layout(page, 768);
            var texts = Texts(page);
            Assert.Contains("Edit arrangement", texts);
            Assert.NotEqual("Not set", Find<Chippi.UI.Controls.ShortcutSetting>(page).Single().KeysText);
            Shot(page, "arrangements-edit", 768, around: "Edit arrangement");

            // Recapture: Edge's second window is gone, two new windows arrived; the Notepad row keeps its arguments.
            int handle = 300;
            f.Desktop.Live = [.. Coding().Where(r => r.TitleHint != "DESIGN.md · tcurtsinger/Chippi").Select(r => Live(r with { Arguments = "", WorkingFolder = "" }, handle++)),
                Live(Row("Microsoft Edge", "Chippi widget - Figma", Edge, Dell, 2304, 0, 1536, 1056), handle++),
                Live(Row("Remote Desktop Connection", "build-box", Mstsc, Dell, 1536, 1056, 768, 1056), handle++)];
            ButtonNamed(page, "Recapture").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Pump(() => section.Current is { Capturing: false });
            var draft = section.Current!;
            Assert.Equal("Recaptured · 2 new · 1 gone", draft.Recaptured);
            Assert.Equal(@"X:\code\chippi\notes.txt", draft.Rows.Single(r => r.TitleHint == "notes.txt - Notepad").Arguments);
            Layout(page, 768);
            texts = Texts(page);
            Assert.Contains("Recaptured · 2 new · 1 gone", texts);
            Assert.Contains("1536 × 2112 at 0, 0 · launch details kept", texts);
            Assert.Equal(2, texts.Count(t => t == "New"));
            Shot(page, "arrangements-recaptured", 768, around: "Edit arrangement", tall: 3000);

            // Cancel keeps what was saved.
            ButtonNamed(page, "Cancel").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(12, f.Store.Value.Arrangements.Single(a => a.Id == "coding").Rows.Count);
            Assert.Null(section.Current);

            // Delete asks first.
            var confirm = SnapPage.ConfirmPanel(() => { }, () => { }, out _, "Delete Coding?");
            Shot(confirm, "arrangements-delete", 300);
            f.Stop();
        });
    }

    private sealed class ShortcutSettingProbe : FrameworkElement;

    [Fact]
    public void A_launch_from_Settings_shows_its_run_and_turns_the_others_off()
    {
        WpfTestThread.Run(() =>
        {
            Theme();
            WarmIcons();
            var f = new Fixture();
            var page = f.Start(Saved());
            // Five of Coding's windows are open; the rest are opening.
            int handle = 500;
            f.Desktop.Live = [.. Coding().Take(5).Select(r => Live(r, handle++))];
            Layout(page, 768);
            ButtonNamed(page, "Launch Coding").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Pump(() => f.Module.Arrangements!.Results.Count(r => r.State == ArrangementRowState.Ready) == 5);
            Pump(() => Texts(page).Contains("Opening · 5 of 12 ready"));
            Layout(page, 768);
            Assert.True(ButtonNamed(page, "Cancel Coding").IsEnabled);
            Assert.False(ButtonNamed(page, "Launch Review").IsEnabled);
            Shot(page, "arrangements-running", 768, around: "Arrangements");
            ButtonNamed(page, "Cancel Coding").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Pump(() => !f.Module.Arrangements!.IsRunning);
            Pump(() => ButtonNamed(page, "Launch Review").IsEnabled);
            Assert.Contains(f.Module.Arrangements!.Results, r => r.State == ArrangementRowState.Cancelled);

            // A refused launch says why on its row.
            f.Desktop.Stop = "Paused";
            ButtonNamed(page, "Launch Writing").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Pump(() => Texts(page).Contains("Not launched · Paused"));
            f.Stop();
        });
    }
}
