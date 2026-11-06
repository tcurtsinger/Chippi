// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Input;
using Chippi.Capture;
using Chippi.Core.Clipboard;
using Chippi.Core.Island;
using Chippi.Core.Modules;
using Chippi.Core.Shortcuts;
using Chippi.UI.Capture;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Chippi.Features.ColorPicker.Tests;

/// <summary>Pick color and Pick and review color against a fake capture layer and clipboard hub.</summary>
public sealed class PickFlowTests
{
    private sealed class Session(CaptureRequest request) : ICaptureSession
    {
        public CaptureRequest Request { get; } = request;
        public Freeze? Freeze => null;
        public Selection? Selection => null;
        public IReadOnlyList<CaptureSurface> Surfaces => [];
        public bool Tooling { get; set; }
        public CaptureEnd? Ended;
#pragma warning disable CS0067
        public event Action<Selection>? Selected;
        public event Action? Frozen;
        public event Action<KeyEventArgs>? KeyDown;
#pragma warning restore CS0067
        public event Action<PixelPoint, uint>? Picked;
        public event Action<CaptureEnd>? Closed;
        public void SetBars(FrameworkElement? tools, FrameworkElement? output) { }
        public void Close(CaptureEnd end = CaptureEnd.Finished, bool handBack = true) { Ended = end; Closed?.Invoke(end); }
        public void Pick(uint argb) => Picked?.Invoke(new PixelPoint(1, 1), argb);
        public void Cancel() => Close(CaptureEnd.Cancelled);
    }

    private sealed class Layer : ICaptureLayer
    {
        public Session? Last;
        public int Disposed;
        public ICaptureSession? Current => Last is { Ended: null } ? Last : null;
        public PixelRect? LastRegion { get; set; }
        public IDisposable Use() => new Disposer(() => Disposed++);
        public ICaptureSession? Start(CaptureRequest request) => Current is not null ? null : Last = new Session(request);
    }

    private sealed class Disposer(Action a) : IDisposable { public void Dispose() => a(); }

    private sealed class Clipboard : IClipboardHub
    {
        public List<string> Written = [];
        public ClipStatus Result = ClipStatus.Ok;
        public IDisposable Listen(Action<ClipCopy> copied, Func<string, bool>? skip = null) => new Disposer(() => { });
        public Task<ClipRead> ReadAsync(CancellationToken cancel = default) => throw new NotSupportedException();
        public Task<ClipStatus> WriteAsync(ClipContent content, CancellationToken cancel = default) { Written.Add(content.Text ?? ""); return Task.FromResult(Result); }
        public PasteTarget? CaptureTarget() => null;
        public Task<PasteOutcome> PasteAsync(ClipContent content, PasteTarget target, CancellationToken cancel = default) => throw new NotSupportedException();
        public Task<PasteOutcome> PastePlainTextAsync(CancellationToken cancel = default) => throw new NotSupportedException();
        public void Beep() { }
    }

    private sealed class Settings : IModuleSettings
    {
        public ColorPickerSettings Value = new();
        public T Get<T>() where T : class, new() => (T)(object)Value;
        public void Save<T>(T value) where T : class => Value = (ColorPickerSettings)(object)value;
    }

    private sealed class Ui : IModuleUi
    {
        public Func<IReadOnlyList<ChippiCommand>>? Commands;
        public IIslandTile AddIslandTile(IslandTileSpec spec) => default!;
        public void SetSettingsPage(Func<string?, object> page) { }
        public void AddCommands(Func<IReadOnlyList<ChippiCommand>> commands) => Commands = commands;
    }

    private sealed class Keys : IShortcutService
    {
        public Dictionary<string, Hotkey> Added = [];
        public event Action? Changed { add { } remove { } }
        public IReadOnlyList<ShortcutEntry> List => [];
        public IHotkey Add(string owner, string name) => throw new NotSupportedException();
        public IHotkey Add(string owner, string name, ShortcutEdits edits) => Added[name] = new Hotkey(edits);
        public string? Check(KeyShortcut shortcut, long? ignore = null) => null;
        public string? Change(long id, KeyShortcut shortcut) => null;
        public void Remove(long id) { }
    }

    private sealed class Hotkey(ShortcutEdits edits) : IHotkey
    {
        public ShortcutEdits Edits = edits;
        public Action? Pressed;
        public bool Disposed;
        public bool Register(KeyShortcut shortcut, Action pressed) { Pressed = pressed; return true; }
        public void Dispose() => Disposed = true;
    }

    private sealed record Rig(ColorPickerModule Module, Layer Layer, Clipboard Clip, Settings Settings, IslandActivityHub Hub, List<string> Pages, Ui Ui, Keys Keys);

    private static Rig Start(ColorPickerSettings? saved = null)
    {
        var layer = new Layer();
        var clip = new Clipboard();
        var settings = new Settings { Value = saved ?? new() };
        var hub = new IslandActivityHub();
        var ui = new Ui();
        var keys = new Keys();
        var pages = new List<string>();
        var module = new ColorPickerModule(layer);
        module.StartAsync(new ModuleContext(ColorPickerModule.Id, settings, NullLogger.Instance, hub.CreatePublisher(ColorPickerModule.Id), ui)
        { Clipboard = clip, Shortcuts = keys, OpenIslandPage = pages.Add }, default).GetAwaiter().GetResult();
        return new Rig(module, layer, clip, settings, hub, pages, ui, keys);
    }

