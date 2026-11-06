// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Diagnostics;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Chippi.Capture;
using Chippi.Core.Clipboard;
using Chippi.Core.Island;
using Chippi.Core.Modules;
using Chippi.Core.Shortcuts;
using Chippi.UI.Capture;
using Chippi.Features.ScreenText.Views;
using Chippi.UI.Controls;

namespace Chippi.Features.ScreenText;

/// <summary>
/// Text and QR from screen (docs/phase-7/text-and-qr.md): the Text and QR code pages' models. Reading the selection joins it
/// on the capture layer, which calls <see cref="ShowText"/> and <see cref="ShowCodes"/> with what it read. Nothing here is
/// logged, saved or kept after the page closes.
/// </summary>
/// <summary>The OCR language and line breaks, and the three shortcuts, in the module's settings. Nothing read is kept.</summary>
public sealed record ScreenTextSettings
{
    /// <summary>A language tag with OCR installed ("en-US"); null or a missing one falls back to the profile's.</summary>
    public string? Language { get; init; }

    /// <summary>Join lines into paragraphs; false keeps the lines as recognized.</summary>
    public bool JoinLines { get; init; }

    public KeyShortcut? CopyKeys { get; init; }

    public KeyShortcut? ExtractKeys { get; init; }

    public KeyShortcut? ScanKeys { get; init; }
}

/// <summary>What a starter does with the region.</summary>
public enum ScreenTextVerb
{
    CopyText,
    ExtractText,
    ScanQr,
}

public sealed class ScreenTextModule : IFeatureModule
{
    public const string Id = "screen-text";
    public const string QrTileKey = "screen-qr";

    private readonly object _gate = new();
    private ModuleContext? _context;
    private TextResultView? _textView;
    private QrResultView? _qrView;
    private readonly ICaptureLayer? _capture;
    private readonly IOcr _ocr;
    private readonly IQrDecoder _decoder;
    private ScreenTextSettings _settings = new();
    private IDisposable? _use;
    private readonly List<IHotkey> _keys = [];
    private CapturedImage? _lastImage;
    private long _generation;

    public const string CopyName = "Copy text from screen", ExtractName = "Extract text", ScanName = "Scan QR";
    private const string FeedbackKey = "screen-text.result";

    public ScreenTextModule(ICaptureLayer? capture = null, IOcr? ocr = null, IQrDecoder? decoder = null)
    {
        _capture = capture;
        _ocr = ocr ?? new WindowsOcr();
        _decoder = decoder ?? new ZXingQrDecoder();
        Text = new TextPage(this);
        Qr = new QrPage(this);
    }

    public FeatureDescriptor Descriptor { get; } = new(Id, "Text and QR from screen", "Copies text or reads a QR code from anything on screen", FeatureGroup.Capture)
    {
        Icon = nameof(LucideIcons.ScanText),
        Presets = PresetMembership.PowerUser,
    };

    public TextPage Text { get; }

    public QrPage Qr { get; }

