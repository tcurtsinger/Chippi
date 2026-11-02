// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Chippi.Features.CleaningMode.Screens;
using Chippi.Island.Tests;

namespace Chippi.Features.CleaningMode.Tests;

/// <summary>
/// Renders the main screen's black off screen at 1920 × 1080, as the Phase 5.5 mockup's frames show it: locked, two presses,
/// and unlocked. Runs only when CHIPPI_ISLAND_SHOTS names an output folder.
/// </summary>
public sealed class CleaningShots
{
    [Fact]
    public void Render()
    {
        if (Environment.GetEnvironmentVariable("CHIPPI_ISLAND_SHOTS") is not { Length: > 0 } folder)
            return;
        Directory.CreateDirectory(folder);
        WpfTestThread.Run(() =>
        {
            Shoot(face => { }, Path.Combine(folder, "cleaning-locked.png"));
            Shoot(face => { face.Count(1, squish: false); face.Count(2, squish: false); }, Path.Combine(folder, "cleaning-two.png"));
            Shoot(face => face.Celebrate(), Path.Combine(folder, "cleaning-unlocked.png"));
        });
    }

    private static void Shoot(Action<CleaningFace> arrange, string path)
    {
        var face = new CleaningFace();
        var screen = new Border { Width = 1920, Height = 1080, Background = Brushes.Black, Child = face };
        screen.Measure(new Size(1920, 1080));
        screen.Arrange(new Rect(0, 0, 1920, 1080));
        arrange(face);
        // Let the rise and the celebration's face land.
        var until = DateTime.UtcNow + TimeSpan.FromMilliseconds(400);
        while (DateTime.UtcNow < until)
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);
        screen.UpdateLayout();
        var bitmap = new RenderTargetBitmap(1920, 1080, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(screen);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(path);
        encoder.Save(file);
    }
}
