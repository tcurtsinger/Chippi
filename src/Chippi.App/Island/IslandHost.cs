// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.IO;
using System.Windows;
using System.Windows.Threading;
using Chippi.App.Island.Media;
using Chippi.App.Pet;
using Chippi.App.Shell;
using Chippi.Core.Config;
using Chippi.Core.Island;
using Chippi.Core.Modules;
using Microsoft.Extensions.Logging;

namespace Chippi.App.Island;

/// <summary>
/// Composes the island (WS3): the window, the foreground tracker, the activity hub, the pet's mood,
/// the island settings and, with <see cref="ShellContext.DebugMode"/>, the fake-activity menu.
/// Create and start on the UI thread from the composition root.
/// </summary>
public sealed class IslandHost : IDisposable
{
    /// <summary>The Keep awake module's id; its Status activity makes Gumdrop wide awake.</summary>
    public const string KeepAwakeModuleId = "keep-awake";

    private const string DebugModuleId = "debug";
    private const string DebugWideAwakeKey = "debug-wide-awake";

    private readonly ShellContext _shell;
    private readonly ILogger _logger;
    private readonly Dispatcher _dispatcher;
    private readonly IslandWindow _window;
    private readonly ForegroundTracker _tracker;
    private SystemMediaSessions? _media;
    private bool _refreshPending;
    private bool _disposed;

    public IslandHost(ShellContext shell)
    {
        _shell = shell;
        _logger = shell.LoggerFactory.CreateLogger("Island");
        _dispatcher = Dispatcher.CurrentDispatcher;
        _window = new IslandWindow();
        _tracker = new ForegroundTracker(_dispatcher, _logger);

    }

    /// <summary>The pet's mood for the live activities: wide awake while Keep awake holds the PC, idle while anything is live, sleepy at rest.</summary>
    public static PetMood MoodFor(IReadOnlyList<IslandActivity> activities) =>
        activities.Any(a => a.Kind == IslandActivityKind.Status && a.ModuleId == KeepAwakeModuleId) ? PetMood.WideAwake
        : activities.Count > 0 ? PetMood.Idle
        : PetMood.Sleepy;

    public void Start()
    {
        _window.Initialize();
        _tracker.Ignore = [_window.Handle, _window.PanelHandle];
        _window.SettingsRequested += (_, _) => _shell.Commands.OpenSettings();
        _window.LastPageChanged += (_, key) => UpdateSettings(s => s.IslandLastPage = key);
        // A page's Paste goes to the app that was in front before the island took the keyboard (Phase 5 notes #62).
        _window.CaptureTarget = () => _shell.Clipboard.CaptureTarget();
        _shell.Services.IslandPasteTarget = () => _window.PasteTarget;
        _shell.Services.CloseIsland = _window.ClosePages;
        // The tray icon and a second launch open these same pages: in the island, or docked by the tray.
        _window.TrayIconRect = () => _shell.Commands.TrayAnchor()?.Icon;
        _shell.Commands.RegisterIslandHold(_window.HoldIslandOpen);
        _shell.Commands.RegisterPages(
            keyboard => _window.TogglePages(_shell.Commands.TrayAnchor(), keyboard),
            () => _window.ShowPages(_shell.Commands.TrayAnchor(), keyboard: true),
            key => _window.ShowPage(_shell.Commands.TrayAnchor(), key));
        _window.IsVisibleChanged += (_, _) => _shell.Commands.SetIslandShowing(_window.IsVisible);
        StartMedia();
        _tracker.Changed += (_, _) =>
        {
            _window.SetMonitor(_tracker.Monitor);
            _window.SetSuppressed(_tracker.FullscreenInFront);
        };
        _window.DisplayChanged += (_, _) => _tracker.OnDisplaysChanged();
        _window.PlacementChosen += OnPlacementChosen;
        _window.DropRefused += (_, _) => _logger.LogInformation("Island drop refused: the taskbar's edge, the bottom or a corner");

        _shell.Island.Changed += (_, _) => ScheduleRefresh();
        _shell.Registry.Changed += (_, _) => _dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            if (!_disposed)
                RefreshPanels();
        });
        _shell.Island.Succeeded += (_, _) => _dispatcher.BeginInvoke(() => _window.Celebrate());
        _shell.Config.SectionChanged += OnSectionChanged;

        // The tab's right click is the tray icon's menu, so Quit stays reachable with the icon hidden.
        var menu = new List<(string Label, Action Action)?>();
        if (_shell.DebugMode)
            menu.AddRange([.. DebugMenu(), null]);
        menu.Add(("Open settings", () => _shell.Commands.OpenSettings()));
        menu.Add(("Quit", () => _shell.Commands.Quit()));
        _window.SetTabMenu(menu);

        LoadSettings();
        Refresh();
        RefreshPanels();
        _tracker.Start();

        if (Application.Current is { } app)
            app.Exit += (_, _) => Dispose();

