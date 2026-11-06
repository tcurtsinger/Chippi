// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.Features.ColorPicker;
using Xunit;

namespace Chippi.Features.ColorPicker.Tests;

public sealed class ColorFormatsTests
{
    [Theory]
    [InlineData("#7C5CFF", "rgb(124, 92, 255)", "hsl(252, 100%, 68%)", "hsv(252, 64%, 100%)", "cmyk(51%, 64%, 0%, 0%)")]
    [InlineData("#000000", "rgb(0, 0, 0)", "hsl(0, 0%, 0%)", "hsv(0, 0%, 0%)", "cmyk(0%, 0%, 0%, 100%)")]
    [InlineData("#FFFFFF", "rgb(255, 255, 255)", "hsl(0, 0%, 100%)", "hsv(0, 0%, 100%)", "cmyk(0%, 0%, 0%, 0%)")]
    [InlineData("#FF0000", "rgb(255, 0, 0)", "hsl(0, 100%, 50%)", "hsv(0, 100%, 100%)", "cmyk(0%, 100%, 100%, 0%)")]
    public void Formats_match_the_plan(string hex, string rgb, string hsl, string hsv, string cmyk)
    {
        var color = ColorFormats.Describe(hex);
        Assert.Equal([rgb, hsl, hsv, cmyk], color.Formats.Select(f => f.Value));
    }

    [Fact]
    public void Normalize_accepts_hex_only()
    {
        Assert.Equal("#7C5CFF", ColorFormats.Normalize("7c5cff"));
        Assert.Null(ColorFormats.Normalize("#12345"));
        Assert.Null(ColorFormats.Normalize("zzzzzz"));
    }
}
