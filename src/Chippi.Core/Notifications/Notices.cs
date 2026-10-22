// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.Core.Config;
using Chippi.Core.Island;
using Microsoft.Extensions.Logging;

namespace Chippi.Core.Notifications;

/// <summary>Where a notice may appear. The module's own flyout tab always lists every event; this is only about the two extra places.</summary>
[Flags]
public enum NoticePlaces
{
    None = 0,
    Windows = 1,
    Island = 2,
}

/// <summary>An event a module can announce, e.g. "organized". The user picks its places in the module's settings.</summary>
/// <param name="Id">Stable key, also the settings key. Lowercase, e.g. "organized".</param>
/// <param name="Name">What the settings grid shows, Bare Label Rule: "Organized".</param>
/// <param name="Default">Places used until the user changes them.</param>
public sealed record NoticeKind(string Id, string Name, NoticePlaces Default);

/// <summary>A button on a Windows notification. <see cref="Activation"/> is routed like a launch, e.g. <c>--downloads-run 42</c>.</summary>
public sealed record NoticeAction(string Label, string Activation);

/// <summary>One event to announce.</summary>
/// <param name="Kind">A declared <see cref="NoticeKind.Id"/>.</param>
/// <param name="Title">State first, short: "4 files organized".</param>
public sealed record Notice(string Kind, string Title)
{
    /// <summary>Second line: on the island it is the source line; on Windows the body.</summary>
    public string? Body { get; init; }

    /// <summary>Buttons on the Windows notification. The island ignores them.</summary>
    public IReadOnlyList<NoticeAction> Actions { get; init; } = [];

    /// <summary>What the clicked notification body activates, routed like <see cref="NoticeAction.Activation"/>.</summary>
    public string? Activation { get; init; }

    /// <summary>How the island shows it (Stash for files, Status for background state).</summary>
    public IslandActivityKind IslandKind { get; init; } = IslandActivityKind.Stash;

    /// <summary>Gumdrop celebrates when the notice reaches the island.</summary>
    public bool Celebrate { get; init; }
}

/// <summary>The shell's Windows notification backend. Must not throw for a suppressed or disabled notification.</summary>
public interface IToastPresenter
{
    void Show(string moduleId, Notice notice);
}

/// <summary>A module's notices: declare the kinds once, then <see cref="Show"/>. Never fails the caller.</summary>
public interface INotifier
{
    /// <summary>Declares the module's event kinds (replacing earlier ones). Call in StartAsync.</summary>
    void Declare(IReadOnlyList<NoticeKind> kinds);

    IReadOnlyList<NoticeKind> Kinds { get; }

    /// <summary>The user's places for a kind, or its default.</summary>
    NoticePlaces PlacesFor(string kindId);

    /// <summary>Saves the places for a kind in the module's settings (<c>modules.&lt;id&gt;.notices</c>).</summary>
    void SetPlaces(string kindId, NoticePlaces places);

    /// <summary>Sends the notice to the places set for its kind. Disabled places, a missing backend or a backend failure are skipped silently (logged).</summary>
    void Show(Notice notice);
}

/// <summary>The <c>notices</c> part of a module's settings section; other keys in the section are left alone.</summary>
internal sealed class NoticeSettings
{
    public Dictionary<string, NoticePlaces>? Notices { get; set; } = [];
}

