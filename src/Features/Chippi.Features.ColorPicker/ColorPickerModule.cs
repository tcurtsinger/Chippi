// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.Capture;
using Chippi.Core.Clipboard;
using Chippi.Core.Island;
using Chippi.Core.Modules;
using Chippi.Core.Shortcuts;
using Chippi.UI.Capture;
using Chippi.Features.ColorPicker.Views;
using Chippi.UI.Controls;

namespace Chippi.Features.ColorPicker;

/// <summary>The palette, kept in the module's settings (docs/phase-7/color-picker.md, Storage).</summary>
public sealed record ColorPickerSettings
{
    /// <summary>Up to 10 colors as "#RRGGBB", newest first.</summary>
    public string[] Recent { get; init; } = [];

    /// <summary>What Pick color copies: HEX, RGB, HSL, HSV or CMYK.</summary>
    public string CopyFormat { get; init; } = "HEX";

    public KeyShortcut? PickKeys { get; init; }

    public KeyShortcut? ReviewKeys { get; init; }
}

/// <summary>
/// Color picker (docs/phase-7/color-picker.md): the Color page's model, the formats and the recent palette. Sampling the
/// screen joins it on the capture layer, which calls <see cref="Present"/> with the color it read.
/// </summary>
public sealed class ColorPickerModule : IFeatureModule, IColorPageModel
{
    public const string Id = "color-picker";
    public const int MaxRecent = 10;

    private readonly object _gate = new();
    private ModuleContext? _context;
    private ColorResultView? _view;
    private PickedColor? _color;
    private string? _copiedAs;
    private List<string> _recent = [];
    private readonly ICaptureLayer? _capture;
    private ColorPickerSettings _settings = new();
    private IDisposable? _use;
    private IHotkey? _pickKey, _reviewKey;

    public ColorPickerModule(ICaptureLayer? capture = null) => _capture = capture;

    public FeatureDescriptor Descriptor { get; } = new(Id, "Color picker", "Picks a color from anywhere on screen and copies it", FeatureGroup.Capture)
    {
        Icon = nameof(LucideIcons.Pipette),
        Presets = PresetMembership.PowerUser,
    };

    public PickedColor? Color { get { lock (_gate) return _color; } }

    public string? CopiedAs { get { lock (_gate) return _copiedAs; } }

    public IReadOnlyList<string> Recent { get { lock (_gate) return [.. _recent]; } }

    public event Action? Changed;

    /// <summary>Starts Pick and review again. Null until the capture layer joins; then <see cref="PickAgain"/> does nothing.</summary>
    public Action? PickRequested { get; set; }

    /// <summary>Why a shortcut didn't register ("Shortcut in use"), by its name; empty when none failed.</summary>
    public IReadOnlyDictionary<string, string> ShortcutErrors => _errors;
    private readonly Dictionary<string, string> _errors = [];

    public const string PickName = "Pick color", ReviewName = "Pick and review color";

    public Task StartAsync(ModuleContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        lock (_gate)
        {
            _context = context;
            _settings = context.Settings.Get<ColorPickerSettings>();
            _recent = [.. Validated(_settings.Recent)];
            if (!Array.Exists(FormatNames, n => n == _settings.CopyFormat)) _settings = _settings with { CopyFormat = "HEX" };
        }
        if (_capture is not null)
        {
            _use = _capture.Use();
            _pickKey = AddShortcut(context, PickName, () => _settings.PickKeys, k => _settings = _settings with { PickKeys = k }, () => Pick(review: false));
            _reviewKey = AddShortcut(context, ReviewName, () => _settings.ReviewKeys, k => _settings = _settings with { ReviewKeys = k }, () => Pick(review: true));
            PickRequested = () => Pick(review: true, CaptureDoor.Island);
            context.Ui.AddCommands(() =>
            [
                new ChippiCommand("color-picker/pick", PickName, nameof(LucideIcons.Pipette), () => { Pick(review: false, CaptureDoor.Island); return Task.CompletedTask; }) { Words = "color colour eyedropper pipette hex" },
                new ChippiCommand("color-picker/review", ReviewName, nameof(LucideIcons.Pipette), () => { Pick(review: true, CaptureDoor.Island); return Task.CompletedTask; }) { Words = "color colour eyedropper pipette review palette" },
            ]);
        }
        // Stand-in until the Capture host takes its pages: the Color page has a tile of its own.
        context.Ui.AddIslandTile(new IslandTileSpec(Id, "Color", nameof(LucideIcons.Pipette), Order: 77, SectionOrder: 0, () => View())
            { CreateHeader = () => View().Header });
#if DEBUG
        if (Environment.GetEnvironmentVariable("CHIPPI_RESULT_SAMPLES") == "1")
            Present("#7C5CFF", copiedAs: "HEX");
#endif
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct)
    {
        _pickKey?.Dispose(); _reviewKey?.Dispose(); _pickKey = _reviewKey = null;
        _use?.Dispose(); _use = null;
        PickRequested = null;
        lock (_gate)
        {
            _context = null;
            _color = null;
            _copiedAs = null;
        }
        Changed?.Invoke();
        return Task.CompletedTask;
    }

