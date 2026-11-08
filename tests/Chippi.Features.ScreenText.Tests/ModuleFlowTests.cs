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

namespace Chippi.Features.ScreenText.Tests;

/// <summary>The three verbs against a fake capture layer, OCR, QR decoder and clipboard hub.</summary>
public sealed class ModuleFlowTests
{
    private sealed class Session(CaptureRequest request) : ICaptureSession
    {
        public CaptureRequest Request { get; } = request;
        public Freeze? Freeze => null;
        public Selection? Selection => null;
        public IReadOnlyList<CaptureSurface> Surfaces => [];
        public bool Tooling { get; set; }
        public CaptureEnd? Ended;
        public bool HandedBack;
        public event Action<Selection>? Selected;
#pragma warning disable CS0067
        public event Action<PixelPoint, uint>? Picked;
        public event Action? Frozen;
        public event Action<KeyEventArgs>? KeyDown;
#pragma warning restore CS0067
        public event Action<CaptureEnd>? Closed;
        public void SetBars(FrameworkElement? tools, FrameworkElement? output) { }
        public void Close(CaptureEnd end = CaptureEnd.Finished, bool handBack = true) { Ended = end; HandedBack = handBack; Closed?.Invoke(end); }
        public void Release(int width, int height) => Selected?.Invoke(Selection_(width, height));
        public void Cancel() => Close(CaptureEnd.Cancelled);

        // A key the layer passes to the feature once a region is released. WPF key events need an STA thread.
        public void Press(Key key)
        {
            var thread = new Thread(() => KeyDown?.Invoke(new KeyEventArgs(Keyboard.PrimaryDevice, new UnshownSource(), 0, key) { RoutedEvent = Keyboard.KeyDownEvent }));
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
        }

        // The module reads the pixels from selection.Image(); a frozen screen of one flat colour stands in.
        private static Selection Selection_(int width, int height)
        {
            var pixels = new byte[width * height * 4];
            Array.Fill(pixels, (byte)255);
            var screen = new CapturedScreen("D", 0, new PixelRect(0, 0, width, height), 96, pixels);
            var freeze = new Freeze([screen], [], DateTimeOffset.Now);
            return new Selection(new PixelRect(0, 0, width, height), freeze, Chippi.Capture.CaptureMode.Region, null, null, false, screen);
        }
    }

    private sealed class UnshownSource : PresentationSource
    {
        public override System.Windows.Media.Visual RootVisual { get; set; } = null!;
        public override bool IsDisposed => false;
        protected override System.Windows.Media.CompositionTarget GetCompositionTargetCore() => null!;
    }

    private sealed class Layer : ICaptureLayer
    {
        public Session? Last;
        public int Disposed;
        public ICaptureSession? Current => Last is { Ended: null } ? Last : null;
        public PixelRect? LastRegion { get; set; }
        public IDisposable Use() => new Disposer(() => Disposed++);
        public ICaptureSession? Start(CaptureRequest request) => Current is not null ? null : Last = new Session(request);
        public ICaptureSession? Reopen(CapturedImage image, CaptureDoor door) => null;
    }

    private sealed class Disposer(Action a) : IDisposable { public void Dispose() => a(); }

    private sealed class Ocr : IOcr
    {
        public OcrOutcome Next = new(TextReadState.Read, [new OcrLine("Hello world", new Rect(0, 0, 50, 10)), new OcrLine("from the screen", new Rect(0, 12, 70, 10))], "English");
        public Task<OcrOutcome>? Hold;
        public int Calls;
        public string? LastLanguage;
        public IReadOnlyList<(string Tag, string Name)> Languages() => [("en-US", "English")];
        public Task<OcrOutcome> RecognizeAsync(CapturedImage image, string? language, CancellationToken ct) { Calls++; LastLanguage = language; return Hold ?? Task.FromResult(Next); }
    }

