// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows.Media;
using Chippi.Core.Clipboard;
using Chippi.Core.Island;
using Chippi.Core.Modules;
using Chippi.Features.CommandBar;
using Chippi.Features.CommandBar.Search;
using Chippi.Features.RadialMenu.Input;
using Chippi.Features.RadialMenu.Pc;
using Chippi.Features.RadialMenu.Slices;
using Chippi.Features.Sound.Mute;
using Microsoft.Extensions.Logging.Abstractions;

namespace Chippi.Features.RadialMenu.Tests;

internal sealed class Fixture
{
    public FakePc Pc { get; } = new();
    public FakeInput Input { get; } = new();
    public FakeHotkey Hotkey { get; } = new();
    public FakeHub Hub { get; } = new();
    public FakeSettings Settings { get; } = new();
    public List<string> RanCommands { get; } = [];
    public bool Cleaning { get; init; }
    public bool KeepAwake { get; init; }
    public List<Slice> Slices { init => Settings.Value = Settings.Value with { Slices = value }; }

    private RadialMenuModule? _module;

    public RadialMenuModule Module => _module ??= new RadialMenuModule(() => Pc, sink => Input, () => Hotkey, new FakeRecorder(), () => Cleaning, () => KeepAwake, () => WebEngine.DuckDuckGo);

    public SliceRunner Runner() => new(Pc, Commands, () => Hub, () => WebEngine.DuckDuckGo);

    public Task StartAsync() => Module.StartAsync(new ModuleContext(RadialMenuModule.Id, Settings, NullLogger.Instance, Hub, new FakeUi())
    {
        Commands = Commands,
        Clipboard = Hub,
    }, default);

    private IReadOnlyList<ChippiCommand> Commands() =>
    [
        new("keep-awake/toggle", "Keep awake", "Coffee", () =>
        {
            RanCommands.Add("keep-awake/toggle");
            return Task.CompletedTask;
        }),
        new("clipboard/7", "A copy", "Clipboard", () => Task.CompletedTask) { Kind = ChippiCommandKind.Paste },
        new("command-bar/open", "Command bar", "Search", () => Task.CompletedTask),
        new("mic-mute/toggle", "Mute microphone", "Mic", () => Task.CompletedTask),
    ];
}

/// <summary>A screen far off every real one, so a test's wheel never shows where the owner looks.</summary>
internal sealed class FakePc : IRadialPc
{
    public static readonly Screen Far = new(-20000, -20000, 1920, 1080, 1);

    public PixelPoint Middle => new(Far.Left + 960, Far.Top + 540);
    public bool Shown { get; set; } = true;
    public nint Under { get; set; } = 42;
    public (bool, string?) Terminal { get; set; } = (false, null);
    public List<string> Opened { get; } = [];
    public List<KeyCombo> Pressed { get; } = [];
    public List<string> Typed { get; } = [];
    public List<(CommandShell, string)> Ran { get; } = [];
    public List<MediaAction> Played { get; } = [];
    public List<(nint, WindowAction)> Windowed { get; } = [];

    public IReadOnlyList<StartApp> AppList { get; set; } = [new("Spotify", "Spotify"), new("File Explorer", "Microsoft.Windows.Explorer")];

    public IReadOnlyList<StartApp> Apps => AppList;

    public event Action? AppsChanged { add { } remove { } }

    public void RefreshApps() { }

    /// <summary>Apps get a stand-in icon (the shots'): a rounded square in a colour of their own with their first letter.</summary>
    public bool Icons { get; set; }

    public Task<ImageSource?> IconAsync(string appOrPath, int pixels)
    {
        if (!Icons)
            return Task.FromResult<ImageSource?>(null);
        var (color, mark) = appOrPath switch
        {
            "Chrome" => (Color.FromRgb(0x1A, 0x73, 0xE8), "C"),
            "Spotify" => (Color.FromRgb(0x1D, 0xB9, 0x54), "S"),
            "Microsoft.WindowsTerminal" => (Color.FromRgb(0x3A, 0x3A, 0x3E), ">"),
            "Microsoft.VisualStudioCode" => (Color.FromRgb(0x00, 0x7A, 0xCC), "V"),
            _ => (Color.FromRgb(0x5A, 0x6B, 0x86), appOrPath[..1].ToUpperInvariant()),
        };
        var letter = new FormattedText(mark, System.Globalization.CultureInfo.InvariantCulture, System.Windows.FlowDirection.LeftToRight,
            new Typeface("Segoe UI Semibold"), 13, Brushes.White, 1);
        var group = new DrawingGroup();
        using (var dc = group.Open())
        {
            dc.DrawRoundedRectangle(new SolidColorBrush(color), null, new System.Windows.Rect(0, 0, 24, 24), 6, 6);
            dc.DrawText(letter, new System.Windows.Point(12 - letter.Width / 2, 12 - letter.Height / 2));
        }
        var image = new DrawingImage(group);
        image.Freeze();
        return Task.FromResult<ImageSource?>(image);
    }