    public Task StartAsync(ModuleContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        lock (_gate)
        {
            _context = context;
            _settings = context.Settings.Get<ScreenTextSettings>();
        }
        if (_capture is not null)
        {
            _use = _capture.Use();
            _keys.Add(AddShortcut(context, CopyName, () => _settings.CopyKeys, k => _settings = _settings with { CopyKeys = k }, ScreenTextVerb.CopyText));
            _keys.Add(AddShortcut(context, ExtractName, () => _settings.ExtractKeys, k => _settings = _settings with { ExtractKeys = k }, ScreenTextVerb.ExtractText));
            _keys.Add(AddShortcut(context, ScanName, () => _settings.ScanKeys, k => _settings = _settings with { ScanKeys = k }, ScreenTextVerb.ScanQr));
            TextReselect = () => Start(ScreenTextVerb.ExtractText, CaptureDoor.Island);
            TextRetry = Retry;
            QrReselect = () => Start(ScreenTextVerb.ScanQr, CaptureDoor.Island);
            context.Ui.AddCommands(() =>
            [
                new ChippiCommand("screen-text/copy", CopyName, nameof(LucideIcons.ScanText), () => { Start(ScreenTextVerb.CopyText, CaptureDoor.Island); return Task.CompletedTask; }) { Words = "ocr text copy capture screenshot" },
                new ChippiCommand("screen-text/extract", ExtractName, nameof(LucideIcons.ScanText), () => { Start(ScreenTextVerb.ExtractText, CaptureDoor.Island); return Task.CompletedTask; }) { Words = "ocr text extract read capture" },
                new ChippiCommand("screen-text/scan", ScanName, nameof(LucideIcons.QrCode), () => { Start(ScreenTextVerb.ScanQr, CaptureDoor.Island); return Task.CompletedTask; }) { Words = "qr code scan barcode read" },
            ]);
        }
        // Stand-ins until the Capture host takes its pages: each result page has a tile of its own.
        context.Ui.AddIslandTile(new IslandTileSpec(Id, "Text", nameof(LucideIcons.ScanText), Order: 78, SectionOrder: 0, () => TextView())
            { CreateHeader = () => TextView().Header });
        context.Ui.AddIslandTile(new IslandTileSpec(QrTileKey, "QR code", nameof(LucideIcons.QrCode), Order: 79, SectionOrder: 0, () => QrView())
            { CreateHeader = () => QrView().Header });
#if DEBUG
        if (Environment.GetEnvironmentVariable("CHIPPI_RESULT_SAMPLES") == "1")
        {
            ShowText(Samples.Document());
            ShowCodes([Samples.Link(), Samples.WiFi(), Samples.Plain()], QrScanState.Read);
        }
#endif
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct)
    {
        foreach (var key in _keys) key.Dispose();
        _keys.Clear();
        _use?.Dispose();
        _use = null;
        Interlocked.Increment(ref _generation);
        _lastImage = null;
        TextReselect = TextRetry = QrReselect = null;
        lock (_gate) _context = null;
        // What was read doesn't outlive the feature.
        Text.Clear();
        Qr.Clear();
        return Task.CompletedTask;
    }

    public Task UninstallAsync(CancellationToken ct) => Task.CompletedTask;

    private TextResultView TextView() => _textView ??= new TextResultView(Text);

    private QrResultView QrView() => _qrView ??= new QrResultView(Qr);

    /// <summary>Puts what Extract text read on the Text page.</summary>
    public void ShowText(TextResult result) => Text.Set(result);

    /// <summary>Puts what Scan QR decoded on the QR code page.</summary>
    public void ShowCodes(IReadOnlyList<QrCodeResult> codes, QrScanState state) => Qr.Set(codes, state);

    /// <summary>Selector again for the Text page. Null until the capture layer joins.</summary>
    public Action? TextReselect { get; set; }

    /// <summary>Reads the same region once more. Null until the capture layer joins.</summary>
    public Action? TextRetry { get; set; }

    /// <summary>Selector again for the QR code page. Null until the capture layer joins.</summary>
    public Action? QrReselect { get; set; }

    /// <summary>The languages with OCR installed in Windows, for Settings; empty when there is none.</summary>
    public IReadOnlyList<(string Tag, string Name)> Languages() => _ocr.Languages();

    public string? Language => _settings.Language;

    public bool JoinLines => _settings.JoinLines;

    public void SetLanguage(string? tag) => Update(_settings with { Language = tag });

    public void SetJoinLines(bool join) => Update(_settings with { JoinLines = join });

    private void Update(ScreenTextSettings settings)
    {
        _settings = settings;
        _context?.Settings.Save(settings);
    }

