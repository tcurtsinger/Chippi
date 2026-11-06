// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

#if DEBUG
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Chippi.Features.ScreenText;

/// <summary>Invented results for a Debug run with <c>CHIPPI_RESULT_SAMPLES=1</c>, so the pages can be opened and timed before the capture layer lands.</summary>
internal static class Samples
{
    private static readonly string[] DocumentLines =
    [
        "Quarterly review",
        "Revenue grew 12% over the last quarter, led by the",
        "new subscription plans.",
        "Next steps",
        "· Ship the onboarding update by Friday",
        "· Review pricing with the team",
        "· Draft the launch email for the new plans",
    ];

    public static TextResult Document()
    {
        var visual = new DrawingVisual();
        var boxes = new List<Rect>();
        var face = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        var bold = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, 640, 420));
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0xF3, 0xF3, 0xF3)), null, new Rect(0, 0, 640, 36));
            double y = 60;
            foreach (string line in DocumentLines)
            {
                bool heading = line is "Quarterly review" or "Next steps";
                if (line == "Next steps") y += 18;
                var text = new FormattedText(line, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, heading ? bold : face, heading ? 24 : 18,
                    new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A)), 1);
                dc.DrawText(text, new Point(32, y));
                boxes.Add(new Rect(28, y - 2, text.Width + 8, text.Height + 4));
                y += text.Height + 12;
            }
        }
        var bitmap = new RenderTargetBitmap(640, 420, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        var lines = new List<TextLine>();
        int at = 0;
        for (int i = 0; i < DocumentLines.Length; i++)
        {
            lines.Add(new TextLine(at, DocumentLines[i].Length, boxes[i]));
            at += DocumentLines[i].Length + 1;
        }
        return new TextResult(bitmap, string.Join("\n", DocumentLines), lines, "English");
    }

    public static QrCodeResult Link() =>
        new(QrKind.Link, "https://chippi.example/docs?ref=qr", Code(1)) { Link = new Uri("https://chippi.example/docs?ref=qr") };

    public static QrCodeResult WiFi() =>
        new(QrKind.WiFi, "WIFI:T:WPA;S:Studio;P:correct horse;;", Code(2))
        {
            Fields = [new("Name", "Studio"), new("Security", "WPA"), new("Password", "correct horse", Secret: true)],
        };

    public static QrCodeResult Plain() => new(QrKind.Text, "Pick up the parcel at 5", Code(3));

    private static BitmapSource Code(int seed)
    {
        const int Modules = 25, Quiet = 4, Px = 6, Side = (Modules + 2 * Quiet) * Px;
        var random = new Random(seed);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, Side, Side));
            static bool Finder(int x, int y, int ox, int oy)
            {
                int dx = x - ox, dy = y - oy;
                if (dx is < 0 or > 6 || dy is < 0 or > 6) return false;
                return dx is 0 or 6 || dy is 0 or 6 || (dx is >= 2 and <= 4 && dy is >= 2 and <= 4);
            }
            for (int y = 0; y < Modules; y++)
                for (int x = 0; x < Modules; x++)
                {
                    bool inFinder = (x < 8 && y < 8) || (x >= Modules - 8 && y < 8) || (x < 8 && y >= Modules - 8);
                    if (inFinder ? Finder(x, y, 0, 0) || Finder(x, y, Modules - 7, 0) || Finder(x, y, 0, Modules - 7) : random.Next(2) == 0)
                        dc.DrawRectangle(Brushes.Black, null, new Rect((Quiet + x) * Px, (Quiet + y) * Px, Px, Px));
                }
        }
        var bitmap = new RenderTargetBitmap(Side, Side, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }
}
#endif
