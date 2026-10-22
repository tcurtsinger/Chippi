// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.IO;
using System.Windows;
using System.Windows.Threading;
using Chippi.App.Pet;
using Chippi.App.Shell;
using Chippi.Core.Config;
using Chippi.Core.Island;
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
        _tracker.Ignore = _window.Handle;
        _tracker.Changed += (_, _) =>
        {
            _window.SetMonitor(_tracker.Monitor);
            _window.SetSuppressed(_tracker.FullscreenInFront);
        };
        _window.DisplayChanged += (_, _) => _tracker.Evaluate();

        _shell.Island.Changed += (_, _) => ScheduleRefresh();
        _shell.Island.Succeeded += (_, _) => _dispatcher.BeginInvoke(() => _window.Celebrate());
        _shell.Config.SectionChanged += OnSectionChanged;

        if (_shell.DebugMode)
            _window.SetDebugMenu(DebugMenu());

        LoadSettings();
        Refresh();
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
    /// Debug builds (<c>--island-shots &lt;dir&gt;</c>): steps the island through its states with fake
    /// activities and saves the window's pixels, once with the app in the dark theme and once in
    /// light (the island must look the same), over the matching backdrop color.
    /// </summary>
    private async Task SaveIslandShotsAsync(string directory)
    {
        try
        {
            Directory.CreateDirectory(directory);
            var debug = _shell.Island.CreatePublisher(DebugModuleId);
            var keepAwake = _shell.Island.CreatePublisher(KeepAwakeModuleId);
            var original = _shell.Config.GetSection<AppSettings>(AppSettings.SectionName).Theme;
            var settle = TimeSpan.FromMilliseconds(900);
            await Task.Delay(1200);
            foreach (var (theme, backdrop) in new[] { (ThemePreference.Dark, "#0A0D18"), (ThemePreference.Light, "#E9EDF4") })
            {
                Chippi.UI.Themes.ThemeManager.Apply(theme);
                var color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(backdrop);
                string suffix = theme.ToString().ToLowerInvariant();
                void Shot(string name) => _window.SaveSnapshot(Path.Combine(directory, $"ws3-island-{name}-{suffix}.png"), color);

                _shell.Island.ClearModule(DebugModuleId);
                keepAwake.Clear(DebugWideAwakeKey);
                await Task.Delay(settle);
                Shot("resting");

                debug.Publish(new IslandActivity("media", IslandActivityKind.Media, "Weekend Mix", "Spotify") { Progress = 0.38 });
                await Task.Delay(settle);
                Shot("live");

                _shell.Island.ClearModule(DebugModuleId);
                keepAwake.Publish(new IslandActivity(DebugWideAwakeKey, IslandActivityKind.Status, "Keep awake", "Until 6:00 PM"));
                await Task.Delay(settle);
                Shot("live-keepawake");
                keepAwake.Clear(DebugWideAwakeKey);

                debug.Publish(new IslandActivity("media", IslandActivityKind.Media, "Weekend Mix", "Spotify") { Progress = 0.38 });
                debug.Publish(new IslandActivity("timer", IslandActivityKind.Timer, "Focus timer", "18:42") { Progress = 0.4 });
                debug.Publish(new IslandActivity("stash", IslandActivityKind.Stash, "invoice-october.pdf", "Documents"));
                debug.Publish(new IslandActivity("agent", IslandActivityKind.Agent, "Claude Code", "62% of 5h · resets 4:10 PM") { Progress = 0.62 });
                debug.Publish(new IslandActivity("status", IslandActivityKind.Status, "Cleaning mode", "Keyboard paused"));
                await Task.Delay(TimeSpan.FromMilliseconds(300));
                _window.ForceExpanded(true);
                await Task.Delay(settle);
                Shot("expanded");
                debug.ReportSuccess("Snapshot");
                await Task.Delay(TimeSpan.FromMilliseconds(500));
                Shot("expanded-celebrating");
                _window.ForceExpanded(false);
                _shell.Island.ClearModule(DebugModuleId);
                await Task.Delay(TimeSpan.FromSeconds(3));
            }
            Chippi.UI.Themes.ThemeManager.Apply(original);
            _logger.LogInformation("Island snapshots saved to {Directory}", directory);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Island snapshots failed");
        }
    }
#endif

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

    private void LoadSettings()
    {
        var settings = _shell.Config.GetSection<AppSettings>(AppSettings.SectionName);
        _window.SetPetColor(settings.PetColor);
        _window.SetEnabled(settings.IslandEnabled);
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
        // Publishes as Keep awake's module id so the wide-awake wiring can be seen before WS4 lands.
        // Uses its own key, so it never touches a real Keep awake activity.
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
            null,
            ("Quit Chippi", () => _shell.Commands.Quit()),
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
        _tracker.Dispose();
        _window.Close();
    }
}
