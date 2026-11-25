// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Collections.Concurrent;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Chippi.UI.Controls;

/// <summary>Windows app artwork, shared by Search, Audio, Actions, System and Agents.</summary>
public static class AppIcon
{
    private static readonly object Gate = new();
    private static readonly Dictionary<(string, int), Task<BitmapSource?>> Cache = [];
    private static readonly Lazy<BlockingCollection<Action>> Work = new(() =>
    {
        var queue = new BlockingCollection<Action>();
        var thread = new Thread(() => { foreach (var action in queue.GetConsumingEnumerable()) action(); })
        { IsBackground = true, Name = "Windows app icons" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return queue;
    });

    // Windows may return a larger native frame; WPF performs the final downsample, not GDI.
    public static int SourcePixels(int displayPixels) => displayPixels <= 128 ? 128 : 256;

    /// <summary>Read on an STA worker, never on the UI thread. Callers may own their request cache.</summary>
    public static BitmapSource? Read(string? parsing, int pixels = 64) => WindowsAppIcons.Read(parsing, SourcePixels(pixels));

    /// <summary>The <see cref="LoadExactAsync"/> frame, read here: on an STA worker, never on the UI thread.</summary>
    public static BitmapSource? ReadExact(string? parsing, int pixels) => WindowsAppIcons.Read(parsing, Math.Clamp(pixels, 16, 256), exact: true);

    /// <summary>A bounded window-icon fallback. Physical display size determines the DPI hint.</summary>
    public static BitmapSource? ReadWindow(nint window, int pixels) => WindowsAppIcons.ReadWindow(window, pixels);

    public static Task<BitmapSource?> LoadAsync(string? parsing, int pixels = 64) => Load(parsing, SourcePixels(pixels));

    /// <summary>
    /// The frame Windows draws at exactly <paramref name="pixels"/> device pixels: an icon's own hinted small size where it
    /// has one, which stays sharp where a 256-pixel frame scaled down to it goes soft (the dock's icons at rest).
    /// </summary>
    public static Task<BitmapSource?> LoadExactAsync(string? parsing, int pixels) => Load(parsing, -Math.Clamp(pixels, 16, 256));

    /// <summary>A negative <paramref name="source"/> asks for exactly that many pixels (cached apart from the larger frames).</summary>
    private static Task<BitmapSource?> Load(string? parsing, int source)
    {
        if (string.IsNullOrWhiteSpace(parsing)) return Task.FromResult<BitmapSource?>(null);
        var key = (parsing.ToUpperInvariant(), source);
        lock (Gate)
        {
            if (Cache.TryGetValue(key, out var known)) return known;
            if (Cache.Count >= 256)
                foreach (var old in Cache.Where(pair => pair.Value.IsCompleted).Select(pair => pair.Key).Take(64).ToArray())
                    Cache.Remove(old);
            return Cache[key] = Run(() => WindowsAppIcons.Read(parsing, Math.Abs(source), exact: source < 0));
        }
    }

    /// <summary>Resolve registered identities, never display names or similarly named helper processes.</summary>
    public static Task<BitmapSource?> LoadInstalledAsync(params string[] identityPrefixes)
    {
        string[] identities = [.. identityPrefixes];
        return Run(() => WindowsAppIcons.ReadInstalled(identities));
    }

    private static Task<BitmapSource?> Run(Func<BitmapSource?> read)
    {
        var done = new TaskCompletionSource<BitmapSource?>(TaskCreationOptions.RunContinuationsAsynchronously);
        Work.Value.Add(() =>
        {
            try { done.SetResult(read()); }
            catch (Exception) { done.SetResult(null); } // inaccessible/uninstalled app: keep its monogram
        });
        return done.Task;
    }

    public static void Configure(Image image)
    {
        image.SnapsToDevicePixels = true;
        image.UseLayoutRounding = true;
        image.Stretch = Stretch.Uniform;
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
    }

    /// <summary>A stable-size app mark. Its monogram stays until Windows artwork is available.</summary>
    public static FrameworkElement View(string? path, double size, UIElement fallback)
    {
        var image = new Image { Width = size, Height = size, IsHitTestVisible = false };
        Configure(image);
        var box = new Grid { Width = size, Height = size, VerticalAlignment = VerticalAlignment.Center };
        box.Children.Add(fallback);
        box.Children.Add(image);
        var pending = LoadAsync(path, (int)Math.Ceiling(size * 4));
        if (pending.IsCompletedSuccessfully) Paint(pending.Result);
        else _ = Show();
        return box;

        void Paint(BitmapSource? source)
        {
            if (source is null) return;
            image.Source = source;
            fallback.Visibility = Visibility.Collapsed;
        }

        async Task Show()
        {
            var source = await pending.ConfigureAwait(false);
            if (source is null || image.Dispatcher.HasShutdownStarted) return;
            try { await image.Dispatcher.InvokeAsync(() => Paint(source)); }
            catch (TaskCanceledException) { }
            catch (InvalidOperationException) when (image.Dispatcher.HasShutdownStarted) { }
        }
    }
}