    private IHotkey AddShortcut(ModuleContext context, string name, Func<KeyShortcut?> saved, Action<KeyShortcut?> set, ScreenTextVerb verb)
    {
        IHotkey? hotkey = null;
        string? Change(KeyShortcut k)
        {
            if (!k.IsBindable) return "Shortcut unavailable";
            if (!hotkey!.Register(k, () => Start(verb))) return hotkey.Error ?? ShortcutService.InUse;
            set(k); Update(_settings);
            return null;
        }
        hotkey = context.Shortcuts.Add(Id, name, new ShortcutEdits(saved, Change, () => { hotkey!.Unregister(); set(null); Update(_settings); })
            { Icon = verb == ScreenTextVerb.ScanQr ? nameof(LucideIcons.QrCode) : nameof(LucideIcons.ScanText) });
        if (saved() is { IsBindable: true } keys)
            hotkey.Register(keys, () => Start(verb));
        return hotkey;
    }

    /// <summary>The task of the last read, for tests to wait on.</summary>
    internal Task Work { get; private set; } = Task.CompletedTask;

    /// <summary>
    /// A starter: the screen freezes, a region is dragged, and its pixels are read off the UI thread. A second request while
    /// one reads replaces it; the older result is dropped.
    /// </summary>
    public void Start(ScreenTextVerb verb, CaptureDoor door = CaptureDoor.Shortcut)
    {
        if (_capture is null || _context is null) return;
        var session = _capture.Start(new CaptureRequest(Chippi.Capture.CaptureMode.Region) { Door = door });
        if (session is null) return;
        bool taken = false;
        session.Selected += selection =>
        {
            // A sub-8-pixel drag is no selection; the first real one is the region.
            if (taken || selection.Rect.Width < 8 || selection.Rect.Height < 8) return;
            taken = true;
            var image = selection.Image();
            session.Close(CaptureEnd.Finished, handBack: verb == ScreenTextVerb.CopyText);
            Work = verb == ScreenTextVerb.ScanQr ? ScanAsync(image) : ReadAsync(image, copy: verb == ScreenTextVerb.CopyText);
        };
    }

    private void Retry()
    {
        if (_lastImage is { } image) Work = ReadAsync(image, copy: false);
    }

    private async Task ReadAsync(CapturedImage image, bool copy)
    {
        long generation = Interlocked.Increment(ref _generation);
        _lastImage = image;
        string? language = _settings.Language;
        bool join = _settings.JoinLines;
        OcrOutcome outcome;
        try { outcome = await Task.Run(() => _ocr.RecognizeAsync(image, language, CancellationToken.None)); }
        catch (Exception ex) when (ex is not OperationCanceledException) { outcome = new OcrOutcome(TextReadState.Failed, [], ""); }
        if (generation != Interlocked.Read(ref _generation) || _context is not { } context) return;

        var (text, starts) = TextJoin.Apply([.. outcome.Lines.Select(l => l.Text)], join);
        var lines = outcome.Lines.Select((l, i) => new TextLine(starts[i], l.Text.Length, l.Box)).ToList();
        var result = new TextResult(Bitmap(image), text, lines, outcome.Language) { State = outcome.State };
        if (!copy)
        {
            ShowText(result);
            context.OpenIslandPage(Id);
            return;
        }
        switch (outcome.State)
        {
            case TextReadState.Read:
                int words = TextJoin.WordCount(text);
                if (Write(text))
                    Feedback(context, "Copied text", words == 1 ? "1 word" : $"{words} words", false);
                else
                    Feedback(context, "Copy failed", "Text", true);
                break;
            case TextReadState.NoText: Feedback(context, "No text found", "Text", true); break;
            case TextReadState.NoLanguage: Feedback(context, "No OCR language installed", "Text", true); break;
            default: Feedback(context, "Text recognition failed", "Text", true); break;
        }
    }

