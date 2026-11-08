// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
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
        Settings = new SettingsModel(this);
    }

    public FeatureDescriptor Descriptor { get; } = new(Id, "Text and QR from screen", "Copies text or reads a QR code from anything on screen", FeatureGroup.Capture)
    {
        Icon = nameof(LucideIcons.ScanText),
        Presets = PresetMembership.PowerUser,
    };

    public TextPage Text { get; }

    public QrPage Qr { get; }

    /// <summary>What Settings › Text and QR shows and changes.</summary>
    public SettingsModel Settings { get; }

    public Task StartAsync(ModuleContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        lock (_gate)
        {
            _context = context;
            _line = new FeedbackLine(context.Island, FeedbackKey, Time, NoticeDuration);
            _settings = context.Settings.Get<ScreenTextSettings>();
        }
        context.Ui.SetSettingsPage(_ => new Views.ScreenTextSettingsPage(Settings));
        context.Shortcuts.Changed += Settings.RaiseChanged;
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
        // The result pages: opened by key, not listed on the home (the Capture tile starts them).
        context.Ui.AddIslandTile(new IslandTileSpec(Id, "Text", nameof(LucideIcons.ScanText), Order: 78, SectionOrder: 0, () => TextView())
            { OnHome = false, CreateHeader = () => TextView().Header, HandleBack = () => BackToCapture(context) });
        context.Ui.AddIslandTile(new IslandTileSpec(QrTileKey, "QR code", nameof(LucideIcons.QrCode), Order: 79, SectionOrder: 0, () => QrView())
            { OnHome = false, CreateHeader = () => QrView().Header, HandleBack = () => BackToCapture(context) });
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
        lock (_gate) if (_context is { } stopping) stopping.Shortcuts.Changed -= Settings.RaiseChanged;
        foreach (var key in _keys) key.Dispose();
        _keys.Clear();
        _use?.Dispose();
        _use = null;
        Interlocked.Increment(ref _generation);
        _lastImage = null;
        TextReselect = TextRetry = QrReselect = null;
        lock (_gate) { _context = null; _line?.Dispose(); _line = null; }
        // What was read doesn't outlive the feature.
        Text.Clear();
        Qr.Clear();
        return Task.CompletedTask;
    }

    public Task UninstallAsync(CancellationToken ct) => Task.CompletedTask;

    // What was read doesn't outlive its page: closing the page clears it, and the region's pixels with it.
    private TextResultView TextView()
    {
        if (_textView is null)
        {
            _textView = new TextResultView(Text);
            _textView.Unloaded += (_, _) => { Text.Clear(); _lastImage = null; };
        }
        return _textView;
    }

    private QrResultView QrView()
    {
        if (_qrView is null)
        {
            _qrView = new QrResultView(Qr);
            _qrView.Unloaded += (_, _) => Qr.Clear();
        }
        return _qrView;
    }

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
        Settings.RaiseChanged();
    }

    /// <summary>Settings › Text and QR's model: the languages Windows has, the saved language and line breaks, and the shortcut rows.</summary>
    public sealed class SettingsModel(ScreenTextModule owner) : IScreenTextSettingsModel
    {
        public IReadOnlyList<OcrLanguage> Languages => [.. owner._ocr.Languages().Select(l => new OcrLanguage(l.Tag, l.Name))];

        public string? Language
        {
            get
            {
                var all = owner._ocr.Languages();
                string? saved = owner._settings.Language;
                return saved is not null && all.Any(l => l.Tag == saved) ? saved : all.Count == 0 ? null : owner._ocr.DefaultLanguage();
            }
        }

        public bool KeepLineBreaks => !owner._settings.JoinLines;

        public string? SettingsError => null;

        public event Action? Changed;

        internal void RaiseChanged() => Changed?.Invoke();

        public (KeyShortcut? Keys, string? Error) Shortcut(string name)
        {
            ModuleContext? context;
            lock (owner._gate) context = owner._context;
            if (context is null) return (null, null);
            bool host = name == ScreenTextShortcuts.OpenCapture;
            foreach (var entry in context.Shortcuts.List)
                if (host ? entry.Name == name && entry.Owner == "capture" : entry.Owner == Id && entry.Name == Map(name))
                    return (entry.Shortcut, entry.Error);
            return (null, null);
        }

        private static string Map(string slot) => slot switch
        {
            ScreenTextShortcuts.ScanQrCode => ScanName,
            _ => slot,
        };

        public bool SetLanguage(string tag) => Write(owner._settings with { Language = tag });

        public bool SetKeepLineBreaks(bool on) => Write(owner._settings with { JoinLines = !on });

        private bool Write(ScreenTextSettings settings)
        {
            var before = owner._settings;
            try { owner.Update(settings); return true; }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                owner._settings = before;
                RaiseChanged();
                return false;
            }
        }

        public void OpenLanguageSettings()
        {
            try { Process.Start(new ProcessStartInfo("ms-settings:regionlanguage") { UseShellExecute = true }); }
            catch (Exception ex) when (ex is Win32Exception or InvalidOperationException) { }
        }

        public void EditShortcut(string name)
        {
            ModuleContext? context;
            lock (owner._gate) context = owner._context;
            if (name == ScreenTextShortcuts.OpenCapture) context?.EditShortcutFor("capture", name); else context?.EditShortcut(Map(name));
        }
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
        // Once a region is released the layer hands every key to the feature, Esc included.
        session.KeyDown += e =>
        {
            if (e.Key != System.Windows.Input.Key.Escape || taken) return;
            e.Handled = true;
            session.Close(CaptureEnd.Cancelled);
        };
        session.Selected += selection =>
        {
            if (taken) return;
            // A sub-8-pixel drag is no selection: the capture ends instead of leaving the frozen screen with nothing to do.
            if (selection.Rect.Width < 8 || selection.Rect.Height < 8)
            {
                taken = true;
                session.Close(CaptureEnd.Cancelled);
                return;
            }
            taken = true;
            var image = selection.Image();
            session.Close(CaptureEnd.Finished, handBack: verb == ScreenTextVerb.CopyText);
            Work = verb == ScreenTextVerb.ScanQr ? ScanAsync(image) : ReadAsync(image, copy: verb == ScreenTextVerb.CopyText, show: verb == ScreenTextVerb.ExtractText);
        };
    }

    /// <summary>
    /// Consumes Screen capture's composed selection without starting another capture. Copy text there copies and opens the
    /// Text page too (Travis's live check, 2026-11-08); Extract text only opens it.
    /// </summary>
    public Task ReadSelectionAsync(CapturedImage image, ScreenTextVerb verb)
    {
        if (_context == null) return Task.CompletedTask;
        return Work = verb == ScreenTextVerb.ScanQr ? ScanAsync(image) : ReadAsync(image, copy: verb == ScreenTextVerb.CopyText, show: true);
    }
    private void Retry()
    {
        if (_lastImage is { } image) Work = ReadAsync(image, copy: false, show: true);
    }

    private async Task ReadAsync(CapturedImage image, bool copy, bool show)
    {
        long generation = Interlocked.Increment(ref _generation);
        _lastImage = image;
        if (show) Text.SetReading(true);
        string? language = _settings.Language;
        bool join = _settings.JoinLines;
        OcrOutcome outcome;
        try { outcome = await Task.Run(() => _ocr.RecognizeAsync(image, language, CancellationToken.None)); }
        catch (Exception ex) when (ex is not OperationCanceledException) { outcome = new OcrOutcome(TextReadState.Failed, [], ""); }
        if (generation != Interlocked.Read(ref _generation) || _context is not { } context) return;

        var (text, starts) = TextJoin.Apply([.. outcome.Lines.Select(l => l.Text)], join);
        var lines = outcome.Lines.Select((l, i) => new TextLine(starts[i], l.Text.Length, l.Box)).ToList();
        var result = new TextResult(Bitmap(image), text, lines, outcome.Language) { State = outcome.State };
        if (show)
        {
            // The page says why nothing was read; a copy still gets its bar line, so a failed write isn't silent.
            if (copy && outcome.State == TextReadState.Read) Copy(context, text);
            ShowText(result);
            ShowPage(context, Id);
            return;
        }
        switch (outcome.State)
        {
            case TextReadState.Read: Copy(context, text); break;
            case TextReadState.NoText: Feedback(context, "No text found", "Text", true); break;
            case TextReadState.NoLanguage: Feedback(context, "No OCR language installed", "Text", true); break;
            default: Feedback(context, "Text recognition failed", "Text", true); break;
        }
    }

    private async Task ScanAsync(CapturedImage image)
    {
        long generation = Interlocked.Increment(ref _generation);
        IReadOnlyList<QrHit> hits = [];
        bool failed = false, damaged = false;
        try
        {
            (hits, damaged) = await Task.Run(() =>
            {
                var found = _decoder.Decode(image);
                return (found, found.Count == 0 && _decoder.LooksLikeCode(image));
            });
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { failed = true; }
        if (generation != Interlocked.Read(ref _generation) || _context is not { } context) return;

        var codes = hits.Select(h => QrPayload.Parse(h.Text, Bitmap(image, h.Box))).ToList();
        ShowCodes(codes, failed || damaged ? QrScanState.Unreadable : codes.Count == 0 ? QrScanState.NotFound : QrScanState.Read);
        if (failed) Feedback(context, "QR scan failed", "QR code", true);
        ShowPage(context, QrTileKey);
    }

    // The capture's overlay still covers the screen when a result asks for its page, so the island may be hidden as for a
    // fullscreen app and refuse; then it's shown over it (Settings opens only when the island is off).
    // The result pages are reached from the Capture panel, so Back returns there; the home only when it isn't on the island.
    private static bool BackToCapture(ModuleContext context) => context.TryOpenIslandPage("capture");

    private static void ShowPage(ModuleContext context, string key)
    {
        if (!context.TryOpenIslandPage(key) && !context.SummonIslandPage(key))
            context.OpenIslandPage(key);
    }

    // The code as it was in the capture: its box, or the whole region when the decoder gave none.
    private static BitmapSource Bitmap(CapturedImage image, Rect box)
    {
        int x = (int)box.X, y = (int)box.Y, w = Math.Min((int)Math.Ceiling(box.Width), image.Width - x), h = Math.Min((int)Math.Ceiling(box.Height), image.Height - y);
        if (box.IsEmpty || w <= 0 || h <= 0 || x < 0 || y < 0) return Bitmap(image);
        var cut = new CroppedBitmap(Bitmap(image), new Int32Rect(x, y, w, h));
        cut.Freeze();
        return cut;
    }

    private static BitmapSource Bitmap(CapturedImage image)
    {
        if (image.Width <= 0 || image.Height <= 0)
            return BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, new byte[4], 4);
        var bitmap = BitmapSource.Create(image.Width, image.Height, 96, 96, PixelFormats.Bgra32, null, image.Pixels, image.Stride);
        bitmap.Freeze();
        return bitmap;
    }

    private void Copy(ModuleContext context, string text)
    {
        int words = TextJoin.WordCount(text);
        if (Write(text))
            Feedback(context, "Copied text", words == 1 ? "1 word" : $"{words} words", false);
        else
            Feedback(context, "Copy failed", "Text", true);
    }

    private FeedbackLine? _line;

    /// <summary>The clock and notice time of the bar line, which clears itself; tests hold both.</summary>
    public TimeProvider Time { get; init; } = TimeProvider.System;
    public TimeSpan NoticeDuration { get; init; } = TimeSpan.FromSeconds(6);

    private void Feedback(ModuleContext context, string title, string source, bool attention) =>
        _line?.Show(new IslandActivity(FeedbackKey, IslandActivityKind.Status, title, source) { IsFeedback = true, NeedsAttention = attention });

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

        // Reading starts when a region is taken, so Esc on the selector leaves the page as it was.
        public void Reselect() => owner.TextReselect?.Invoke();

        public void Retry() => owner.TextRetry?.Invoke();

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

        internal void SetReading(bool reading)
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
