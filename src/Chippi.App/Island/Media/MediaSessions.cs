// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Chippi.Core.Diagnostics;
using Windows.Media.Control;

namespace Chippi.App.Island.Media;

/// <summary>One app's media session as Windows reports it, for Now Playing.</summary>
/// <param name="Id">The app's AppUserModelID; picks the session.</param>
/// <param name="Source">The app's display name, such as "Spotify".</param>
/// <param name="UpdatedAt">When Windows last reported <paramref name="Position"/>.</param>
/// <param name="CanPlay">The app's separate Play, Pause and Toggle capabilities, as Windows reports them.</param>
public sealed record MediaTrack(
    string Id, string Source, string Title, string Artist, bool Playing,
    bool CanPrevious, bool CanPlay, bool CanPause, bool CanToggle, bool CanNext,
    TimeSpan Position, TimeSpan Duration, DateTimeOffset UpdatedAt, ImageSource? Artwork)
{
    /// <summary>
    /// What the play/pause button sends: Pause while playing and Play while not, when the app offers
    /// that; else Toggle; else nothing (the button is disabled).
    /// </summary>
    public PlayPauseCommand PlayPause => Playing
        ? CanPause ? PlayPauseCommand.Pause : CanToggle ? PlayPauseCommand.Toggle : PlayPauseCommand.None
        : CanPlay ? PlayPauseCommand.Play : CanToggle ? PlayPauseCommand.Toggle : PlayPauseCommand.None;

    /// <summary>Where playback is at <paramref name="now"/>: the reported position, moved on while playing.</summary>
    public TimeSpan PositionAt(DateTimeOffset now)
    {
        var at = Playing && UpdatedAt != default ? Position + (now - UpdatedAt) : Position;
        if (at < TimeSpan.Zero)
            at = TimeSpan.Zero;
        return Duration > TimeSpan.Zero && at > Duration ? Duration : at;
    }
}

public enum PlayPauseCommand { None, Play, Pause, Toggle }

/// <summary>The media sessions Now Playing shows and controls. Raises <see cref="Changed"/> on the UI thread.</summary>
public interface IMediaSessions : IDisposable
{
    IReadOnlyList<MediaTrack> Sessions { get; }

    /// <summary>The chosen session, else the one Windows calls current, else the first.</summary>
    MediaTrack? Current { get; }

    event EventHandler? Changed;

    /// <summary>Shows this app's session; null follows Windows' current one.</summary>
    void Select(string? id);

    Task PreviousAsync();

    Task PlayPauseAsync();

    Task NextAsync();
}

/// <summary>
/// Windows' media sessions (GlobalSystemMediaTransportControlsSessionManager). Read-only except for
/// the transport commands the user presses. Create on the UI thread; every refresh runs there.
/// </summary>
public sealed class SystemMediaSessions : IMediaSessions
{
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private readonly Dictionary<string, (string Key, ImageSource? Image)> _art = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<GlobalSystemMediaTransportControlsSession> _watched = [];
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private IReadOnlyList<MediaTrack> _sessions = [];
    private string? _selected;
    private string? _windowsCurrent;
    private bool _pending;
    private int _version;
    private bool _disposed;

    public IReadOnlyList<MediaTrack> Sessions => _sessions;

    public MediaTrack? Current =>
        _sessions.FirstOrDefault(s => s.Id == _selected) ?? _sessions.FirstOrDefault(s => s.Id == _windowsCurrent) ?? _sessions.FirstOrDefault();

    public event EventHandler? Changed;

    public async Task StartAsync()
    {
        try
        {
            _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        }
        catch (Exception ex)
        {
            Log.Warn("Now Playing: media sessions unavailable", ex);
            return;
        }
        if (_disposed)
            return;
        _manager.SessionsChanged += (_, _) => Schedule();
        _manager.CurrentSessionChanged += (_, _) => Schedule();
        Schedule();
    }

