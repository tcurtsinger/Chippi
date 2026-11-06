// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Chippi.Capture;
using Chippi.Features.ScreenText;
using Xunit;
using ZXing;
using ZXing.QrCode;

namespace Chippi.Features.ScreenText.Tests;

public sealed class EngineTests(Xunit.Abstractions.ITestOutputHelper output)
{
    // ---- line breaks and words

    [Fact]
    public void Keep_lines_leaves_the_text_as_recognized()
    {
        var (text, starts) = TextJoin.Apply(["one two", "three"], join: false);
        Assert.Equal("one two\nthree", text);
        Assert.Equal([0, 8], starts);
    }

    [Theory]
    [InlineData(new[] { "a line that goes", "on and ends." }, "a line that goes on and ends.")]
    [InlineData(new[] { "ends here.", "A new sentence" }, "ends here.\nA new sentence")]
    [InlineData(new[] { "intro", "• first", "• second" }, "intro\n• first\n• second")]
    [InlineData(new[] { "intro", "1. first", "2) second" }, "intro\n1. first\n2) second")]
    [InlineData(new[] { "intro", "  indented" }, "intro\n  indented")]
    [InlineData(new[] { "one", "", "two" }, "one\n\ntwo")]
    [InlineData(new[] { "only" }, "only")]
    public void Join_follows_the_rules(string[] lines, string expected) =>
        Assert.Equal(expected, TextJoin.Apply(lines, join: true).Text);

    [Fact]
    public void Join_without_lines_is_empty() => Assert.Equal("", TextJoin.Apply([], join: true).Text);

    [Theory]
    [InlineData("", 0)]
    [InlineData("one", 1)]
    [InlineData("  one  two\nthree, four. ", 4)]
    [InlineData("日本語 テキスト", 2)]
    public void Words_are_whitespace_separated(string text, int count) => Assert.Equal(count, TextJoin.WordCount(text));

    // ---- payload kinds

    [Theory]
    [InlineData("https://example.com/a?b=1", QrKind.Link)]
    [InlineData("mailto:a@b.co", QrKind.Email)]
    [InlineData("tel:+15551234", QrKind.Phone)]
    [InlineData("SMSTO:555:hello", QrKind.Message)]
    [InlineData("geo:48.85,2.35", QrKind.Location)]
    [InlineData("hello world", QrKind.Text)]
    [InlineData("file:///C:/Windows/System32/calc.exe", QrKind.Other)]
    [InlineData("ms-settings:network", QrKind.Other)]
    [InlineData("javascript:alert(1)", QrKind.Other)]
    [InlineData("", QrKind.Text)]
    public void Payload_kinds(string payload, QrKind kind) => Assert.Equal(kind, QrPayload.Parse(payload).Kind);

    [Fact]
    public void Only_http_and_https_get_a_link()
    {
        Assert.Equal(new Uri("https://example.com/"), QrPayload.Parse("https://example.com/").Link);
        Assert.Equal(new Uri("http://example.com/"), QrPayload.Parse("http://example.com/").Link);
        foreach (string unsafeLink in new[] { "file:///C:/x", "javascript:alert(1)", "ms-settings:network", "tel:123", "sms:1", "intent://x#Intent;end", "smb://host/share", "http:", "https://", "http://exa mple.com" })
            Assert.Null(QrPayload.Parse(unsafeLink).Link);
    }

    [Theory]
    [InlineData("https://xn--pple-43d.com/", true)]
    [InlineData("https://аpple.com/", true)]
    [InlineData("https://sub.xn--e1afmkfd.example/", true)]
    [InlineData("https://apple.com/", false)]
    public void Check_the_address_for_lookalike_hosts(string payload, bool check) => Assert.Equal(check, QrPayload.Parse(payload).CheckAddress);

    [Fact]
    public void WiFi_shows_name_security_and_a_hidden_password_with_escapes()
    {
        var wifi = QrPayload.Parse(@"WIFI:T:WPA;S:Cafe\;Guest;P:pa\:ss\\word;;");
        Assert.Equal(QrKind.WiFi, wifi.Kind);
        Assert.Equal(("Network", "Cafe;Guest", false), (wifi.Fields[0].Name, wifi.Fields[0].Value, wifi.Fields[0].Secret));
        Assert.Equal("WPA", wifi.Fields[1].Value);
        Assert.Equal(("Password", @"pa:ss\word", true), (wifi.Fields[2].Name, wifi.Fields[2].Value, wifi.Fields[2].Secret));
        Assert.Equal("None", QrPayload.Parse("WIFI:T:nopass;S:Open;;").Fields[1].Value);
        Assert.DoesNotContain(QrPayload.Parse("WIFI:T:nopass;S:Open;;").Fields, f => f.Secret);
    }

    [Fact]
    public void A_contact_shows_its_lines_as_text()
    {
        var card = QrPayload.Parse("BEGIN:VCARD\nVERSION:3.0\nFN:Ada Lovelace\nTEL;TYPE=CELL:555 0100\nEMAIL:ada@example.com\nEND:VCARD");
        Assert.Equal(QrKind.Contact, card.Kind);
        Assert.Equal(["Name: Ada Lovelace", "Phone: 555 0100", "Email: ada@example.com"], card.Fields.Select(f => $"{f.Name}: {f.Value}"));
    }