    public bool Exists(string path) => path is @"C:\Users\Travis\Downloads" or @"C:\Tools\tool.exe";

    public (PixelPoint At, bool Shown) Pointer() => (Middle, Shown);

    public Screen ScreenAt(PixelPoint at) => Far;

    public nint WindowAt(PixelPoint at) => Under;

    public void Open(string target) => Opened.Add(target);

    public void Window(nint window, WindowAction action)
    {
        if (window == 0)
            throw new SliceFailure("No window");
        Windowed.Add((window, action));
    }

    public void Media(MediaAction action) => Played.Add(action);

    public Task PressAsync(KeyCombo keys)
    {
        Pressed.Add(keys);
        return Task.CompletedTask;
    }

    public Task TypeAsync(string text)
    {
        Typed.Add(text);
        return Task.CompletedTask;
    }

    public void Run(CommandShell shell, string line) => Ran.Add((shell, line));

    public Task<(bool Terminal, string? Text)> TerminalSelectionAsync() => Task.FromResult(Terminal);

    public void Dispose() { }
}

internal sealed class FakeInput : IWheelInput
{
    public (bool, bool, bool) Watched { get; private set; }
    public Func<bool> Paused { get; set; } = () => false;

    public bool OpenOverFullscreen { get; set; } = true;
    public int ClosedCount { get; private set; }
    public int ByShortcut { get; private set; }
    public PixelPoint? Virtual { get; private set; }

    public void Watch(bool middle, bool back, bool forward) => Watched = (middle, back, forward);

    public void OpenedByShortcut(bool virtualPointer, PixelPoint at) => ByShortcut++;

    public void UseVirtualPointer(PixelPoint at) => Virtual = at;

    public void Closed() => ClosedCount++;

    public void Dispose() { }
}

internal sealed class FakeHotkey : IMicHotkey
{
    public Action? Pressed;

    public bool Register(MicShortcut shortcut, Action pressed)
    {
        Pressed = pressed;
        return true;
    }

    public void Dispose() => Pressed = null;
}

internal sealed class FakeRecorder : IKeyRecorder
{
    public IDisposable Record(Action<KeyCombo?> recorded) => new NoRecording();

    internal sealed class NoRecording : IDisposable
    {
        public void Dispose() { }
    }
}

/// <summary>The clipboard hub and the island, both fake: what was written, the selection, the bar's activities.</summary>
internal sealed class FakeHub : IClipboardHub, IIslandPublisher
{
    public string? Selection { get; set; }
    public int SelectionsCopied { get; private set; }
    public List<string> Written { get; } = [];
    public List<IslandActivity> Activities { get; } = [];

    public Task<string?> CopySelectionAsync(CancellationToken cancel = default)
    {
        SelectionsCopied++;
        return Task.FromResult(Selection);
    }

    public Task<ClipStatus> WriteAsync(ClipContent content, CancellationToken cancel = default)
    {
        Written.Add(content.Text ?? "");
        return Task.FromResult(ClipStatus.Ok);
    }

    public IDisposable Listen(Action<ClipCopy> copied, Func<string, bool>? skip = null) => throw new NotSupportedException();
    public Task<ClipRead> ReadAsync(CancellationToken cancel = default) => throw new NotSupportedException();
    public PasteTarget? CaptureTarget() => null;
    public Task<PasteOutcome> PasteAsync(ClipContent content, PasteTarget target, CancellationToken cancel = default) => throw new NotSupportedException();
    public Task<PasteOutcome> PastePlainTextAsync(CancellationToken cancel = default) => throw new NotSupportedException();
    public void Beep() { }

    public void Publish(IslandActivity activity)
    {
        Activities.RemoveAll(a => a.Key == activity.Key);
        Activities.Add(activity);
    }

    public void Clear(string key) => Activities.RemoveAll(a => a.Key == key);

    public void ReportSuccess(string? message = null) { }
}

internal sealed class FakeSettings : IModuleSettings
{
    public RadialMenuSettings Value = new();
    public T Get<T>() where T : class, new() => (T)(object)Value;
    public void Save<T>(T value) where T : class => Value = (RadialMenuSettings)(object)value;
}

internal sealed class FakeUi : IModuleUi
{
    public IIslandTile AddIslandTile(IslandTileSpec spec) => new IslandTileContribution(RadialMenuModule.Id, spec);

    public void SetSettingsPage(Func<string?, object> page) { }
}
