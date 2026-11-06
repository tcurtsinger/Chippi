// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using Chippi.Capture;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;
using ZXing;
using ZXing.Common;

namespace Chippi.Features.ScreenText;

/// <summary>One recognized line: its text and where it sits in the region, in pixels.</summary>
public sealed record OcrLine(string Text, Rect Box);

/// <summary>What the OCR engine made of a region. <see cref="Language"/> is the one used, by name.</summary>
public sealed record OcrOutcome(TextReadState State, IReadOnlyList<OcrLine> Lines, string Language);

/// <summary>Text recognition behind a small interface so the module's tests use a fake (docs/phase-7/text-and-qr.md, Shared code).</summary>
public interface IOcr
{
    /// <summary>The languages with OCR installed in Windows: tag and name.</summary>
    IReadOnlyList<(string Tag, string Name)> Languages();

    /// <summary>Reads the region. <paramref name="language"/> is a saved tag; one no longer installed falls back to the profile's.</summary>
    Task<OcrOutcome> RecognizeAsync(CapturedImage image, string? language, CancellationToken ct);
}

/// <summary>One decoded code and where it sits in the region.</summary>
public sealed record QrHit(string Text, Point At);

/// <summary>QR decoding behind a small interface.</summary>
public interface IQrDecoder
{
    /// <summary>Every code in the region, top to bottom then left to right; empty when there is none.</summary>
    IReadOnlyList<QrHit> Decode(CapturedImage image);
}

/// <summary><c>Windows.Media.Ocr</c>: built in, local, standard user.</summary>
public sealed class WindowsOcr : IOcr
{
    public IReadOnlyList<(string Tag, string Name)> Languages() =>
        [.. OcrEngine.AvailableRecognizerLanguages.Select(l => (l.LanguageTag, l.DisplayName))];

    public async Task<OcrOutcome> RecognizeAsync(CapturedImage image, string? language, CancellationToken ct)
    {
        OcrEngine? engine = null;
        if (language is { Length: > 0 } && OcrEngine.AvailableRecognizerLanguages.Any(l => l.LanguageTag == language))
            engine = OcrEngine.TryCreateFromLanguage(new Language(language));
        engine ??= OcrEngine.TryCreateFromUserProfileLanguages();
        if (engine is null)
            return new OcrOutcome(TextReadState.NoLanguage, [], "");
        string used = engine.RecognizerLanguage.DisplayName;
        if (image.Width <= 0 || image.Height <= 0 || image.Width > OcrEngine.MaxImageDimension || image.Height > OcrEngine.MaxImageDimension)
            return new OcrOutcome(TextReadState.Failed, [], used);

        // Under 40 px tall the text is too small for the engine: enlarged 2x, and the boxes brought back.
        int scale = image.Height < 40 && image.Width * 2 <= OcrEngine.MaxImageDimension ? 2 : 1;
        var pixels = scale == 1 ? image.Pixels : Enlarge(image);
        var writer = new DataWriter();
        writer.WriteBytes(pixels);
        using var bitmap = SoftwareBitmap.CreateCopyFromBuffer(writer.DetachBuffer(), BitmapPixelFormat.Bgra8, image.Width * scale, image.Height * scale, BitmapAlphaMode.Premultiplied);
        ct.ThrowIfCancellationRequested();
        var result = await engine.RecognizeAsync(bitmap);
        var lines = new List<OcrLine>();
        foreach (var line in result.Lines)
        {
            if (string.IsNullOrWhiteSpace(line.Text))
                continue;
            double left = double.MaxValue, top = double.MaxValue, right = 0, bottom = 0;
            foreach (var word in line.Words)
            {
                var r = word.BoundingRect;
                left = Math.Min(left, r.X);
                top = Math.Min(top, r.Y);
                right = Math.Max(right, r.X + r.Width);
                bottom = Math.Max(bottom, r.Y + r.Height);
            }
            var box = left > right ? Rect.Empty : new Rect(left / scale, top / scale, (right - left) / scale, (bottom - top) / scale);
            lines.Add(new OcrLine(line.Text, box));
        }
        return new OcrOutcome(lines.Count == 0 ? TextReadState.NoText : TextReadState.Read, lines, used);
    }

    private static byte[] Enlarge(CapturedImage image)
    {
        int w = image.Width, h = image.Height;
        var big = new byte[(long)w * 2 * h * 2 * 4];
        for (int y = 0; y < h * 2; y++)
            for (int x = 0; x < w * 2; x++)
                System.Buffer.BlockCopy(image.Pixels, ((y / 2) * w + x / 2) * 4, big, (y * w * 2 + x) * 4, 4);
        return big;
    }
}

/// <summary>ZXing.Net over the region's BGRA pixels: QR only, harder search, any 90 degree turn, every code in the region.</summary>
public sealed class ZXingQrDecoder : IQrDecoder
{
    public IReadOnlyList<QrHit> Decode(CapturedImage image)
    {
        if (image.Width <= 0 || image.Height <= 0)
            return [];
        var reader = new BarcodeReaderGeneric
        {
            AutoRotate = true,
            Options = new DecodingOptions { TryHarder = true, PossibleFormats = [BarcodeFormat.QR_CODE] },
        };
        var source = new RGBLuminanceSource(image.Pixels, image.Width, image.Height, RGBLuminanceSource.BitmapFormat.BGRA32);
        var found = reader.DecodeMultiple(source) ?? [];
        return [.. found
            .Where(r => !string.IsNullOrEmpty(r.Text))
            .Select(r => new QrHit(r.Text, At(r)))
            .OrderBy(h => Math.Round(h.At.Y / 20)).ThenBy(h => h.At.X)];
    }

    private static Point At(Result result)
    {
        var points = result.ResultPoints;
        if (points is null || points.Length == 0)
            return default;
        return new Point(points.Min(p => p.X), points.Min(p => p.Y));
    }
}