/// <summary>One module's notifier: reads and saves its places in settings, sends to Windows and the island.</summary>
internal sealed class ModuleNotifier(
    string moduleId, ConfigStore config, IIslandPublisher island, Modules.ModuleServices services, ILogger logger) : INotifier, IDisposable
{
    // Process-wide, so a reinstalled module's notices never reuse an earlier lifetime's keys.
    private static long s_sequence;

    private readonly Lock _gate = new();
    private readonly HashSet<ITimer> _timers = [];
    private IReadOnlyList<NoticeKind> _kinds = [];
    private bool _disposed;

    public IReadOnlyList<NoticeKind> Kinds
    {
        get { lock (_gate) return _kinds; }
    }

    public void Declare(IReadOnlyList<NoticeKind> kinds)
    {
        ArgumentNullException.ThrowIfNull(kinds);
        if (kinds.Select(k => k.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() != kinds.Count)
            throw new ArgumentException("Notice kind ids must be unique.", nameof(kinds));
        lock (_gate) _kinds = [.. kinds];
    }

    public NoticePlaces PlacesFor(string kindId)
    {
        // A hand-edited or imported "notices": null reads as no choices yet: defaults apply.
        var saved = config.GetModuleSettings<NoticeSettings>(moduleId).Notices;
        if (saved is not null && saved.TryGetValue(kindId, out var places))
            return places;
        return Kinds.FirstOrDefault(k => string.Equals(k.Id, kindId, StringComparison.OrdinalIgnoreCase))?.Default ?? NoticePlaces.None;
    }

    public void SetPlaces(string kindId, NoticePlaces places)
    {
        var settings = config.GetModuleSettings<NoticeSettings>(moduleId);
        settings.Notices ??= [];
        settings.Notices[kindId] = places;
        config.SetModuleSettings(moduleId, settings);
    }

    public void Show(Notice notice)
    {
        ArgumentNullException.ThrowIfNull(notice);
        try
        {
            Deliver(notice);
        }
        catch (Exception ex)
        {
            // The contract: a notice never fails the caller (the organizer's move already happened).
            logger.LogWarning(ex, "Notice {Kind} could not be shown", notice.Kind);
        }
    }

    /// <summary>The module stopped: pending island expiries die with this lifetime and never touch a later one.</summary>
    public void Dispose()
    {
        List<ITimer> timers;
        lock (_gate)
        {
            _disposed = true;
            timers = [.. _timers];
            _timers.Clear();
        }
        foreach (var timer in timers)
            timer.Dispose();
    }

    private void Deliver(Notice notice)
    {
        // The app-wide switches (Settings › Notifications) silence a place without touching each event's choice.
        var app = config.GetSection<AppSettings>(AppSettings.SectionName);
        var places = PlacesFor(notice.Kind);
        if (!app.WindowsNotices)
            places &= ~NoticePlaces.Windows;
        if (!app.IslandNotices)
            places &= ~NoticePlaces.Island;

        if (places.HasFlag(NoticePlaces.Windows))
        {
            if (services.Toasts is { } toasts)
            {
                try
                {
                    toasts.Show(moduleId, notice);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Windows notification for {Kind} failed", notice.Kind);
                }
            }
            else
            {
                logger.LogDebug("Windows notifications are not available; {Kind} skipped there", notice.Kind);
            }
        }

        if (places.HasFlag(NoticePlaces.Island))
        {
            lock (_gate)
            {
                if (_disposed)
                    return;
            }
            string key = $"notice-{Interlocked.Increment(ref s_sequence)}";
            island.Publish(new IslandActivity(key, notice.IslandKind, notice.Title, notice.Body ?? ""));
            if (notice.Celebrate)
                island.ReportSuccess(notice.Title);

            // The island shows a notice for a few seconds, then the activity goes. The timer is
            // tracked so stopping the module cancels it.
            var holder = new ITimer?[1];
            var timer = services.Time.CreateTimer(_ =>
            {
                lock (_gate)
                {
                    if (_disposed)
                        return;
                    if (holder[0] is { } self)
                        _timers.Remove(self);
                }
                island.Clear(key);
                holder[0]?.Dispose();
            }, null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            lock (_gate)
            {
                if (_disposed)
                {
                    timer.Dispose();
                    return;
                }
                holder[0] = timer;
                _timers.Add(timer);
            }
            timer.Change(services.IslandNoticeDuration, Timeout.InfiniteTimeSpan); // armed only once tracked
        }
    }
}
/// <summary>A notifier that drops everything: the default for contexts built outside the registry (tests).</summary>
public sealed class NullNotifier : INotifier
{
    public static NullNotifier Instance { get; } = new();

    private NullNotifier()
    {
    }

    public IReadOnlyList<NoticeKind> Kinds => [];

    public void Declare(IReadOnlyList<NoticeKind> kinds)
    {
    }

    public NoticePlaces PlacesFor(string kindId) => NoticePlaces.None;

    public void SetPlaces(string kindId, NoticePlaces places)
    {
    }

    public void Show(Notice notice)
    {
    }
}