    private async Task ScanAsync(CapturedImage image)
    {
        long generation = Interlocked.Increment(ref _generation);
        IReadOnlyList<QrHit> hits = [];
        bool failed = false;
        try { hits = await Task.Run(() => _decoder.Decode(image)); }
        catch (Exception ex) when (ex is not OperationCanceledException) { failed = true; }
        if (generation != Interlocked.Read(ref _generation) || _context is not { } context) return;

        var codes = hits.Select(h => QrPayload.Parse(h.Text)).ToList();
        ShowCodes(codes, failed ? QrScanState.Unreadable : codes.Count == 0 ? QrScanState.NotFound : QrScanState.Read);
        if (failed) Feedback(context, "QR scan failed", "QR code", true);
        context.OpenIslandPage(QrTileKey);
    }

    private static BitmapSource Bitmap(CapturedImage image)
    {
        if (image.Width <= 0 || image.Height <= 0)
            return BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, new byte[4], 4);
        var bitmap = BitmapSource.Create(image.Width, image.Height, 96, 96, PixelFormats.Bgra32, null, image.Pixels, image.Stride);
        bitmap.Freeze();
        return bitmap;
    }

    private static void Feedback(ModuleContext context, string title, string source, bool attention) =>
        context.Island.Publish(new IslandActivity(FeedbackKey, IslandActivityKind.Status, title, source) { IsFeedback = true, NeedsAttention = attention });

    // The page needs the answer now, so the hub's write is waited for (it returns in milliseconds).
    private bool Write(string text)
    {
        ModuleContext? context;
        lock (_gate) context = _context;
        if (context?.Clipboard is not { } clipboard) return false;
        try { return clipboard.WriteAsync(ClipContent.FromText(text)).GetAwaiter().GetResult() == ClipStatus.Ok; }
        catch (Exception ex) when (ex is not OperationCanceledException) { return false; }
    }

    public sealed class TextPage(ScreenTextModule owner) : ITextPageModel
    {
        private readonly object _gate = new();
        private TextResult? _result;
        private bool _reading;

        public TextResult? Result { get { lock (_gate) return _result; } }

        public bool Reading { get { lock (_gate) return _reading; } }

        public event Action? Changed;

        public bool Copy(string text) => owner.Write(text);

        public void Reselect() { SetReading(owner.TextReselect is not null); owner.TextReselect?.Invoke(); }

        public void Retry() { SetReading(owner.TextRetry is not null); owner.TextRetry?.Invoke(); }

        internal void Set(TextResult result)
        {
            lock (_gate) { _result = result; _reading = false; }
            Changed?.Invoke();
        }

        internal void Clear()
        {
            lock (_gate) { _result = null; _reading = false; }
            Changed?.Invoke();
        }

        private void SetReading(bool reading)
        {
            lock (_gate) _reading = reading;
            Changed?.Invoke();
        }
    }

    public sealed class QrPage(ScreenTextModule owner) : IQrPageModel
    {
        private readonly object _gate = new();
        private IReadOnlyList<QrCodeResult> _codes = [];
        private QrScanState _state = QrScanState.NotFound;

        public IReadOnlyList<QrCodeResult> Codes { get { lock (_gate) return _codes; } }

        public QrScanState State { get { lock (_gate) return _state; } }

        public event Action? Changed;

        public bool Copy(string payload) => owner.Write(payload);

        /// <summary>Only an http or https address opens, once, in the default browser.</summary>
        public void Open(Uri link)
        {
            if (!link.IsAbsoluteUri || link.Scheme is not ("http" or "https")) return;
            try { Process.Start(new ProcessStartInfo(link.AbsoluteUri) { UseShellExecute = true })?.Dispose(); }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { }
        }

        public void Reselect() => owner.QrReselect?.Invoke();

        internal void Set(IReadOnlyList<QrCodeResult> codes, QrScanState state)
        {
            lock (_gate) { _codes = codes; _state = state; }
            Changed?.Invoke();
        }

        internal void Clear() => Set([], QrScanState.NotFound);
    }
}