#if DEBUG
        var args = Environment.GetCommandLineArgs();
        int sheet = Array.FindIndex(args, a => string.Equals(a, "--pet-sheet", StringComparison.OrdinalIgnoreCase));
        if (_shell.DebugMode && sheet >= 0 && sheet + 1 < args.Length)
            PetSheet.Save(args[sheet + 1]);
        int motion = Array.FindIndex(args, a => string.Equals(a, "--pet-motion", StringComparison.OrdinalIgnoreCase));
        if (_shell.DebugMode && motion >= 0 && motion + 1 < args.Length)
            _ = SaveMotionAsync(args[motion + 1]);
        int shots = Array.FindIndex(args, a => string.Equals(a, "--island-shots", StringComparison.OrdinalIgnoreCase));
        if (_shell.DebugMode && shots >= 0 && shots + 1 < args.Length)
            _ = SaveIslandShotsAsync(args[shots + 1]);
        int pages = Array.FindIndex(args, a => string.Equals(a, "--island-pages", StringComparison.OrdinalIgnoreCase));
        if (_shell.DebugMode && pages >= 0 && pages + 1 < args.Length)
            _ = SaveIslandPagesAsync(args[pages + 1]);
#endif
        _logger.LogInformation("Island started");
    }

#if DEBUG
    private async Task SaveMotionAsync(string directory)
    {
        try
        {
            await PetSheet.SaveMotionAsync(directory);
            _logger.LogInformation("Pet motion sheets saved to {Directory}", directory);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Pet motion sheets failed");
        }
    }

    /// <summary>
    /// Debug builds (<c>--island-shots &lt;dir&gt;</c>): the closed tab on Top and Right at rest and with
    /// a fake media activity live, each also swollen as if hovered, then the tab docked on Top, Left and Right with the island open, each scene in
    /// both app themes (the island must look the same) with Follow Windows, Blue (the mockup's
    /// default) and Violet. Then drops the tab on the taskbar's edge
    /// and in the corners, which must be refused, and once on the top edge, which must be taken
    /// (r5-drop-check.txt). Use a CHIPPI_DEV_PROFILE: it saves the island settings it moves through,
    /// then puts them back.
    /// </summary>
    private async Task SaveIslandShotsAsync(string directory)
    {
        var original = _shell.Config.GetSection<AppSettings>(AppSettings.SectionName);
        try
        {
            Directory.CreateDirectory(directory);
            var debug = _shell.Island.CreatePublisher(DebugModuleId);
            var settle = TimeSpan.FromMilliseconds(900);
            await _shell.ModulesReady; // their tiles and pages exist once they run
            var media = new IslandActivity("media", IslandActivityKind.Media, "Weekend Mix", "Spotify") { Progress = 0.38 };
            async Task SaveSwellAsync(string name, System.Windows.Media.Color color)
            {
                _window.ForceHover(true);
                await Task.Delay(settle);
                _window.SaveScene(Path.Combine(directory, name.ToLowerInvariant() + ".png"), color);
                _window.ForceHover(false);
                await Task.Delay(settle);
            }
            foreach (var accent in new[] { AccentChoice.FollowWindows, AccentChoice.Blue, AccentChoice.Violet })
            {
                UpdateSettings(s => s.Accent = accent);
                foreach (var (theme, backdrop) in new[] { (ThemePreference.Dark, "#202020"), (ThemePreference.Light, "#F3F3F3") })
                {
                    Chippi.UI.Themes.ThemeManager.Apply(theme);
                    var color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(backdrop);
                    foreach (var edge in new[] { IslandEdge.Top, IslandEdge.Right })
                    {
                        UpdateSettings(s => (s.IslandEdge, s.IslandPlace) = (edge, 0.5));
                        _shell.Island.ClearModule(DebugModuleId);
                        await Task.Delay(settle);
                        _window.SaveScene(Path.Combine(directory, $"r5-island-rest-{edge}-{theme}-{accent}".ToLowerInvariant() + ".png"), color);
                        await SaveSwellAsync($"r5-island-rest-swell-{edge}-{theme}-{accent}", color);
                        debug.Publish(media);
                        await Task.Delay(settle);
                        _window.SaveScene(Path.Combine(directory, $"r5-island-live-{edge}-{theme}-{accent}".ToLowerInvariant() + ".png"), color);
                        await SaveSwellAsync($"r5-island-live-swell-{edge}-{theme}-{accent}", color);
                    }
                    foreach (var edge in new[] { IslandEdge.Top, IslandEdge.Left, IslandEdge.Right })
                    {
                        UpdateSettings(s => (s.IslandEdge, s.IslandPlace) = (edge, 0.5));
                        await Task.Delay(settle);
                        _window.ForceOpen(true);
                        await Task.Delay(settle);
                        string name = $"r5-island-{edge}-{theme}-{accent}".ToLowerInvariant();
                        _window.SaveScene(Path.Combine(directory, name + ".png"), color);
                        _window.ForceOpen(false);
                    }
                }
            }
            _shell.Island.ClearModule(DebugModuleId);
            UpdateSettings(s => (s.IslandEdge, s.IslandPlace) = (IslandEdge.Top, 0.5));
            await Task.Delay(settle);
            await File.WriteAllLinesAsync(Path.Combine(directory, "r5-drop-check.txt"), await DropCheckAsync());
            _logger.LogInformation("Island snapshots saved to {Directory}", directory);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Island snapshots failed");
        }
        finally
        {
            UpdateSettings(s =>
            {
                (s.Accent, s.Theme, s.IslandEdge, s.IslandPlace) = (original.Accent, original.Theme, original.IslandEdge, original.IslandPlace);
                (s.IslandDisplay, s.IslandMonitorId) = (original.IslandDisplay, original.IslandMonitorId);
            });
            Chippi.UI.Themes.ThemeManager.Apply(original.Theme);
        }
    }

    /// <summary>
    /// Debug builds (<c>--island-pages &lt;dir&gt;</c>): with a fake media activity live, the island open on
    /// Top, Left and Right, on the grid and on every page, plus a page-to-page morph caught midway,
    /// then with the island off the same pages docked by the tray (s6-*.png). Use a
    /// CHIPPI_DEV_PROFILE: the island settings it moves through are put back.
    /// </summary>
    private async Task SaveIslandPagesAsync(string directory)
    {
        var original = _shell.Config.GetSection<AppSettings>(AppSettings.SectionName);
        var debug = _shell.Island.CreatePublisher(DebugModuleId);
        try
        {
            Directory.CreateDirectory(directory);
            var settle = TimeSpan.FromMilliseconds(900);
            await _shell.ModulesReady; // their tiles and pages exist once they run
            var color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#202020");
            debug.Publish(new IslandActivity("media", IslandActivityKind.Media, "Weekend Mix", "Spotify"));
            foreach (var edge in new[] { IslandEdge.Top, IslandEdge.Left, IslandEdge.Right })
            {
                UpdateSettings(s => (s.IslandEdge, s.IslandPlace, s.IslandLastPage) = (edge, 0.5, null));
                await Task.Delay(settle);
                _window.ForceOpen(true);
                await Task.Delay(settle);
                _window.SaveScene(Path.Combine(directory, $"s6-{edge}-grid".ToLowerInvariant() + ".png"), color);
                foreach (var key in _window.Panel.PageKeys())
                {
                    await KeepOpenAsync(settle);
                    _window.Panel.ShowPage(key);
                    await Task.Delay(settle);
                    _window.SaveScene(Path.Combine(directory, $"s6-{edge}-{key}".ToLowerInvariant() + ".png"), color);
                }
                _window.Panel.ShowPage(null, remember: false);
                await Task.Delay(settle);
                _window.SaveScene(Path.Combine(directory, $"s6-{edge}-grid-back".ToLowerInvariant() + ".png"), color);
                if (edge == IslandEdge.Top)
                {
                    _window.Panel.ShowPage(IslandPanelWindow.NowPlayingKey);
                    await Task.Delay(110); // half of the 220ms morph
                    _window.SaveScene(Path.Combine(directory, "s6-top-morph-midway.png"), color);
                    await Task.Delay(settle);
                }
                _window.ForceOpen(false);
                await Task.Delay(settle);
            }
            UpdateSettings(s => s.IslandEnabled = false);
            await Task.Delay(settle);
            _window.ShowPages(_shell.Commands.TrayAnchor(), keyboard: false);
            await Task.Delay(settle);
            _window.SaveScene(Path.Combine(directory, "s6-docked-grid.png"), color);
            foreach (var key in _window.Panel.PageKeys())
            {
                _window.Panel.ShowPage(key);
                await Task.Delay(settle);
                _window.SaveScene(Path.Combine(directory, $"s6-docked-{key}".ToLowerInvariant() + ".png"), color);
            }
            _window.TogglePages(null, keyboard: false);
            _logger.LogInformation("Island page snapshots saved to {Directory}", directory);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Island page snapshots failed");
        }
        finally
        {
            _shell.Island.ClearModule(DebugModuleId);
            UpdateSettings(s => (s.IslandEdge, s.IslandPlace, s.IslandLastPage, s.IslandEnabled) = (original.IslandEdge, original.IslandPlace, original.IslandLastPage, original.IslandEnabled));
        }
    }

    /// <summary>
    /// A click, Esc or a fullscreen window in front closes the island, as it should, even during a
    /// snapshot run; it is opened again (and logged) so the rest of the run still shows its pages.
    /// </summary>
    private async Task KeepOpenAsync(TimeSpan settle)
    {
        if (_window.IsIslandOpen)
            return;
        _logger.LogInformation("Island page snapshot: the island had closed (a click, Esc or a fullscreen window); opening it again");
        _window.ForceOpen(true);
        await Task.Delay(settle);
    }

    /// <summary>Drops the tab at points on its monitor through the real drop path and reports what was taken.</summary>
    private async Task<List<string>> DropCheckAsync()
    {
        var lines = new List<string>();
        if (_window.CurrentArea is not { } area)
            return ["No monitor"];
        var (m, w, s) = (area.Monitor, area.Work, area.Scale);
        var taskbar = IslandGeometry.TaskbarEdge(m, w);
        lines.Add($"Monitor {m}, work area {w}, scale {s}, taskbar edge {taskbar?.ToString() ?? "none (auto-hide)"}");
        var cases = new List<(string Name, Point Pointer, bool Expected)>
        {
            ("Bottom edge, center", new(w.Left + w.Width / 2, w.Bottom - 2), false),
            ("Top-left corner", new(w.Left + 4, w.Top + 4), false),
            ("Top-right corner", new(w.Right - 4, w.Top + 4), false),
            ("Top edge, 100 DIPs from the left end (inside the 160 margin)", new(w.Left + 100 * s, w.Top + 2), false),
            ("Left edge, 30 DIPs from the bottom end (inside the 64 margin)", new(w.Left + 2, w.Bottom - 30 * s), false),
            ("Right edge, 30 DIPs from the top end (inside the 64 margin)", new(w.Right - 2, w.Top + 30 * s), false),
        };
        if (taskbar is { } bar && bar != ScreenEdge.Bottom)
            cases.Add(("Taskbar edge, center", bar switch
            {
                ScreenEdge.Top => new(w.Left + w.Width / 2, w.Top + 2),
                ScreenEdge.Left => new(w.Left + 2, w.Top + w.Height / 2),
                _ => new(w.Right - 2, w.Top + w.Height / 2),
            }, false));
        cases.Add(("Top edge, center (control)", new(w.Left + w.Width / 2, w.Top + 2), taskbar != ScreenEdge.Top));
        foreach (var (name, pointer, expected) in cases)
        {
            bool taken = _window.DropAt(pointer);
            await Task.Delay(400);
            lines.Add($"{(taken == expected ? "PASS" : "FAIL")}  {name} at ({pointer.X:0},{pointer.Y:0}): {(taken ? "taken" : "refused")}");
        }
        return lines;
    }
