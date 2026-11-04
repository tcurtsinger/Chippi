// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Xml.Linq;
using Chippi.Audio.Soundboard;
using Chippi.Features.Sound.Soundboard.Library;
using Chippi.Features.Sound.Soundboard.Views;
using Chippi.UI.Controls;

namespace Chippi.Features.Sound.Tests;

/// <summary>Opt-in offscreen captures. Never creates a Window, HWND, audio service, or native input.</summary>
public sealed class SoundboardShots
{
    [Fact]
    public Task Render() => SoundUiThread.Sta(async () =>
    {
        if (Environment.GetEnvironmentVariable("CHIPPI_SOUNDBOARD_SHOTS") is not { Length: > 0 } output) return;
        var repository = Environment.GetEnvironmentVariable("CHIPPI_SOUNDBOARD_REPO") ?? throw new InvalidOperationException("CHIPPI_SOUNDBOARD_REPO is required for captures");
        var app = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        foreach (var theme in new[] { "Dark", "Light" })
        {
            app.Resources.MergedDictionaries.Clear();
            foreach (var path in new[] { $"Themes/{theme}.xaml", "Themes/Palette.xaml", "Themes/SharedStyles.xaml", "Controls/Controls.xaml" })
                app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri($"pack://application:,,,/Chippi.UI;component/{path}") });
            // FillFade is a shell animation helper. A settled offscreen frame uses the exact same
            // style values as direct Background/Opacity setters, without loading the executable.
            var sourceStyles = XDocument.Load(Path.Combine(repository, "src/Chippi.App/Island/IslandStyles.xaml"));
            var needed = new HashSet<string> { "IslandButtonStyle", "IslandAccentButtonStyle", "IslandFocusVisual", "IslandTileFocusVisual", "IslandAudioTabsStyle", "IslandSliderStyle" };
            XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
            foreach (var style in sourceStyles.Root!.Elements().Where(e => e.Name.LocalName == "Style" && !needed.Contains((string?)e.Attribute(xaml + "Key") ?? "")).ToArray()) style.Remove();
            var styles = sourceStyles.ToString()
                .Replace("i:FillFade.To", "Background", StringComparison.Ordinal)
                .Replace("i:FillFade.Opacity", "Opacity", StringComparison.Ordinal);
            app.Resources.MergedDictionaries.Add((ResourceDictionary)XamlReader.Parse(styles));
            string folder = Path.Combine(output, theme.ToLowerInvariant()); Directory.CreateDirectory(folder);
            var fake = Sample();
            fake.CoverPath = Path.Combine(folder, "fixture-cover.png"); MakeCoverFixture(fake.CoverPath);
            fake.Library.Clips[1] = fake.Library.Clips[1] with { ImageFile = "fixture-cover.png" };
            var page = SoundboardUiTests.Open(fake);
            fake.Playback = new("clip0", true, false, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(3)); fake.Notify();
            Shoot(page, Path.Combine(folder, "gallery.png"));
            SoundboardUiTests.Click(SoundboardUiTests.Named<Button>(page, "Edit sounds"));
            Shoot(page, Path.Combine(folder, "reorder.png")); page.HandleBack();
            var clipButton = SoundboardUiTests.Descendants(page).OfType<Button>().First(b => b.Tag is string);
            ((MenuItem)clipButton.ContextMenu.Items[0]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Shoot(page, Path.Combine(folder, "edit-sound.png")); page.HandleBack();
            SoundboardUiTests.Click(SoundboardUiTests.Named<Button>(page, "+ Add"));
            Shoot(page, Path.Combine(folder, "add-file.png"));
            SoundboardUiTests.Click(SoundboardUiTests.Named<Button>(page, "Link"));
            SoundboardUiTests.Named<TextBox>(page, "Audio link").Text = "https://www.youtube.com/watch?v=example";
            Shoot(page, Path.Combine(folder, "add-link.png"));
            page.HandleBack();
            fake.SendUnavailableReason = "Virtual mic is not installed"; fake.Notify();
            Shoot(page, Path.Combine(folder, "missing-mic.png"));
            fake.SendUnavailableReason = null;
            fake.Error = "Voice capture failed · sounds can still play. Check your microphone in Devices."; fake.Notify();
            Shoot(page, Path.Combine(folder, "voice-warning.png"));
            SoundboardUiTests.Close(page);
            var empty = SoundboardUiTests.Open(new SoundboardViewFake()); Shoot(empty, Path.Combine(folder, "empty.png")); SoundboardUiTests.Close(empty);
            var many = SoundboardViewFake.WithClips(64);
            many.Library = many.Library with { Collections = [.. Enumerable.Range(0, 15).Select(i => new SoundCollection(i == 0 ? "default" : $"c{i}", i == 0 ? "A very long collection name" : $"Collection {i}"))] };
            many.Library.Clips[0] = many.Library.Clips[0] with { Name = "A very long sound name that must remain a single readable tile" };
            var overflow = SoundboardUiTests.Open(many); Shoot(overflow, Path.Combine(folder, "overflow.png")); SoundboardUiTests.Close(overflow);
            var importing = Sample();
            importing.Prepare = async (_, progress, ct) => { progress?.Report(new("Downloading sound", .35)); await Task.Delay(Timeout.Infinite, ct); throw new InvalidOperationException(); };
            var importPage = SoundboardUiTests.Open(importing);
            SoundboardUiTests.Click(SoundboardUiTests.Named<Button>(importPage, "+ Add"));
            SoundboardUiTests.Descendants(importPage).OfType<SoundboardEditor>().Single().QueueFiles(["fixture.wav"]);
            await Dispatcher.Yield(DispatcherPriority.Background);
            Shoot(importPage, Path.Combine(folder, "preparing.png")); SoundboardUiTests.Close(importPage);
            var reviewData = Sample();
            reviewData.Library = reviewData.Library with { Collections = [new("default", "A very long collection name for reactions and game sounds"), new("games", "Games")] };
            var review = SoundboardUiTests.Open(reviewData);
            SoundboardUiTests.Click(SoundboardUiTests.Named<Button>(review, "+ Add"));
            SoundboardUiTests.Descendants(review).OfType<SoundboardEditor>().Single().QueueFiles(["fixture.wav"]);
            await Dispatcher.Yield(DispatcherPriority.Background);
            SoundboardUiTests.Named<TextBox>(review, "Sound name").Text = "Victory · 勝利 · انتصار";
            Shoot(review, Path.Combine(folder, "import-review.png"));
            SoundboardUiTests.Named<TextBox>(review, "End time").Text = "0:00";
            SoundboardUiTests.Click(SoundboardUiTests.Named<Button>(review, "Add"));
            Shoot(review, Path.Combine(folder, "trim-error.png")); SoundboardUiTests.Close(review);
            var failure = Sample();
            failure.Prepare = (_, _, _) => Task.FromException<SoundImportDraft>(new SoundboardException("This link is unavailable. Check the URL or choose a local file."));
            var failedPage = SoundboardUiTests.Open(failure);
            SoundboardUiTests.Click(SoundboardUiTests.Named<Button>(failedPage, "+ Add"));
            SoundboardUiTests.Click(SoundboardUiTests.Named<Button>(failedPage, "Link"));
            SoundboardUiTests.Named<TextBox>(failedPage, "Audio link").Text = "https://example.invalid/unavailable.mp3";
            SoundboardUiTests.Click(SoundboardUiTests.Named<Button>(failedPage, "Continue"));
            await Dispatcher.Yield(DispatcherPriority.Background);
            Shoot(failedPage, Path.Combine(folder, "import-error.png")); SoundboardUiTests.Close(failedPage);
        }
    });

    private static SoundboardViewFake Sample()
    {
        var names = new[] { "Air horn", "Applause", "Rimshot", "Crickets", "Drum roll", "Victory", "Laugh", "Record scratch", "Sad trombone", "Bell", "Whoosh", "Countdown" };
        var seconds = new[] { 3, 6, 2, 5, 4, 4, 3, 2, 4, 1, 2, 5 };
        var sample = SoundboardViewFake.WithClips(12);
        sample.Library = sample.Library with
        {
            Collections = [new("default", "Reactions"), new("games", "Games"), new("music", "Music")],
            Clips = [.. sample.Library.Clips.Select((clip, i) => clip with { Name = names[i], DurationSeconds = seconds[i], EndSeconds = seconds[i], Favorite = i < 4,
                Waveform = [.. Enumerable.Range(0, 160).Select(x => (float)(.12 + Math.Abs(Math.Sin(x * .3)) * .65))] })],
        };
        return sample;
    }

    private static void MakeCoverFixture(string path)
    {
        // A labeled synthetic cover fixture, not bundled starter content or a remote image.
        var cover = new Border { Width = 64, Height = 64, Background = new SolidColorBrush(Color.FromRgb(210, 181, 242)),
            Child = new TextBlock { Text = "A", FontSize = 38, FontWeight = FontWeights.Bold, Foreground = Brushes.Indigo,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
        cover.Measure(new Size(64, 64)); cover.Arrange(new Rect(0, 0, 64, 64)); cover.UpdateLayout();
        var bitmap = new RenderTargetBitmap(64, 64, 96, 96, PixelFormats.Pbgra32); bitmap.Render(cover);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(path); encoder.Save(file);
    }

    private static void Shoot(SoundboardPage body, string path)
    {
        SoundboardUi.Detach(body);
        var root = new Grid(); root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(32) }); root.RowDefinitions.Add(new RowDefinition());
        var header = new DockPanel();
        var back = SoundboardUi.Glyph(LucideIcons.ChevronLeft, 16); back.Margin = new Thickness(3, 0, 9, 0); DockPanel.SetDock(back, Dock.Left); header.Children.Add(back);
        var tabs = new SegmentedControl(); tabs.Items.Add("Devices"); tabs.Items.Add("App volume"); tabs.Items.Add("Soundboard"); tabs.SelectedIndex = 2;
        tabs.SetResourceReference(FrameworkElement.StyleProperty, "IslandAudioTabsStyle"); DockPanel.SetDock(tabs, Dock.Right); header.Children.Add(tabs);
        var title = SoundboardUi.Text("Audio", 16); title.FontWeight = FontWeights.SemiBold; header.Children.Add(title); root.Children.Add(header);
        body.Margin = new Thickness(0, 8, 0, 0); Grid.SetRow(body, 1); root.Children.Add(body);
        var shell = new Border { Width = 520, Height = 360, Padding = new Thickness(12), CornerRadius = new CornerRadius(8), Child = root };
        shell.SetResourceReference(Border.BackgroundProperty, "IslandBrush");
        shell.SetResourceReference(TextElement.FontFamilyProperty, "UiFont");
        shell.Measure(new Size(520, 360)); shell.Arrange(new Rect(0, 0, 520, 360)); shell.UpdateLayout();
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle); shell.UpdateLayout();
        // An unhosted visual has no composition target to flush pending drawing content.
        // Invalidate the settled tree explicitly before the single offscreen render.
        Redraw(shell);
        shell.UpdateLayout();
        Assert.True(header.ActualWidth > 0 && header.ActualHeight > 0 && title.ActualWidth > 0);
        Assert.Null(PresentationSource.FromVisual(shell));
        var bitmap = new RenderTargetBitmap(1040, 720, 192, 192, PixelFormats.Pbgra32); bitmap.Render(shell);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(path); encoder.Save(file);
        SoundboardUi.Detach(body); body.Margin = new Thickness(0);
    }

    private static void Redraw(DependencyObject root)
    {
        if (root is UIElement element) element.InvalidateVisual();
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) Redraw(VisualTreeHelper.GetChild(root, i));
    }
}
