// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.IO;
using System.Windows;
using System.Windows.Threading;
using Chippi.App.Island.Media;
using Chippi.UI.Pet;
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
    /// <summary>The Keep awake module's id; its Status activity makes ChippiPet wide awake.</summary>
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
        _shell.Commands.RegisterIslandHold(_window.HoldIslandOpen);
        // A second launch and a notice click open the island; with it off or hidden, ShellCommands opens Settings.
        _shell.Commands.RegisterPages(_window.ShowPages, _window.ShowPage);
        _window.IsVisibleChanged += (_, _) => _shell.Commands.SetIslandShowing(_window.IsVisible);
        StartMedia();
        _tracker.Changed += (_, _) =>
        {
            _window.SetMonitor(_tracker.Monitor, _tracker.ChosenMissing);
            _window.SetSuppressed(_tracker.FullscreenInFront);
        };
        _window.DisplayChanged += (_, _) => _tracker.OnDisplaysChanged();
        _window.PlacementChosen += OnPlacementChosen;

        _shell.Island.Changed += (_, _) => ScheduleRefresh();
        _shell.Registry.Changed += (_, _) => _dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            if (!_disposed)
                RefreshPanels();
        });
        _shell.Island.Succeeded += (_, _) => _dispatcher.BeginInvoke(() => _window.Celebrate());
        _shell.Config.SectionChanged += OnSectionChanged;

        // The puck's right click is the tray icon's menu, so Quit stays reachable with the icon hidden.
        var menu = new List<(string Label, Action Action)?>();
        if (_shell.DebugMode)
            menu.AddRange([.. DebugMenu(), null]);
        menu.Add(("Open settings", () => _shell.Commands.OpenSettings()));
        menu.Add(("Quit", () => _shell.Commands.Quit()));
        _window.SetMenu(menu);

        LoadSettings();
        Refresh();
        RefreshPanels();
        _tracker.Start();
        _ = PrewarmAsync();

        if (Application.Current is { } app)
            app.Exit += (_, _) => Dispose();

#if DEBUG
        var args = Environment.GetCommandLineArgs();
        int sheet = Array.FindIndex(args, a => string.Equals(a, "--pet-sheet", StringComparison.OrdinalIgnoreCase));
        if (_shell.DebugMode && sheet >= 0 && sheet + 1 < args.Length)
            Chippi.App.Pet.PetSheet.Save(args[sheet + 1]);
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

    /// <summary>Once the features run and the app is idle, lays the panel's home out so the first open is as quick as the rest.</summary>
    private async Task PrewarmAsync()
    {
        try
        {
            await _shell.ModulesReady;
            await _dispatcher.InvokeAsync(() =>
            {
                if (!_disposed)
                    _window.Prewarm();
            }, DispatcherPriority.ApplicationIdle);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Island prewarm failed");
        }
    }

