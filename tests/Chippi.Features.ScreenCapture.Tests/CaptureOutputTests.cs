// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Chippi.Features.ScreenCapture.Tests;

public sealed class CaptureOutputTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Retry_only_repeats_the_failed_output_and_updates_one_recent_entry(bool copyFails)
    {
        var pixels = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, new byte[4], 4);
        pixels.Freeze();
        int copies = 0, saves = 0, entries = 0;
        bool fail = true;
        string? retainedFile = null;
        var output = new CaptureOutput(pixels,
            _ => { copies++; return Task.FromResult(!(copyFails && fail)); },
            _ => { saves++; if (!copyFails && fail) throw new IOException(); return Task.FromResult("fixture.png"); },
            (_, file) => { entries = 1; retainedFile = file; return Task.CompletedTask; });
        var first = await output.FinishAsync(EnterAction.CopyAndSave);
        Assert.Equal(copyFails ? "Saved. Copy failed" : "Copied. Save failed", first.Title);
        Assert.Equal(1, entries);
        fail = false;
        var retried = await output.RetryAsync();
        Assert.Equal("Copied and saved", retried.Title);
        Assert.Equal(copyFails ? 2 : 1, copies);
        Assert.Equal(copyFails ? 1 : 2, saves);
        Assert.Equal("fixture.png", retainedFile);
        Assert.Equal(1, entries);
    }
}