    // ---- ZXing over BGRA pixels, from codes the test draws

    private static BitMatrixView Draw(string text, int modulePixels = 6) =>
        new(new QRCodeWriter().encode(text, BarcodeFormat.QR_CODE, 0, 0), modulePixels);

    private sealed class BitMatrixView(ZXing.Common.BitMatrix matrix, int scale)
    {
        public int Size => (matrix.Width + 8) * scale;

        public bool Black(int x, int y)
        {
            int mx = x / scale - 4, my = y / scale - 4;
            return mx >= 0 && my >= 0 && mx < matrix.Width && my < matrix.Height && matrix[mx, my];
        }
    }

    private static CapturedImage Canvas(int width, int height, params (BitMatrixView Code, int X, int Y)[] codes)
    {
        var pixels = new byte[width * height * 4];
        Array.Fill(pixels, (byte)255);
        foreach (var (code, ox, oy) in codes)
            for (int y = 0; y < code.Size; y++)
                for (int x = 0; x < code.Size; x++)
                    if (code.Black(x, y) && ox + x < width && oy + y < height)
                    {
                        int i = ((oy + y) * width + ox + x) * 4;
                        pixels[i] = pixels[i + 1] = pixels[i + 2] = 0;
                    }
        return new CapturedImage(width, height, pixels);
    }

    private static CapturedImage Rotated(CapturedImage image)
    {
        var turned = new byte[image.Pixels.Length];
        for (int y = 0; y < image.Height; y++)
            for (int x = 0; x < image.Width; x++)
                Buffer.BlockCopy(image.Pixels, (y * image.Width + x) * 4, turned, (x * image.Height + (image.Height - 1 - y)) * 4, 4);
        return new CapturedImage(image.Height, image.Width, turned);
    }

    [Fact]
    public void ZXing_reads_one_code_at_several_sizes_and_turns()
    {
        var decoder = new ZXingQrDecoder();
        foreach (int scale in new[] { 3, 6, 10 })
        {
            var code = Draw("https://example.com/hello", scale);
            var image = Canvas(code.Size + 40, code.Size + 40, (code, 20, 20));
            Assert.Equal(["https://example.com/hello"], decoder.Decode(image).Select(h => h.Text));
            Assert.Equal(["https://example.com/hello"], decoder.Decode(Rotated(image)).Select(h => h.Text));
            Assert.Equal(["https://example.com/hello"], decoder.Decode(Rotated(Rotated(image))).Select(h => h.Text));
        }
    }

    [Fact]
    public void ZXing_reads_two_codes_in_position_order_and_none_from_a_blank_region()
    {
        var decoder = new ZXingQrDecoder();
        var a = Draw("first", 5);
        var b = Draw("second", 5);
        var image = Canvas(a.Size * 2 + 120, a.Size * 2 + 40, (b, 20 + a.Size + 60, 20), (a, 20, 20 + a.Size / 2));
        var hits = decoder.Decode(image);
        Assert.Equal(["first", "second"], hits.Select(h => h.Text).Order());
        Assert.Equal(2, hits.Count);
        Assert.Empty(decoder.Decode(Canvas(300, 300)));
        Assert.Empty(decoder.Decode(new CapturedImage(0, 0, [])));
    }

    // ---- Windows OCR over text the test draws; the CI runner may have no language, which the engine says

    [Fact]
    public async Task Windows_OCR_reads_drawn_text_or_says_no_language()
    {
        CapturedImage image = null!;
        Chippi.Island.Tests.WpfTestThread.Run(() => image = DrawText("Quarterly review of the numbers"));
        var ocr = new WindowsOcr();
        var outcome = await ocr.RecognizeAsync(image, null, CancellationToken.None);
        output.WriteLine($"OCR state {outcome.State}, language {outcome.Language}, lines {string.Join(" | ", outcome.Lines.Select(l => l.Text))}");
        if (outcome.State == TextReadState.NoLanguage)
        {
            Assert.Empty(ocr.Languages());
            return;
        }
        Assert.Equal(TextReadState.Read, outcome.State);
        Assert.Contains("Quarterly", string.Join(" ", outcome.Lines.Select(l => l.Text)));
        Assert.All(outcome.Lines, l => Assert.False(l.Box.IsEmpty));
        // Too small to read at 1x is enlarged for the engine, and a blank region has no text.
        var blank = await ocr.RecognizeAsync(new CapturedImage(200, 60, Enumerable.Repeat((byte)255, 200 * 60 * 4).ToArray()), null, CancellationToken.None);
        Assert.Equal(TextReadState.NoText, blank.State);
    }

    private static CapturedImage DrawText(string text)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, 520, 60));
            dc.DrawText(new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 24, Brushes.Black, 1), new Point(12, 12));
        }
        var bitmap = new RenderTargetBitmap(520, 60, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var pixels = new byte[520 * 60 * 4];
        bitmap.CopyPixels(pixels, 520 * 4, 0);
        return new CapturedImage(520, 60, pixels);
    }
}