    private sealed class Qr : IQrDecoder
    {
        public IReadOnlyList<QrHit> Hits = [new QrHit("https://example.com/", new Point(10, 10))];
        public bool Throw, Damaged;
        public IReadOnlyList<QrHit> Decode(CapturedImage image) => Throw ? throw new InvalidOperationException() : Hits;
        public bool LooksLikeCode(CapturedImage image) => Damaged;
    }

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
        public ScreenTextSettings Value = new();
        public T Get<T>() where T : class, new() => (T)(object)Value;
        public void Save<T>(T value) where T : class => Value = (ScreenTextSettings)(object)value;
    }

    private sealed class Ui : IModuleUi
    {
        public Func<IReadOnlyList<ChippiCommand>>? Commands;
        public List<IslandTileSpec> Specs = [];
        public List<string> Tried = [];
        public IIslandTile AddIslandTile(IslandTileSpec spec) { Specs.Add(spec); return default!; }
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

    private sealed record Rig(ScreenTextModule Module, Layer Layer, Ocr Ocr, Qr Qr, Clipboard Clip, Settings Settings, IslandActivityHub Hub, List<string> Pages, Ui Ui, Keys Keys);

    private static async Task<Rig> Start(ScreenTextSettings? saved = null, TimeProvider? time = null)
    {
        var layer = new Layer();
        var ocr = new Ocr();
        var qr = new Qr();
        var clip = new Clipboard();
        var settings = new Settings { Value = saved ?? new() };
        var hub = new IslandActivityHub();
        var ui = new Ui();
        var keys = new Keys();
        var pages = new List<string>();
        var module = new ScreenTextModule(layer, ocr, qr) { Time = time ?? TimeProvider.System };
        await module.StartAsync(new ModuleContext(ScreenTextModule.Id, settings, NullLogger.Instance, hub.CreatePublisher(ScreenTextModule.Id), ui)
        { Clipboard = clip, Shortcuts = keys, OpenIslandPage = pages.Add, TryOpenIslandPage = key => { ui.Tried.Add(key); return key == "capture"; } }, default);
        return new Rig(module, layer, ocr, qr, clip, settings, hub, pages, ui, keys);
    }

    private static async Task Select(Rig t, ScreenTextVerb verb, int w = 120, int h = 40)
    {
        t.Module.Start(verb);
        t.Layer.Last!.Release(w, h);
        await t.Module.Work;
    }

    [Fact]
    public async Task Existing_capture_is_read_without_opening_another_selector_and_removal_stops_reads()
    {
        var t = await Start();
        var image = new CapturedImage(120, 40, new byte[120 * 40 * 4]);
        await t.Module.ReadSelectionAsync(image, ScreenTextVerb.CopyText);
        Assert.Null(t.Layer.Last);
        Assert.Equal(1, t.Ocr.Calls);
        Assert.Single(t.Clip.Written);
        await t.Module.ReadSelectionAsync(image, ScreenTextVerb.ScanQr);
        Assert.Null(t.Layer.Last);
        Assert.Equal(QrScanState.Read, t.Module.Qr.State);
        await t.Module.StopAsync(default);
        await t.Module.ReadSelectionAsync(image, ScreenTextVerb.CopyText);
        Assert.Equal(1, t.Ocr.Calls);
    }
    // Travis's live check, 2026-11-08: the capture bar's Copy text copies and opens the Text page.
    [Fact]
    public async Task Copy_text_from_a_capture_copies_and_opens_the_Text_page()
    {
        var t = await Start();
        var image = new CapturedImage(120, 40, new byte[120 * 40 * 4]);
        await t.Module.ReadSelectionAsync(image, ScreenTextVerb.CopyText);
        Assert.Equal(["Hello world\nfrom the screen"], t.Clip.Written);
        var line = Assert.Single(t.Hub.Activities);
        Assert.Equal(("Copied text", "5 words"), (line.Title, line.Source));
        Assert.Equal([ScreenTextModule.Id], t.Pages);
        Assert.Equal("Hello world\nfrom the screen", t.Module.Text.Result!.Text);
        Assert.False(t.Module.Text.Reading);
    }

    [Fact]
    public async Task Copy_text_from_a_capture_with_nothing_read_leaves_the_clipboard_and_opens_the_page()
    {
        var t = await Start();
        t.Ocr.Next = new OcrOutcome(TextReadState.NoText, [], "English");
        await t.Module.ReadSelectionAsync(new CapturedImage(120, 40, new byte[120 * 40 * 4]), ScreenTextVerb.CopyText);
        Assert.Empty(t.Clip.Written);
        Assert.Empty(t.Hub.Activities);
        Assert.Equal([ScreenTextModule.Id], t.Pages);
        Assert.Equal(TextReadState.NoText, t.Module.Text.Result!.State);
    }

    [Fact]
    public async Task The_result_line_clears_after_the_notice_time_and_a_new_line_restarts_it()
    {
        var time = new Chippi.Capture.Tests.ManualTime();
        var t = await Start(time: time);
        await Select(t, ScreenTextVerb.CopyText);
        Assert.Single(t.Hub.Activities);
        time.Advance(TimeSpan.FromSeconds(5));
        Assert.Single(t.Hub.Activities);
        await Select(t, ScreenTextVerb.CopyText);
        time.Advance(TimeSpan.FromSeconds(5));
        Assert.Single(t.Hub.Activities);
        time.Advance(TimeSpan.FromSeconds(1));
        Assert.Empty(t.Hub.Activities);
    }

    [Fact]
    public async Task Copy_text_copies_the_text_once_and_says_how_many_words()
    {
        var t = await Start();
        await Select(t, ScreenTextVerb.CopyText);
        Assert.Equal(["Hello world\nfrom the screen"], t.Clip.Written);
        var line = Assert.Single(t.Hub.Activities);
        Assert.Equal(("Copied text", "5 words"), (line.Title, line.Source));
        Assert.Empty(t.Pages);
        Assert.Equal(CaptureEnd.Finished, t.Layer.Last!.Ended);
        Assert.True(t.Layer.Last.HandedBack);
    }

    [Fact]
    public async Task Copy_text_with_joined_lines_and_one_word()
    {
        var t = await Start(new ScreenTextSettings { JoinLines = true });
        await Select(t, ScreenTextVerb.CopyText);
        Assert.Equal(["Hello world from the screen"], t.Clip.Written);
        t.Ocr.Next = new OcrOutcome(TextReadState.Read, [new OcrLine("Hi", default)], "English");
        await Select(t, ScreenTextVerb.CopyText);
        Assert.Equal("1 word", t.Hub.Activities.Single().Source);
    }

    [Theory]
    [InlineData(TextReadState.NoText, "No text found")]
    [InlineData(TextReadState.NoLanguage, "No OCR language installed")]
    [InlineData(TextReadState.Failed, "Text recognition failed")]
    public async Task Nothing_read_copies_nothing_and_says_why(TextReadState state, string title)
    {
        var t = await Start();
        t.Ocr.Next = new OcrOutcome(state, [], "");
        await Select(t, ScreenTextVerb.CopyText);
        Assert.Empty(t.Clip.Written);
        var line = Assert.Single(t.Hub.Activities);
        Assert.Equal(title, line.Title);
        Assert.True(line.NeedsAttention);
    }

    [Fact]
    public async Task A_refused_write_says_Copy_failed()
    {
        var t = await Start();
        t.Clip.Result = ClipStatus.Busy;
        await Select(t, ScreenTextVerb.CopyText);
        Assert.Equal("Copy failed", t.Hub.Activities.Single().Title);
    }

    [Fact]
    public async Task Extract_text_opens_the_page_and_copies_nothing()
    {
        var t = await Start();
        await Select(t, ScreenTextVerb.ExtractText);
        Assert.Empty(t.Clip.Written);
        Assert.Equal([ScreenTextModule.Id], t.Pages);
        Assert.Equal("Hello world\nfrom the screen", t.Module.Text.Result!.Text);
        Assert.Equal("English", t.Module.Text.Result.Language);
        Assert.Equal(2, t.Module.Text.Result.Lines.Count);
    }

    [Fact]
    public async Task Retry_reads_the_same_region_in_the_language_set_now()
    {
        var t = await Start();
        await Select(t, ScreenTextVerb.ExtractText);
        t.Module.SetLanguage("de-DE");
        t.Ocr.Next = new OcrOutcome(TextReadState.Read, [new OcrLine("Hallo", default)], "Deutsch");
        t.Module.TextRetry!();
        await t.Module.Work;
        Assert.Equal(2, t.Ocr.Calls);
        Assert.Equal("de-DE", t.Ocr.LastLanguage);
        Assert.Equal("Hallo", t.Module.Text.Result!.Text);
        Assert.Equal("de-DE", t.Settings.Value.Language);
    }

    [Fact]
    public async Task Esc_and_a_tiny_drag_read_nothing()
    {
        var t = await Start();
        t.Module.Start(ScreenTextVerb.CopyText);
        t.Layer.Last!.Release(4, 4);
        // The layer sends no key to a feature that closed; a tiny drag ends the capture itself.
        Assert.Equal(CaptureEnd.Cancelled, t.Layer.Last.Ended);
        await t.Module.Work;
        Assert.Equal(0, t.Ocr.Calls);
        Assert.Empty(t.Clip.Written);
        Assert.Empty(t.Hub.Activities);
    }

    [Fact]
    public async Task Esc_after_a_region_is_released_closes_the_capture()
    {
        var t = await Start();
        t.Module.Start(ScreenTextVerb.CopyText);
        Chippi.Island.Tests.WpfTestThread.Run(() => t.Layer.Last!.Press(Key.Escape));
        Assert.Equal(CaptureEnd.Cancelled, t.Layer.Last!.Ended);
        Assert.Equal(0, t.Ocr.Calls);
    }

    [Fact]
    public async Task Cancelling_Reselect_leaves_the_Text_page_as_it_was()
    {
        var t = await Start();
        await Select(t, ScreenTextVerb.ExtractText);
        Assert.False(t.Module.Text.Reading);
        t.Module.Text.Reselect();
        Assert.False(t.Module.Text.Reading);
        t.Layer.Last!.Cancel();
        Assert.False(t.Module.Text.Reading);
        Assert.NotNull(t.Module.Text.Result);
    }

    [Fact]
    public async Task A_code_the_decoder_finds_but_cannot_read_is_Unreadable_and_a_blank_region_is_NotFound()
    {
        var t = await Start();
        t.Qr.Hits = [];
        t.Qr.Damaged = true;
        await Select(t, ScreenTextVerb.ScanQr);
        Assert.Equal(QrScanState.Unreadable, t.Module.Qr.State);
        t.Qr.Damaged = false;
        await Select(t, ScreenTextVerb.ScanQr);
        Assert.Equal(QrScanState.NotFound, t.Module.Qr.State);
    }

    [Fact]
    public async Task A_second_request_replaces_the_first_and_the_older_result_is_dropped()
    {
        var t = await Start();
        var first = new TaskCompletionSource<OcrOutcome>();
        t.Ocr.Hold = first.Task;
        t.Module.Start(ScreenTextVerb.ExtractText);
        t.Layer.Last!.Release(100, 40);
        var firstWork = t.Module.Work;
        t.Ocr.Hold = null;
        t.Ocr.Next = new OcrOutcome(TextReadState.Read, [new OcrLine("second", default)], "English");
        await Select(t, ScreenTextVerb.ExtractText);
        first.SetResult(new OcrOutcome(TextReadState.Read, [new OcrLine("first", default)], "English"));
        await firstWork;
        Assert.Equal("second", t.Module.Text.Result!.Text);
    }

    [Fact]
    public async Task Scan_QR_opens_the_page_with_the_codes_and_never_opens_a_link()
    {
        var t = await Start();
        t.Qr.Hits = [new QrHit("https://example.com/", default), new QrHit("WIFI:T:WPA;S:Net;P:pw;;", default)];
        await Select(t, ScreenTextVerb.ScanQr);
        Assert.Equal([ScreenTextModule.QrTileKey], t.Pages);
        Assert.Equal(QrScanState.Read, t.Module.Qr.State);
        Assert.Equal([QrKind.Link, QrKind.WiFi], t.Module.Qr.Codes.Select(c => c.Kind));
        Assert.Empty(t.Clip.Written);
    }

    [Fact]
    public async Task Scan_QR_gives_each_code_its_picture_from_the_capture()
    {
        var t = await Start();
        t.Qr.Hits = [new QrHit("https://example.com/", new Point(10, 10), new Rect(10, 8, 40, 30)), new QrHit("plain", default)];
        await Select(t, ScreenTextVerb.ScanQr);
        var codes = t.Module.Qr.Codes;
        Assert.Equal((40, 30), (codes[0].Image!.PixelWidth, codes[0].Image!.PixelHeight));
        Assert.Equal((120, 40), (codes[1].Image!.PixelWidth, codes[1].Image!.PixelHeight));
    }

    [Fact]
    public async Task Back_from_the_Text_and_QR_pages_returns_to_Capture()
    {
        var t = await Start();
        foreach (var key in new[] { ScreenTextModule.Id, ScreenTextModule.QrTileKey })
        {
            t.Ui.Tried.Clear();
            Assert.True(t.Ui.Specs.Single(s => s.Key == key).HandleBack!());
            Assert.Equal(["capture"], t.Ui.Tried);
        }
    }

    [Fact]
    public async Task Extract_text_opens_its_page_before_the_capture_screen_closes()
    {
        var t = await Start();
        var gate = new TaskCompletionSource<OcrOutcome>();
        t.Ocr.Hold = gate.Task;
        t.Module.Start(ScreenTextVerb.ExtractText);
        var session = t.Layer.Last!;
        int pagesAtClose = -1;
        session.Closed += _ => pagesAtClose = t.Pages.Count;
        session.Release(120, 40);
        Assert.Null(session.Ended); // still frozen while the text is read
        gate.SetResult(new OcrOutcome(TextReadState.Read, [new OcrLine("hi", default)], "English"));
        await t.Module.Work;
        Assert.Equal(1, pagesAtClose);
        Assert.Equal(CaptureEnd.Finished, session.Ended);
        Assert.False(session.HandedBack);
    }

    [Fact]
    public async Task Escape_while_the_region_is_read_closes_the_screen_and_drops_the_result()
    {
        var t = await Start();
        var gate = new TaskCompletionSource<OcrOutcome>();
        t.Ocr.Hold = gate.Task;
        t.Module.Start(ScreenTextVerb.ExtractText);
        var session = t.Layer.Last!;
        session.Release(120, 40);
        session.Press(Key.Escape);
        Assert.Equal(CaptureEnd.Cancelled, session.Ended);
        gate.SetResult(new OcrOutcome(TextReadState.Read, [new OcrLine("late", default)], "English"));
        await t.Module.Work;
        Assert.Empty(t.Pages);
        Assert.Null(t.Module.Text.Result);
        Assert.Equal(CaptureEnd.Cancelled, session.Ended);
    }

    [Fact]
    public async Task A_new_request_while_the_region_is_read_replaces_it_and_only_the_new_result_opens()
    {
        var t = await Start();
        var gate = new TaskCompletionSource<OcrOutcome>();
        t.Ocr.Hold = gate.Task;
        t.Module.Start(ScreenTextVerb.ExtractText);
        var first = t.Layer.Last!;
        first.Release(120, 40);
        var firstWork = t.Module.Work;
        t.Ocr.Hold = null;
        t.Ocr.Next = new OcrOutcome(TextReadState.Read, [new OcrLine("second", default)], "English");
        t.Module.Start(ScreenTextVerb.ExtractText);
        var second = t.Layer.Last!;
        Assert.NotSame(first, second);
        Assert.Equal(CaptureEnd.Interrupted, first.Ended);
        Assert.False(first.HandedBack);
        second.Release(120, 40);
        await t.Module.Work;
        gate.SetResult(new OcrOutcome(TextReadState.Read, [new OcrLine("first", default)], "English"));
        await firstWork;
        Assert.Equal("second", t.Module.Text.Result!.Text);
        Assert.Single(t.Pages);
        Assert.Equal(CaptureEnd.Finished, second.Ended);
        Assert.Equal(CaptureEnd.Interrupted, first.Ended);
    }

    [Fact]
    public async Task Scan_QR_opens_its_page_before_the_capture_screen_closes()
    {
        var t = await Start();
        t.Module.Start(ScreenTextVerb.ScanQr);
        var session = t.Layer.Last!;
        int pagesAtClose = -1;
        session.Closed += _ => pagesAtClose = t.Pages.Count;
        session.Release(120, 40);
        await t.Module.Work;
        Assert.Equal(1, pagesAtClose);
        Assert.False(session.HandedBack);
    }

    [Fact]
    public async Task Scan_QR_with_no_code_or_a_decoder_error()
    {
        var t = await Start();
        t.Qr.Hits = [];
        await Select(t, ScreenTextVerb.ScanQr);
        Assert.Equal(QrScanState.NotFound, t.Module.Qr.State);
        t.Qr.Throw = true;
        await Select(t, ScreenTextVerb.ScanQr);
        Assert.Equal(QrScanState.Unreadable, t.Module.Qr.State);
        Assert.Equal("QR scan failed", t.Hub.Activities.Single().Title);
    }

    [Fact]
    public async Task The_shortcuts_are_listed_set_or_not_saved_and_released_on_stop()
    {
        var t = await Start();
        Assert.Equal(new[] { ScreenTextModule.ExtractName, ScreenTextModule.CopyName, ScreenTextModule.ScanName }.Order(), t.Keys.Added.Keys.Order());
        var edits = t.Keys.Added[ScreenTextModule.ScanName].Edits;
        Assert.Null(edits.Saved());
        var chord = new KeyShortcut(ShortcutModifiers.Control | ShortcutModifiers.Alt, 0x51);
        Assert.Null(edits.Change(chord));
        Assert.Equal(chord, t.Settings.Value.ScanKeys);
        t.Keys.Added[ScreenTextModule.ScanName].Pressed!();
        Assert.Equal(Chippi.Capture.CaptureMode.Region, t.Layer.Last!.Request.Mode);
        edits.Remove();
        Assert.Null(t.Settings.Value.ScanKeys);
        Assert.Equal(3, t.Ui.Commands!().Count);
        await t.Module.StopAsync(default);
        Assert.Equal(1, t.Layer.Disposed);
        Assert.All(t.Keys.Added.Values, h => Assert.True(h.Disposed));
        Assert.Null(t.Module.Text.Result);
    }

    [Fact]
    public async Task The_settings_model_shows_the_language_and_line_breaks_and_saves_changes()
    {
        var t = await Start(new ScreenTextSettings { Language = "fr-FR" });
        var model = t.Module.Settings;
        Assert.Equal("en-US", model.Language);
        Assert.True(model.KeepLineBreaks);
        int raised = 0;
        model.Changed += () => raised++;
        Assert.True(model.SetKeepLineBreaks(false));
        Assert.True(t.Settings.Value.JoinLines);
        Assert.False(model.KeepLineBreaks);
        Assert.True(model.SetLanguage("en-US"));
        Assert.Equal("en-US", t.Settings.Value.Language);
        Assert.Equal(2, raised);
    }
}