    [Fact]
    public void Pick_copies_the_sampled_pixel_once_and_shows_the_bar_line_with_a_swatch()
    {
        var t = Start();
        t.Module.Pick(review: false);
        Assert.False(t.Layer.Last!.Request.Dim);
        Assert.Equal(Chippi.Capture.CaptureMode.PickPoint, t.Layer.Last.Request.Mode);
        t.Layer.Last.Pick(0xFF7C5CFF);
        Assert.Equal(["#7C5CFF"], t.Clip.Written);
        var line = Assert.Single(t.Hub.Activities);
        Assert.Equal("#7C5CFF", line.Swatch);
        Assert.Equal("#7C5CFF", line.Title);
        Assert.Equal("Copied as HEX", line.Source);
        Assert.Equal(["#7C5CFF"], t.Module.Recent);
        Assert.Equal("HEX", t.Module.CopiedAs);
        Assert.Empty(t.Pages);
        Assert.Equal(CaptureEnd.Finished, t.Layer.Last.Ended);
    }

    [Fact]
    public void Pick_copies_in_the_settings_format()
    {
        var t = Start(new ColorPickerSettings { CopyFormat = "RGB" });
        t.Module.Pick(review: false);
        t.Layer.Last!.Pick(0xFF7C5CFF);
        Assert.Equal(["rgb(124, 92, 255)"], t.Clip.Written);
        Assert.Equal("Copied as RGB", t.Hub.Activities.Single().Source);
    }

    [Fact]
    public void Esc_copies_nothing_and_leaves_the_palette()
    {
        var t = Start(new ColorPickerSettings { Recent = ["#112233"] });
        t.Module.Pick(review: false);
        t.Layer.Last!.Cancel();
        Assert.Empty(t.Clip.Written);
        Assert.Empty(t.Hub.Activities);
        Assert.Equal(["#112233"], t.Module.Recent);
    }

    [Fact]
    public void A_refused_write_says_Copy_failed()
    {
        var t = Start();
        t.Clip.Result = ClipStatus.Busy;
        t.Module.Pick(review: false);
        t.Layer.Last!.Pick(0xFF7C5CFF);
        var line = Assert.Single(t.Hub.Activities);
        Assert.Equal("Copy failed", line.Title);
        Assert.True(line.NeedsAttention);
        Assert.Null(t.Module.CopiedAs);
    }

    [Fact]
    public void Pick_and_review_copies_nothing_and_opens_the_page()
    {
        var t = Start();
        t.Module.Pick(review: true);
        t.Layer.Last!.Pick(0xFF7C5CFF);
        Assert.Empty(t.Clip.Written);
        Assert.Equal([ColorPickerModule.Id], t.Pages);
        Assert.Equal("#7C5CFF", t.Module.Color!.Hex);
        Assert.Null(t.Module.CopiedAs);
        Assert.Equal(["#7C5CFF"], t.Module.Recent);
    }

    [Fact]
    public void A_second_starter_while_one_runs_is_ignored()
    {
        var t = Start();
        t.Module.Pick(review: false);
        var first = t.Layer.Last;
        t.Module.Pick(review: true);
        Assert.Same(first, t.Layer.Last);
    }

    [Fact]
    public void The_shortcuts_are_listed_set_or_not_and_saved_when_changed()
    {
        var t = Start();
        Assert.Equal([ColorPickerModule.ReviewName, ColorPickerModule.PickName], t.Keys.Added.Keys.Order());
        var edits = t.Keys.Added[ColorPickerModule.PickName].Edits;
        Assert.Null(edits.Saved());
        var chord = new KeyShortcut(ShortcutModifiers.Control | ShortcutModifiers.Alt, 0x43);
        Assert.Null(edits.Change(chord));
        Assert.Equal(chord, t.Settings.Value.PickKeys);
        t.Keys.Added[ColorPickerModule.PickName].Pressed!();
        Assert.NotNull(t.Layer.Last);
        edits.Remove();
        Assert.Null(t.Settings.Value.PickKeys);
    }

    [Fact]
    public async Task The_two_verbs_are_commands_and_stop_releases_the_layer()
    {
        var t = Start();
        Assert.Equal(["color-picker/pick", "color-picker/review"], t.Ui.Commands!().Select(c => c.Key));
        await t.Module.StopAsync(default);
        Assert.Equal(1, t.Layer.Disposed);
        Assert.All(t.Keys.Added.Values, h => Assert.True(h.Disposed));
    }

    [Fact]
    public void Pick_again_starts_Pick_and_review()
    {
        var t = Start();
        ((IColorPageModel)t.Module).PickAgain();
        Assert.NotNull(t.Layer.Last);
    }

    [Fact]
    public void The_settings_model_lists_the_formats_of_the_newest_color_and_saves_the_format()
    {
        var t = Start(new ColorPickerSettings { Recent = ["#FF0000", "#00FF00"] });
        var model = t.Module.Settings;
        Assert.Equal(["HEX", "RGB", "HSL", "HSV", "CMYK"], model.Formats.Select(f => f.Name));
        Assert.Equal("#FF0000", model.Formats[0].Value);
        Assert.Equal("#7C5CFF", Start().Module.Settings.Formats[0].Value);
        int raised = 0;
        model.Changed += () => raised++;
        Assert.True(model.SetCopyAs("RGB"));
        Assert.Equal("RGB", t.Settings.Value.CopyFormat);
        Assert.False(model.SetCopyAs("nope"));
        model.ClearRecent();
        Assert.Empty(model.Recent);
        Assert.Equal(2, raised);
    }
}
