// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using Chippi.App.Island.Media;
using Chippi.App.Settings.Pages;

namespace Chippi.Island.Tests;

/// <summary>Settings › Now Playing (brief F3 #9): the island's player, sharing its sessions. Unshown, fake sessions.</summary>
public sealed class NowPlayingPageTests
{
    private static MediaTrack Track(string id, string source, bool playing = true) =>
        new(id, source, "Weekend Mix", "Various Artists", playing, true, true, true, true, true,
            TimeSpan.FromSeconds(72), TimeSpan.FromSeconds(232), DateTimeOffset.Now, null);

    [Fact]
    public void Nothing_playing_shows_the_placeholder_and_disables_every_button() => WpfTestThread.Run(() =>
    {
        foreach (var media in new IMediaSessions?[] { null, new CountingMedia() })
        {
            var page = new NowPlayingPage(media);
            Assert.Contains(Descendants(page).OfType<TextBlock>(), t => t.Text == "Nothing playing");
            Assert.All(Descendants(page).OfType<Button>(), b => Assert.False(b.IsEnabled));
            Assert.False(Descendants(page).OfType<ComboBox>().Single().IsEnabled);
        }
    });

    [Fact]
    public void Transport_calls_the_shared_sessions_and_the_source_combo_selects() => WpfTestThread.Run(() =>
    {
        var media = new CountingMedia { Sessions = [Track("spotify", "Spotify"), Track("edge", "Microsoft Edge", playing: false)] };
        var page = new NowPlayingPage(media);
        Assert.Contains(Descendants(page).OfType<TextBlock>(), t => t.Text == "Weekend Mix");
        Assert.Contains(Descendants(page).OfType<TextBlock>(), t => t.Text == "1:12");
        foreach (var name in new[] { "Previous track", "Pause", "Next track" })
        {
            var button = Descendants(page).OfType<Button>().Single(b => AutomationProperties.GetName(b) == name);
            ((IInvokeProvider)new ButtonAutomationPeer(button)).Invoke();
        }
        System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background);
        Assert.Equal(["previous", "play-pause", "next"], media.Calls);

        var source = Descendants(page).OfType<ComboBox>().Single();
        Assert.True(source.IsEnabled);
        Assert.Equal("Spotify", source.SelectedItem);
        source.SelectedIndex = 1;
        Assert.Equal("edge", media.Selected);
    });

    /// <summary>Off-screen shots of a track and of nothing playing; only when CHIPPI_ISLAND_SHOTS names a folder.</summary>
    [Fact]
    public void Render() => WpfTestThread.Run(() =>
    {
        if (Environment.GetEnvironmentVariable("CHIPPI_ISLAND_SHOTS") is not { Length: > 0 } folder)
            return;
        System.IO.Directory.CreateDirectory(folder);
        var styles = new ResourceDictionary { Source = new Uri("pack://application:,,,/Chippi;component/Settings/SettingsStyles.xaml") };
        Application.Current.Resources.MergedDictionaries.Add(styles);
        try
        {
            foreach (var (name, media) in new[] { ("now-playing", new CountingMedia { Sessions = [Track("spotify", "Spotify")] }), ("now-playing-empty", new CountingMedia()) })
            {
                var page = new Border { Width = 824, Padding = new Thickness(32, 24, 32, 32), Child = new NowPlayingPage(media) };
                page.SetResourceReference(Border.BackgroundProperty, "WindowBackgroundBrush");
                page.Measure(new Size(824, double.PositiveInfinity));
                page.Arrange(new Rect(page.DesiredSize));
                page.UpdateLayout();
                var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(1236, (int)Math.Ceiling(page.ActualHeight * 1.5), 144, 144, System.Windows.Media.PixelFormats.Pbgra32);
                bitmap.Render(page);
                var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                using var file = System.IO.File.Create(System.IO.Path.Combine(folder, name + ".png"));
                encoder.Save(file);
            }
        }
        finally { Application.Current.Resources.MergedDictionaries.Remove(styles); }
    });

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach (var descendant in Descendants(child)) yield return descendant;
    }

    private sealed class CountingMedia : IMediaSessions
    {
        public IReadOnlyList<MediaTrack> Sessions { get; set; } = [];
        public MediaTrack? Current => Sessions.FirstOrDefault(s => s.Id == Selected) ?? Sessions.FirstOrDefault();
        public string? Selected { get; private set; }
        public List<string> Calls { get; } = [];
        public event EventHandler? Changed;
        public void Select(string? id) { Selected = id; Changed?.Invoke(this, EventArgs.Empty); }
        public Task PreviousAsync() { Calls.Add("previous"); return Task.CompletedTask; }
        public Task PlayPauseAsync() { Calls.Add("play-pause"); return Task.CompletedTask; }
        public Task NextAsync() { Calls.Add("next"); return Task.CompletedTask; }
        public void Dispose() { }
    }
}
