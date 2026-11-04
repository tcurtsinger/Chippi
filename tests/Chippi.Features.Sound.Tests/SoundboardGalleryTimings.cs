// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Chippi.Features.Sound.Soundboard.Views;

namespace Chippi.Features.Sound.Tests;

/// <summary>A bounded, opt-in offscreen probe; no windows, native input, or audio endpoints.</summary>
public sealed class SoundboardGalleryTimings
{
    [Fact]
    public Task Measure_open_and_playback_updates() => SoundUiThread.Sta(() =>
    {
        if (Environment.GetEnvironmentVariable("CHIPPI_SOUNDBOARD_TIMINGS") is not { Length: > 0 } output) return;
        var report = new List<string> { "clips,sample,open_render_ms,allocated_bytes,realized_tiles,tick_ms" };
        // One small warmup avoids measuring the framework's first resource/template load.
        Measure(12, -1);
        foreach (int count in new[] { 12, 1000 })
            for (int sample = 0; sample < 3; sample++) Measure(count, sample);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        File.WriteAllLines(output, report);

        void Measure(int count, int sample)
        {
            var controller = SoundboardViewFake.WithClips(count);
            long allocated = GC.GetAllocatedBytesForCurrentThread();
            var timer = Stopwatch.StartNew();
            var page = SoundboardUiTests.Open(controller);
            page.Resources.MergedDictionaries.Insert(0, new ResourceDictionary
            { Source = new Uri("pack://application:,,,/Chippi.UI;component/Themes/Dark.xaml") });
            page.Measure(new Size(496, 296)); page.Arrange(new Rect(0, 0, 496, 296)); page.UpdateLayout();
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Loaded); page.UpdateLayout();
            var bitmap = new RenderTargetBitmap(496, 296, 96, 96, PixelFormats.Pbgra32); bitmap.Render(page);
            timer.Stop();
            double opened = timer.Elapsed.TotalMilliseconds;
            allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
            int tiles = SoundboardUiTests.Descendants(page).OfType<Button>().Count(button => button.Tag is string);
            controller.Playback = new($"clip{count - 1}", true, false, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5));
            controller.Notify();
            timer.Restart();
            // Ten updates represent one second of the real 100 ms playback cadence.
            for (int tick = 0; tick < 10; tick++) controller.Notify();
            timer.Stop();
            if (sample >= 0) report.Add(string.Create(CultureInfo.InvariantCulture,
                $"{count},{sample},{opened:F3},{allocated},{tiles},{timer.Elapsed.TotalMilliseconds / 10:F4}"));
            Assert.Null(PresentationSource.FromVisual(page)); SoundboardUiTests.Close(page);
        }
    });
}
