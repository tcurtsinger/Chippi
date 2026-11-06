// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Globalization;

namespace Chippi.Features.ColorPicker;

/// <summary>A color's written forms (docs/phase-7/color-picker.md, Formats).</summary>
public static class ColorFormats
{
    /// <summary>"#rrggbb" or "rrggbb" as "#RRGGBB", or null when it isn't a color.</summary>
    public static string? Normalize(string? hex)
    {
        if (hex is null) return null;
        string digits = hex.Trim().TrimStart('#');
        if (digits.Length != 6 || !uint.TryParse(digits, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out _)) return null;
        return "#" + digits.ToUpperInvariant();
    }

    /// <summary>One format's text for "#RRGGBB": "HEX", "RGB", "HSL", "HSV" or "CMYK".</summary>
    public static string ValueOf(string hex, string format) =>
        format == "HEX" ? hex : Describe(hex).Formats.FirstOrDefault(f => f.Name == format)?.Value ?? hex;

    /// <summary>The color's HEX and its other forms in the page's order: RGB, HSL, HSV, CMYK. <paramref name="hex"/> is "#RRGGBB".</summary>
    public static PickedColor Describe(string hex)
    {
        uint rgb = uint.Parse(hex.AsSpan(1), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);
        int r = (int)(rgb >> 16 & 0xFF), g = (int)(rgb >> 8 & 0xFF), b = (int)(rgb & 0xFF);
        double rf = r / 255.0, gf = g / 255.0, bf = b / 255.0;
        double max = Math.Max(rf, Math.Max(gf, bf)), min = Math.Min(rf, Math.Min(gf, bf)), delta = max - min;

        double hue = 0;
        if (delta > 0)
        {
            hue = max == rf ? (gf - bf) / delta % 6 : max == gf ? (bf - rf) / delta + 2 : (rf - gf) / delta + 4;
            hue *= 60;
            if (hue < 0) hue += 360;
        }
        double lightness = (max + min) / 2;
        double slSat = delta == 0 ? 0 : delta / (1 - Math.Abs(2 * lightness - 1));
        double svSat = max == 0 ? 0 : delta / max;
        double k = 1 - max;
        double c = k >= 1 ? 0 : (1 - rf - k) / (1 - k), m = k >= 1 ? 0 : (1 - gf - k) / (1 - k), y = k >= 1 ? 0 : (1 - bf - k) / (1 - k);

        static string P(double v) => Math.Round(v * 100, MidpointRounding.AwayFromZero).ToString(CultureInfo.InvariantCulture) + "%";
        string h = (Math.Round(hue, MidpointRounding.AwayFromZero) % 360).ToString(CultureInfo.InvariantCulture);
        return new PickedColor(hex,
        [
            new ColorFormat("RGB", FormattableString.Invariant($"rgb({r}, {g}, {b})")),
            new ColorFormat("HSL", $"hsl({h}, {P(slSat)}, {P(lightness)})"),
            new ColorFormat("HSV", $"hsv({h}, {P(svSat)}, {P(max)})"),
            new ColorFormat("CMYK", $"cmyk({P(c)}, {P(m)}, {P(y)}, {P(k)})"),
        ]);
    }
}