    public void Select(string? id)
    {
        _selected = id;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public Task PreviousAsync() => Command(s => s.TrySkipPreviousAsync().AsTask());

    public Task PlayPauseAsync() => Current?.PlayPause switch
    {
        PlayPauseCommand.Play => Command(s => s.TryPlayAsync().AsTask()),
        PlayPauseCommand.Pause => Command(s => s.TryPauseAsync().AsTask()),
        PlayPauseCommand.Toggle => Command(s => s.TryTogglePlayPauseAsync().AsTask()),
        _ => Task.CompletedTask,
    };

    public Task NextAsync() => Command(s => s.TrySkipNextAsync().AsTask());

    private async Task Command(Func<GlobalSystemMediaTransportControlsSession, Task<bool>> command)
    {
        if (Session(Current?.Id) is not { } session)
            return;
        try
        {
            if (!await command(session))
                Log.Info($"Now Playing: {Current?.Source} declined the command");
        }
        catch (Exception ex)
        {
            Log.Warn("Now Playing: media command failed", ex);
        }
    }

    private GlobalSystemMediaTransportControlsSession? Session(string? id) =>
        id is null ? null : _manager?.GetSessions().FirstOrDefault(s => s.SourceAppUserModelId == id);

    // Session events arrive on thread-pool threads, often several at once: one refresh on the UI thread.
    private void Schedule()
    {
        if (_disposed)
            return;
        _dispatcher.BeginInvoke(() =>
        {
            if (_pending || _disposed)
                return;
            _pending = true;
            _ = RefreshAsync();
        });
    }

    private async Task RefreshAsync()
    {
        try
        {
            await Task.Yield();
            _pending = false;
            if (_manager is null || _disposed)
                return;
            // A newer refresh may start while this one awaits; only the newest one's result counts.
            int version = ++_version;
            var sessions = _manager.GetSessions();
            foreach (var session in sessions.Where(_watched.Add))
            {
                session.MediaPropertiesChanged += (_, _) => Schedule();
                session.PlaybackInfoChanged += (_, _) => Schedule();
                session.TimelinePropertiesChanged += (_, _) => Schedule();
            }
            _watched.IntersectWith(sessions);
            _windowsCurrent = _manager.GetCurrentSession()?.SourceAppUserModelId;
            var tracks = new List<MediaTrack>();
            foreach (var session in sessions)
            {
                if (await Read(session) is { } track)
                    tracks.Add(track);
            }
            if (_disposed || version != _version)
                return;
            foreach (var gone in _art.Keys.Where(id => tracks.All(t => t.Id != id)).ToList())
                _art.Remove(gone);
            _sessions = tracks;
            Changed?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            Log.Warn("Now Playing: reading media sessions failed", ex);
        }
    }

    private async Task<MediaTrack?> Read(GlobalSystemMediaTransportControlsSession session)
    {
        try
        {
            var id = session.SourceAppUserModelId;
            var properties = await session.TryGetMediaPropertiesAsync();
            var playback = session.GetPlaybackInfo();
            var timeline = session.GetTimelineProperties();
            var controls = playback.Controls;
            string title = properties?.Title ?? "";
            string artist = properties?.Artist ?? "";
            var duration = timeline.EndTime - timeline.StartTime;
            return new MediaTrack(id, AppName(id), title, artist,
                playback.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing,
                controls.IsPreviousEnabled, controls.IsPlayEnabled, controls.IsPauseEnabled, controls.IsPlayPauseToggleEnabled, controls.IsNextEnabled,
                timeline.Position - timeline.StartTime, duration > TimeSpan.Zero ? duration : TimeSpan.Zero, timeline.LastUpdatedTime,
                await Artwork(id, title + "\n" + artist, properties));
        }
        catch (Exception ex)
        {
            Log.Warn("Now Playing: reading a media session failed", ex);
            return null;
        }
    }

    // Decoded once per track; a session without a thumbnail shows the neutral placeholder.
    private async Task<ImageSource?> Artwork(string id, string key, GlobalSystemMediaTransportControlsSessionMediaProperties? properties)
    {
        if (_art.TryGetValue(id, out var cached) && cached.Key == key)
            return cached.Image;
        ImageSource? image = null;
        if (properties?.Thumbnail is { } thumbnail)
        {
            try
            {
                using var stream = await thumbnail.OpenReadAsync();
                using var source = stream.AsStreamForRead();
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.DecodePixelWidth = 224;
                bitmap.StreamSource = source;
                bitmap.EndInit();
                bitmap.Freeze();
                image = bitmap;
            }
            catch (Exception ex)
            {
                Log.Warn("Now Playing: artwork unreadable", ex);
            }
        }
        _art[id] = (key, image);
        return image;
    }

    /// <summary>A readable app name from an AppUserModelID: the package's display name, else the exe or app part.</summary>
    internal static string AppName(string id)
    {
        try
        {
            if (id.Contains('!') && Windows.ApplicationModel.AppInfo.GetFromAppUserModelId(id) is { } info
                && !string.IsNullOrWhiteSpace(info.DisplayInfo.DisplayName))
                return info.DisplayInfo.DisplayName;
        }
        catch (Exception)
        {
            // Not a packaged app: fall through to the id itself.
        }
        string name = id.Contains('!') ? id[(id.IndexOf('!') + 1)..] : id;
        name = Path.GetFileName(name);
        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            name = name[..^4];
        return name.Length > 0 ? char.ToUpperInvariant(name[0]) + name[1..] : id;
    }

    public void Dispose()
    {
        _disposed = true;
        _watched.Clear();
        _manager = null;
    }
}