#if DEBUG
    private async Task SaveMotionAsync(string directory)
    {
        try
        {
            await Chippi.App.Pet.PetSheet.SaveMotionAsync(directory);
            _logger.LogInformation("Pet motion sheets saved to {Directory}", directory);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Pet motion sheets failed");
        }
    }

    /// <summary>
    /// Debug builds (<c>--island-shots &lt;dir&gt;</c>): the widget where it starts (bottom right of
    /// its display), in the dark and the light theme: at rest, hovered, lifted as if dragged, the bar
    /// with a fake timer live and that bar becoming the panel (early in the morph, open, folding back),
    /// and the puck's panel open (r6-widget-&lt;theme&gt;-*.png). Then drops the puck
    /// through the real drop path in each corner of the work area, past every edge and in the middle,
    /// and checks it lands inside the work area with the panel inside too (r6-drop-check.txt). Use a
    /// CHIPPI_DEV_PROFILE: the place, display and theme it moves through are put back.
    /// </summary>
    private async Task SaveIslandShotsAsync(string directory)
    {
        var original = _shell.Config.GetSection<AppSettings>(AppSettings.SectionName);
        var debug = _shell.Island.CreatePublisher(DebugModuleId);
        try
        {
            Directory.CreateDirectory(directory);
            var settle = TimeSpan.FromMilliseconds(900);
            await _shell.ModulesReady; // their tiles and pages exist once they run
            UpdateSettings(s => (s.IslandX, s.IslandY) = (null, null));
            foreach (var (theme, backdrop) in new[] { (ThemePreference.Dark, "#202020"), (ThemePreference.Light, "#F3F3F3") })
            {
                Chippi.UI.Themes.ThemeManager.Apply(theme);
                var color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(backdrop);
                string prefix = $"r6-widget-{theme}".ToLowerInvariant();
                async Task SaveAsync(string name)
                {
                    await Task.Delay(settle);
                    _window.SaveScene(Path.Combine(directory, $"{prefix}-{name}.png"), color);
                }
                _shell.Island.ClearModule(DebugModuleId);
                await SaveAsync("rest");
                _window.ForceHover(true);
                await SaveAsync("hover");
                _window.ForceHover(false);
                _window.ForceLift(true);
                await SaveAsync("lifted");
                _window.ForceLift(false);
                debug.Publish(new IslandActivity("timer", IslandActivityKind.Timer, "18:42", "Focus timer") { Progress = 0.4 });
                await SaveAsync("timer");
                // The bar becomes the panel: caught early in the 220ms morph, then open, then early in the fold.
                _window.ForceOpen(true);
                await Task.Delay(70);
                _window.SaveScene(Path.Combine(directory, $"{prefix}-timer-opening.png"), color);
                await SaveAsync("timer-open");
                _window.ForceOpen(false);
                await Task.Delay(60);
                _window.SaveScene(Path.Combine(directory, $"{prefix}-timer-closing.png"), color);
                await Task.Delay(settle);
                _shell.Island.ClearModule(DebugModuleId);
                _window.ForceOpen(true);
                await SaveAsync("open");
                _window.ForceOpen(false);
                await Task.Delay(settle);
            }
            await File.WriteAllLinesAsync(Path.Combine(directory, "r6-drop-check.txt"), await DropCheckAsync());
            _logger.LogInformation("Island snapshots saved to {Directory}", directory);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Island snapshots failed");
        }
        finally
        {
            _shell.Island.ClearModule(DebugModuleId);
            UpdateSettings(s =>
            {
                (s.Theme, s.IslandX, s.IslandY) = (original.Theme, original.IslandX, original.IslandY);
                (s.IslandDisplay, s.IslandMonitorId) = (original.IslandDisplay, original.IslandMonitorId);
            });
            Chippi.UI.Themes.ThemeManager.Apply(original.Theme);
        }
    }
    /// <summary>
    /// Debug builds (<c>--island-pages &lt;dir&gt;</c>): with a fake media activity live, the panel open
    /// from the widget where it starts (bottom right), in the dark and the light theme, on the home and
    /// on every page, plus a page-to-page morph caught midway (s6-top-&lt;theme&gt;-*.png). Use a
    /// CHIPPI_DEV_PROFILE: the place and the theme it moves through are put back.
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
            debug.Publish(new IslandActivity("media", IslandActivityKind.Media, "Weekend Mix", "Spotify"));
            foreach (var (theme, backdrop) in new[] { (ThemePreference.Dark, "#202020"), (ThemePreference.Light, "#F3F3F3") })
            {
                Chippi.UI.Themes.ThemeManager.Apply(theme);
                var color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(backdrop);
                string prefix = $"s6-top-{theme}".ToLowerInvariant();
                UpdateSettings(s => (s.IslandX, s.IslandY, s.IslandLastPage) = (null, null, null));
                await Task.Delay(settle);
                _window.ForceOpen(true);
                await Task.Delay(settle);
                _window.SaveScene(Path.Combine(directory, $"{prefix}-grid.png"), color);
                foreach (var key in _window.Panel.PageKeys())
                {
                    await KeepOpenAsync(settle);
                    _window.Panel.ShowPage(key);
                    await Task.Delay(settle);
                    _window.SaveScene(Path.Combine(directory, $"{prefix}-{key}".ToLowerInvariant() + ".png"), color);
                }
                _window.Panel.ShowPage(null, remember: false);
                await Task.Delay(settle);
                _window.SaveScene(Path.Combine(directory, $"{prefix}-grid-back.png"), color);
                _window.Panel.ShowPage(IslandPanelWindow.NowPlayingKey);
                await Task.Delay(110); // half of the 220ms morph
                _window.SaveScene(Path.Combine(directory, $"{prefix}-morph-midway.png"), color);
                await Task.Delay(settle);
                _window.ForceOpen(false);
                await Task.Delay(settle);
            }
            _logger.LogInformation("Island page snapshots saved to {Directory}", directory);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Island page snapshots failed");
        }
        finally
        {
            _shell.Island.ClearModule(DebugModuleId);
            UpdateSettings(s => (s.IslandX, s.IslandY, s.IslandLastPage) = (original.IslandX, original.IslandY, original.IslandLastPage));
            Chippi.UI.Themes.ThemeManager.Apply(original.Theme);
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

    /// <summary>
    /// Drops the puck through the real drop path at points on its monitor (each corner of the work area,
    /// past every edge, the middle) and checks that it, and the panel beside it, land inside the work area.
    /// </summary>
    private async Task<List<string>> DropCheckAsync()
    {
        var lines = new List<string>();
        if (_window.CurrentArea is not { } area)
            return ["No monitor"];
        var (m, w, s) = (area.Monitor, area.Work, area.Scale);
        lines.Add($"Monitor {m}, work area {w}, scale {s}");
        double half = IslandGeometry.Puck * s / 2;
        var cases = new List<(string Name, Point Pointer)>
        {
            ("Top-left corner", new(w.Left + half, w.Top + half)),
            ("Top-right corner", new(w.Right - half, w.Top + half)),
            ("Bottom-left corner", new(w.Left + half, w.Bottom - half)),
            ("Bottom-right corner", new(w.Right - half, w.Bottom - half)),
            ("Past the top edge", new(w.Left + w.Width / 3, w.Top - 40 * s)),
            ("Past the left edge", new(w.Left - 40 * s, w.Top + w.Height / 3)),
            ("Past the right edge", new(w.Right + 40 * s, w.Top + w.Height / 3)),
            ("On the taskbar", new(w.Left + w.Width / 2, m.Bottom - 4)),
            ("Middle", new(w.Left + w.Width / 2, w.Top + w.Height / 2)),
        };
        foreach (var (name, pointer) in cases)
        {
            var puck = _window.DropAt(pointer);
            await Task.Delay(400);
            _window.ForceOpen(true);
            await Task.Delay(400);
            var panel = _window.Panel.Footprint;
            _window.ForceOpen(false);
            await Task.Delay(300);
            // A point past an edge can be on the display beside it: the puck goes there, inside that work area.
            var landed = (_window.CurrentArea ?? area).Work;
            bool inside = landed.Contains(puck) && landed.Contains(panel);
            lines.Add($"{(inside ? "PASS" : "FAIL")}  {name} at ({pointer.X:0},{pointer.Y:0}): work area {landed}, puck {puck}, panel {panel}");
        }
        return lines;
    }
#endif

    /// <summary>
    /// Now Playing reads Windows' media sessions; while one plays it is the island's media activity
    /// (the tab's equalizer), opening Now Playing.
    /// </summary>
    private void StartMedia()
    {
        var media = new SystemMediaSessions();
        _media = media;
        _shell.Media = media;
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
        _window.SetLevel(new TabLevel(Chippi.Features.Sound.SoundAudio.Get(_logger)));
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
        _window.SetPlacement(settings.IslandX, settings.IslandY);
        _window.SetLastPage(settings.IslandLastPage);
        _window.SetLayout(IslandLayout.From(settings));
        _window.SetEnabled(settings.IslandEnabled);
        _tracker.SetPlacement(settings.IslandDisplay, settings.IslandMonitorId);
    }

    /// <summary>
    /// Saves where the widget was dropped. A drop on another display than the widget's switches the
    /// display setting to that one monitor (DESIGN.md §5); otherwise the setting stays as it was.
    /// </summary>
    private void OnPlacementChosen(object? sender, IslandPlacementEventArgs e)
    {
        string? monitorId = e.Monitor == _tracker.Monitor && !_tracker.ChosenMissing ? null : Displays.Connected().FirstOrDefault(d => d.Handle == e.Monitor)?.Id;
        UpdateSettings(s =>
        {
            s.IslandX = e.X;
            s.IslandY = e.Y;
            if (monitorId is not null)
            {
                s.IslandDisplay = IslandDisplay.SpecificMonitor;
                s.IslandMonitorId = monitorId;
            }
        });
        _logger.LogInformation("Widget dropped at {X:0.###}, {Y:0.###}{Display}", e.X, e.Y, monitorId is null ? "" : " on another display (now One monitor)");
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