#endif

    /// <summary>
    /// Now Playing reads Windows' media sessions; while one plays it is the island's media activity
    /// (the tab's equalizer, the live orbit button), opening Now Playing.
    /// </summary>
    private void StartMedia()
    {
        var media = new SystemMediaSessions();
        _media = media;
        var publisher = _shell.Island.CreatePublisher(IslandPanelWindow.NowPlayingKey);
        media.Changed += (_, _) =>
        {
            if (media.Current is { Playing: true } track)
                publisher.Publish(new IslandActivity("track", IslandActivityKind.Media, track.Title.Length > 0 ? track.Title : track.Source, track.Source)
                {
                    TileKey = IslandPanelWindow.NowPlayingKey,
                });
            else
                publisher.Clear("track");
        };
        _window.SetMedia(media);
        _ = media.StartAsync();
    }

    private void ScheduleRefresh()
    {
        // The hub raises on the publisher's thread; coalesce bursts into one UI update.
        if (_refreshPending)
            return;
        _refreshPending = true;
        _dispatcher.BeginInvoke(DispatcherPriority.Normal, () =>
        {
            _refreshPending = false;
            Refresh();
        });
    }

    private void Refresh()
    {
        var activities = _shell.Island.Activities;
        _window.SetActivities(activities, MoodFor(activities));
    }

    /// <summary>The running features' island tiles, in grid order.</summary>
    private void RefreshPanels() =>
        _window.SetTiles(IslandTileContribution.Group(_shell.Registry.Features.Where(f => f.IsRunning).SelectMany(f => f.IslandTiles)));

    private void LoadSettings()
    {
        var settings = _shell.Config.GetSection<AppSettings>(AppSettings.SectionName);
        _window.SetPetColor(settings.PetColor);
        _window.SetPlacement(settings.IslandEdge, settings.IslandPlace);
        _window.SetLastPage(settings.IslandLastPage);
        _window.SetLayout(IslandLayout.From(settings));
        _window.SetEnabled(settings.IslandEnabled);
        _tracker.SetPlacement(settings.IslandDisplay, settings.IslandMonitorId);
    }

    /// <summary>
    /// Saves where the tab was dropped. A drop on another display than the island's switches the
    /// display setting to that one monitor (DESIGN.md §5); otherwise the setting stays as it was.
    /// </summary>
    private void OnPlacementChosen(object? sender, IslandPlacementEventArgs e)
    {
        string? monitorId = e.Monitor == _tracker.Monitor ? null : Displays.Connected().FirstOrDefault(d => d.Handle == e.Monitor)?.Id;
        UpdateSettings(s =>
        {
            s.IslandEdge = e.Edge;
            s.IslandPlace = e.Place;
            if (monitorId is not null)
            {
                s.IslandDisplay = IslandDisplay.SpecificMonitor;
                s.IslandMonitorId = monitorId;
            }
        });
        _logger.LogInformation("Island docked on {Edge} at {Place:0.###}{Display}", e.Edge, e.Place, monitorId is null ? "" : " on another display (now One monitor)");
    }

    private void OnSectionChanged(object? sender, ConfigSectionChangedEventArgs e)
    {
        if (e.Name != AppSettings.SectionName)
            return;
        _dispatcher.BeginInvoke(() =>
        {
            if (!_disposed)
                LoadSettings();
        });
    }

    private IEnumerable<(string Label, Action Action)?> DebugMenu()
    {
        var debug = _shell.Island.CreatePublisher(DebugModuleId);
        // Publishes as Keep awake's module id, so the wide-awake mood can be seen without starting a
        // session. Uses its own key, so it never touches a real Keep awake activity.
        var keepAwake = _shell.Island.CreatePublisher(KeepAwakeModuleId);
        return
        [
            ("Add media", () => debug.Publish(new IslandActivity("media", IslandActivityKind.Media, "Weekend Mix", "Spotify") { Progress = 0.38 })),
            ("Add timer", () => debug.Publish(new IslandActivity("timer", IslandActivityKind.Timer, "Focus timer", "18:42") { Progress = 0.4 })),
            ("Add stash", () => debug.Publish(new IslandActivity("stash", IslandActivityKind.Stash, "invoice-october.pdf", "Documents"))),
            ("Add agent", () => debug.Publish(new IslandActivity("agent", IslandActivityKind.Agent, "Claude Code", "62% of 5h · resets 4:10 PM") { Progress = 0.62 })),
            ("Add status", () => debug.Publish(new IslandActivity("status", IslandActivityKind.Status, "Cleaning mode", "Keyboard paused"))),
            ("Add Keep awake status", () => keepAwake.Publish(new IslandActivity(DebugWideAwakeKey, IslandActivityKind.Status, "Keep awake", "Until 6:00 PM"))),
            null,
            ("Celebrate", () => debug.ReportSuccess("Debug celebration")),
            ("Clear all", () =>
            {
                _shell.Island.ClearModule(DebugModuleId);
                keepAwake.Clear(DebugWideAwakeKey);
            }),
            // Change settings the way Settings › Island & pet does (ConfigStore.SetSection), to exercise the live re-read.
            ("Next pet color", () => UpdateSettings(s => s.PetColor = (PetColor)(((int)s.PetColor + 1) % Enum.GetValues<PetColor>().Length))),
            ("Hide island for 3s", () =>
            {
                UpdateSettings(s => s.IslandEnabled = false);
                var restore = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
                restore.Tick += (_, _) =>
                {
                    restore.Stop();
                    UpdateSettings(s => s.IslandEnabled = true);
                };
                restore.Start();
            }),
        ];
    }

    private void UpdateSettings(Action<AppSettings> change)
    {
        var settings = _shell.Config.GetSection<AppSettings>(AppSettings.SectionName);
        change(settings);
        try
        {
            _shell.Config.SetSection(AppSettings.SectionName, settings);
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "Debug settings change not saved");
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _shell.Config.SectionChanged -= OnSectionChanged;
        _media?.Dispose();
        _tracker.Dispose();
        _window.Close();
    }
}
