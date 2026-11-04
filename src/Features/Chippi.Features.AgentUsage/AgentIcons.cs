// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Chippi.UI.Controls;

namespace Chippi.Features.AgentUsage;

/// <summary>The installed Windows apps' artwork, shared by every provider surface.</summary>
public static class AgentIcons
{
    private static AgentIconSource _icons = new(AppIcon.LoadInstalledAsync);

    /// <summary>The resolved icon, or neutral initials while Windows is loading or the app is absent.</summary>
    public static ImageSource Source(AgentKind agent) => _icons.Source(agent);

    /// <summary>Bind without blocking the UI. Reusing or clearing an image invalidates its previous request.</summary>
    public static void SetSource(Image image, AgentKind? agent) => _ = _icons.SetSource(image, agent);

    // UI fixtures must never discover apps from the owner's installation.
    internal static void UseLoaderForTests(Func<string[], Task<BitmapSource?>> load) => _icons = new(load);
}

internal sealed class AgentIconSource(Func<string[], Task<BitmapSource?>> load)
{
    private readonly Dictionary<AgentKind, Entry> _entries = [];
    private readonly ConditionalWeakTable<Image, Binding> _bindings = new();

    private sealed class Entry(ImageSource fallback)
    {
        public ImageSource Fallback { get; } = fallback;
        public Task<BitmapSource?>? Pending { get; set; }
        public ImageSource Source => Pending is { IsCompletedSuccessfully: true, Result: { } image } ? image : Fallback;
    }

    private sealed class Binding
    {
        public bool Bound;
        public AgentKind? Agent;
        public long Version;
    }

    public ImageSource Source(AgentKind agent) => Get(agent).Source;

    public Task SetSource(Image image, AgentKind? agent)
    {
        image.VerifyAccess();
        AppIcon.Configure(image);
        var binding = _bindings.GetOrCreateValue(image);
        if (binding.Bound && binding.Agent == agent)
            return Task.CompletedTask;
        binding.Bound = true;
        binding.Agent = agent;
        long version = ++binding.Version;
        if (agent is not { } provider)
        {
            image.Source = null;
            return Task.CompletedTask;
        }

        var entry = Get(provider);
        entry.Pending ??= Read(provider);
        if (entry.Pending.IsCompletedSuccessfully)
        {
            image.Source = entry.Pending.Result ?? entry.Fallback;
            return Task.CompletedTask;
        }
        image.Source = entry.Fallback;
        return Complete(new WeakReference<Image>(image), image.Dispatcher, binding, version, entry.Pending);
    }

    private Entry Get(AgentKind agent)
    {
        if (!_entries.TryGetValue(agent, out var entry))
            _entries.Add(agent, entry = new Entry(Fallback(agent)));
        return entry;
    }

    private async Task<BitmapSource?> Read(AgentKind agent)
    {
        try { return await load(Identities(agent)).ConfigureAwait(false); }
        catch (Exception) { return null; } // A missing/inaccessible desktop app keeps its neutral initials.
    }

    private static async Task Complete(WeakReference<Image> image, Dispatcher dispatcher, Binding binding, long version, Task<BitmapSource?> pending)
    {
        var source = await pending.ConfigureAwait(false);
        if (source is null || dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished)
            return;
        try
        {
            await dispatcher.InvokeAsync(() =>
            {
                if (binding.Version == version && image.TryGetTarget(out var target))
                    target.Source = source;
            }, DispatcherPriority.Background).Task.ConfigureAwait(false);
        }
        catch (TaskCanceledException) { } // The window's dispatcher can shut down while the shell responds.
        catch (InvalidOperationException) when (dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished) { }
    }

    internal static string[] Identities(AgentKind agent) => agent switch
    {
        AgentKind.Claude => ["com.squirrel.AnthropicClaude.claude"],
        // This package can be displayed as "ChatGPT" by Windows. Its identity still belongs to Codex;
        // never select a different ChatGPT app or the CodexPrimaryRuntime package by display name.
        AgentKind.Codex => ["OpenAI.Codex_"],
        _ => throw new ArgumentOutOfRangeException(nameof(agent)),
    };

    private static ImageSource Fallback(AgentKind agent)
    {
        string initials = agent switch
        {
            AgentKind.Claude => "C",
            AgentKind.Codex => "Cx",
            _ => throw new ArgumentOutOfRangeException(nameof(agent)),
        };
        var text = new FormattedText(initials, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal),
            30, Brushes.White, 1);
        var drawing = new DrawingGroup();
        using (var dc = drawing.Open())
        {
            dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(77, 77, 83)), null, new Rect(0, 0, 64, 64), 12, 12);
            dc.DrawText(text, new Point((64 - text.Width) / 2, (64 - text.Height) / 2));
        }
        var image = new DrawingImage(drawing);
        image.Freeze();
        return image;
    }
}
