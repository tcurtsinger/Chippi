// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Chippi.Audio;
using Chippi.Features.Sound.Processing;
using Chippi.Features.Sound.Volume;
using Chippi.UI.Controls;
using static Chippi.Audio.Tests.FakeDeviceSystem;

namespace Chippi.Features.Sound.Tests;

/// <summary>
/// Renders Settings › Output, Microphone and App volume off screen with fake devices, as the
/// settings-shell shots c1, c2 and c3 show them. Runs only when CHIPPI_ISLAND_SHOTS names an output folder.
/// </summary>
public sealed class SoundSettingsShots
{
    [Fact]
    public async Task Render()
    {
        if (Environment.GetEnvironmentVariable("CHIPPI_ISLAND_SHOTS") is not { Length: > 0 } folder)
            return;
        Directory.CreateDirectory(folder);
        await using var routing = new RoutingFixture();
        routing.System.With(Speaker("Speakers (Realtek USB2.0 Audio)"), Speaker("Headphones (RØDE VideoMic GO II)"), Speaker("Speakers (INZONE Buds - Game)"),
            Mic("Microphone (RØDE VideoMic GO II)"), Mic("Headset Microphone (INZONE Buds - Chat)"))
            .WithDefault("Speakers (Realtek USB2.0 Audio)").WithDefault("Microphone (RØDE VideoMic GO II)");
        await routing.Start();
        await using var mute = new MuteFixture();
        mute.System.With(Mic("Microphone (RØDE VideoMic GO II)"));
        await mute.Start();
        await using var processing = new ProcessingFixture();
        processing.System.With(Mic("Real"), CableInput(), CableOutput()).WithDefault("Real");
        await processing.Start();
        await processing.Module.SetEnabledAsync(true);
        await using var volume = new VolumeFixture();
        foreach (var (name, i) in new[] { "Zoom Meetings", "Steam", "Spotify", "Discord" }.Select((n, i) => (n, i)))
            volume.System.WithSession(new AudioSessionInfo($"s{i}", "A", 300 + i, $@"C:\Apps\{name}.exe", name, false, SessionState.Active, 0.3f + i * 0.15f, false));
        await volume.Start();

        await Sta(theme =>
        {
            Shoot(Page("Output", (FrameworkElement)routing.Ui.Page!("output")), Path.Combine(folder, $"output-{theme}.png"));
            Shoot(Page("Microphone",
                (FrameworkElement)mute.Ui.Page!("mute"),
                (FrameworkElement)routing.Ui.Page!("microphone"),
                SettingsLayout.Section("Mic processing"),
                new ProcessingPage(processing.Module),
                SettingsLayout.Section("Mute shortcut"),
                (FrameworkElement)mute.Ui.Page!(null)), Path.Combine(folder, $"microphone-{theme}.png"));
            Shoot(Page("App volume", new VolumeView(volume.Module, VolumeSurface.Settings)), Path.Combine(folder, $"app-volume-{theme}.png"));
        });
    }

    private static FrameworkElement Page(string title, params FrameworkElement[] parts)
    {
        var heading = new TextBlock { Text = title, Margin = new Thickness(0, 0, 0, 14) };
        heading.SetResourceReference(FrameworkElement.StyleProperty, "DisplayTextStyle");
        heading.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
        var stack = new StackPanel { Width = 760, Margin = new Thickness(32, 24, 32, 32) };
        stack.Children.Add(heading);
        foreach (var part in parts) stack.Children.Add(part);
        var page = new Border { Child = stack };
        page.SetResourceReference(Border.BackgroundProperty, "WindowBackgroundBrush");
        page.SetResourceReference(TextElement.ForegroundProperty, "TextPrimaryBrush");
        return page;
    }

    private static void Shoot(FrameworkElement page, string path)
    {
        page.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        page.Arrange(new Rect(page.DesiredSize));
        page.UpdateLayout();
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Loaded);
        page.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(page.ActualWidth * 1.5), (int)Math.Ceiling(page.ActualHeight * 1.5), 144, 144, PixelFormats.Pbgra32);
        bitmap.Render(page);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(path);
        encoder.Save(file);
    }

    private static Task Sta(Action<string> run)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                foreach (var theme in new[] { "Dark", "Light" })
                {
                    app.Resources.MergedDictionaries.Clear();
                    foreach (var source in new[] { $"Themes/{theme}.xaml", "Themes/Palette.xaml", "Themes/SharedStyles.xaml", "Controls/Controls.xaml" })
                        app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri($"pack://application:,,,/Chippi.UI;component/{source}") });
                    run(theme);
                }
                done.SetResult();
            }
            catch (Exception ex) { done.SetException(ex); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return done.Task;
    }
}