    public Task UninstallAsync(CancellationToken ct) => Task.CompletedTask;

    private ColorResultView View() => _view ??= new ColorResultView(this);

    /// <summary>Puts a sampled color on the page and in the palette. <paramref name="copiedAs"/> is the format Pick color copied, or null for Pick and review.</summary>
    public void Present(string hex, string? copiedAs = null)
    {
        if (ColorFormats.Normalize(hex) is not { } normal) return;
        lock (_gate)
        {
            _color = ColorFormats.Describe(normal);
            _copiedAs = copiedAs;
            Remember(normal);
        }
        Changed?.Invoke();
    }

    public bool Copy(ColorFormat format) => Write(format.Value);

    public void Show(string hex)
    {
        if (ColorFormats.Normalize(hex) is not { } normal) return;
        lock (_gate)
        {
            _color = ColorFormats.Describe(normal);
            _copiedAs = null;
        }
        Changed?.Invoke();
    }

    public void ClearRecent()
    {
        lock (_gate) _recent = [];
        Save();
        Changed?.Invoke();
    }

    public static readonly string[] FormatNames = ["HEX", "RGB", "HSL", "HSV", "CMYK"];

    /// <summary>The format Pick color copies, from the settings.</summary>
    public string CopyFormat => _settings.CopyFormat;

    public void SetCopyFormat(string format)
    {
        if (!Array.Exists(FormatNames, n => n == format)) return;
        _settings = _settings with { CopyFormat = format };
        Save();
        Changed?.Invoke();
    }

    private IHotkey AddShortcut(ModuleContext context, string name, Func<KeyShortcut?> saved, Action<KeyShortcut?> set, Action pressed)
    {
        IHotkey? hotkey = null;
        string? Change(KeyShortcut k)
        {
            if (!k.IsBindable) return "Shortcut unavailable";
            if (!hotkey!.Register(k, pressed)) return hotkey.Error ?? ShortcutService.InUse;
            set(k); Save();
            return null;
        }
        hotkey = context.Shortcuts.Add(Id, name, new ShortcutEdits(saved, Change, () => { hotkey!.Unregister(); set(null); Save(); }) { Icon = nameof(LucideIcons.Pipette) });
        if (saved() is { IsBindable: true } keys && !hotkey.Register(keys, pressed))
            _errors[name] = hotkey.Error ?? ShortcutService.InUse;
        return hotkey;
    }

    /// <summary>
    /// Starts Pick color (copies in the Settings format) or Pick and review color (opens the Color page). The screen freezes
    /// undimmed; the sample is read from the frozen image. Esc or a right click cancels and keeps the clipboard.
    /// </summary>
    public void Pick(bool review, CaptureDoor door = CaptureDoor.Shortcut)
    {
        if (_capture is null || _context is not { } context) return;
        var session = _capture.Start(new CaptureRequest(CaptureMode.PickPoint) { Door = door, Dim = false });
        if (session is null) return;
        session.Picked += (_, argb) =>
        {
            string hex = ColorFormats.Normalize($"{argb & 0xFFFFFF:X6}")!;
            session.Close(CaptureEnd.Finished, handBack: !review);
            if (review)
            {
                Present(hex);
                context.OpenIslandPage(Id);
                return;
            }
            string format = _settings.CopyFormat;
            string value = ColorFormats.ValueOf(hex, format);
            bool copied = Write(value);
            Present(hex, copied ? format : null);
            Feedback(context, copied ? new IslandActivity(FeedbackKey, IslandActivityKind.Status, value, $"Copied as {format}") { IsFeedback = true, Swatch = hex }
                : new IslandActivity(FeedbackKey, IslandActivityKind.Status, "Copy failed", "Color") { IsFeedback = true, NeedsAttention = true, Icon = nameof(LucideIcons.TriangleAlert) });
        };
    }

    private const string FeedbackKey = "color-picker.pick";

    private static void Feedback(ModuleContext context, IslandActivity line) => context.Island.Publish(line);

    public void PickAgain() => PickRequested?.Invoke();

    private void Remember(string hex)
    {
        _recent.Remove(hex);
        _recent.Insert(0, hex);
        if (_recent.Count > MaxRecent) _recent.RemoveRange(MaxRecent, _recent.Count - MaxRecent);
        Save();
    }

    private void Save()
    {
        ModuleContext? context;
        ColorPickerSettings settings;
        lock (_gate) { context = _context; settings = _settings with { Recent = [.. _recent] }; }
        context?.Settings.Save(settings);
    }

    // The clipboard hub takes the write; the page needs the answer now, so the write is waited for (it returns in ms).
    private bool Write(string text)
    {
        ModuleContext? context;
        lock (_gate) context = _context;
        if (context?.Clipboard is not { } clipboard) return false;
        try { return clipboard.WriteAsync(ClipContent.FromText(text)).GetAwaiter().GetResult() == ClipStatus.Ok; }
        catch (Exception ex) when (ex is not OperationCanceledException) { return false; }
    }

    private static IEnumerable<string> Validated(IEnumerable<string>? saved) =>
        (saved ?? []).Select(ColorFormats.Normalize).OfType<string>().Distinct().Take(MaxRecent);
}
