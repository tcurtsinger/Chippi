// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.IO;
using System.Windows;
using System.Windows.Threading;
using Chippi.App.Shell;
using Chippi.UI.Themes;
using Chippi.Core.Config;
using Chippi.Core.Diagnostics;
using Chippi.Core.SystemIntegration;
using Microsoft.Extensions.Logging;

namespace Chippi.App;

/// <summary>
/// Process entry and startup hardening (ported from Hopper): unhandled-exception logging, the
/// %windir% fix, and single instance. WS0 stops at the foundation; the lead composes the tray
/// (WS1), settings (WS2), island (WS3) and modules (WS4) in <see cref="ComposeShell"/>.
/// </summary>
public partial class App : Application
{
    private Island.IslandHost? _islandHost;
    private SingleInstance? _instance;
    internal Notifications.ToastStartup? NotificationStartup { get; init; }
    private Notifications.ClassicToastPresenter? _toasts;

    /// <summary>Another launch handed this instance its arguments (a verb, or nothing: show the island's pages). Raised on the UI thread.</summary>
    public event EventHandler<ActivationEventArgs>? ActivationRequested;

    private Task _modulesStarted = Task.CompletedTask;

    public static new App Current => (App)Application.Current;

    public ILoggerFactory LoggerFactory { get; private set; } = null!;

    public ConfigStore Config { get; private set; } = null!;

    // True once the tray exists. Before then a failure ends the app; after, a transient UI exception doesn't.
    private bool _trayComposed;
    private bool _modulesStopAttempted;
    private bool _sessionEnding;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Log any unhandled failure so a silent crash is never a mystery, and keep the tray app
        // alive through a transient UI exception rather than vanishing. A failure while starting, before
        // the tray exists, ends the app instead: otherwise an invisible process keeps the single-instance
        // lock and every later launch hands itself to it.
        DispatcherUnhandledException += (_, args) =>
        {
            Log.Error("Dispatcher unhandled exception", args.Exception);
            args.Handled = true;
            if (ShellLifetime.ExitOnUnhandled(_trayComposed))
            {
                Log.Error("Chippi failed to start; exiting");
                Shutdown(1);
            }
        };
        SessionEnding += (_, _) => _sessionEnding = true;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Log.Error("AppDomain unhandled exception", args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log.Error("Unobserved task exception", args.Exception);
            args.SetObserved();
        };

        // Dev isolation: CHIPPI_DEV_PROFILE=ws1 gives a run its own instance, settings and logs, so
        // parallel workstreams can launch Chippi side by side without touching the real profile.
        // A dev profile can also arrive as --dev-profile <name> (a dev Explorer entry, which doesn't
        // inherit the test runner's environment). A malformed argument is ignored; a malformed
        // CHIPPI_DEV_PROFILE stops the launch, so a dev run never writes outside its folder or into
        // the real profile.
        string? devProfile;
        try
        {
            devProfile = ExplorerRegistration.EnvironmentProfile();
        }
        catch (ArgumentException ex)
        {
            Log.Error("Not starting: CHIPPI_DEV_PROFILE isn't a valid profile name", ex);
            Shutdown(1);
            return;
        }
        try
        {
            devProfile = ExplorerRegistration.ResolveDevProfile(e.Args);
        }
        catch (ArgumentException ex)
        {
            Log.Warn("Ignoring an invalid --dev-profile argument", ex);
        }
        string? devConfigPath = null;
        if (!string.IsNullOrWhiteSpace(devProfile))
        {
            Log.Default = new RollingFileLog(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Chippi-dev", devProfile, "logs"));
            devConfigPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Chippi-dev", devProfile, "settings.json");
        }

        // One instance per session. A second launch hands its arguments to the first and exits.
        _instance = NotificationStartup?.Owner ?? SingleInstance.TryAcquire(string.IsNullOrWhiteSpace(devProfile) ? "Chippi" : $"Chippi-{devProfile}", e.Args);
        if (_instance is null)
        {
            Log.Info("Chippi is already running; passed this launch to it");
            Shutdown();
            return;
        }
        _instance.ActivationRequested += (_, args) =>
            Dispatcher.BeginInvoke(() => ActivationRequested?.Invoke(this, args));

        Log.Info($"Starting Chippi {typeof(App).Assembly.GetName().Version} ({System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture})");

        bool debugMode = e.Args.Contains("--debug", StringComparer.OrdinalIgnoreCase)
            || Environment.GetEnvironmentVariable("CHIPPI_DEBUG") == "1";
        // --debug also writes the modules' LogDebug lines, which a normal run leaves out. Both the
        // factory and the provider filter, and the factory's default floor is Information.
        var level = debugMode ? LogLevel.Debug : LogLevel.Information;
        LoggerFactory = Microsoft.Extensions.Logging.LoggerFactory.Create(b => b
            .SetMinimumLevel(level)
            .AddProvider(new RollingFileLoggerProvider(Log.Default, level)));
        Config = new ConfigStore(devConfigPath, LoggerFactory.CreateLogger("Config"));
        Config.Load();

        var app = Config.GetSection<AppSettings>(AppSettings.SectionName);
        ThemeManager.Apply(app.Theme);
        new AccentSource(Config, Dispatcher).Apply();
        Motion.Override = app.ReduceMotion;

#if DEBUG
        if (e.Args.Contains("--gallery", StringComparer.OrdinalIgnoreCase))
        {
            var gallery = new Chippi.UI.Controls.Gallery.ControlGalleryWindow();
            gallery.Closed += (_, _) => Shutdown();
            gallery.Show();
            int snapshot = Array.FindIndex(e.Args, a => string.Equals(a, "--snapshot", StringComparison.OrdinalIgnoreCase));
            if (snapshot >= 0 && snapshot + 1 < e.Args.Length)
                gallery.ContentRendered += async (_, _) => await gallery.SnapshotAsync(e.Args[snapshot + 1]);
            return;
        }
#endif

        Shell = new ShellContext(Config, LoggerFactory, debugMode, devProfile);
        _toasts = NotificationStartup?.Presenter ?? Notifications.ClassicToastPresenter.TryCreate(devProfile, Dispatcher);
        Shell.Services.Toasts = _toasts;
        if (_toasts is not null)
        {
            _toasts.Unavailable += () => Shell.Services.Toasts = null;
            _toasts.RouteTo(arguments => Shell.HandleActivation(arguments));
        }
        Shell.Commands.QuitRequested += async (_, _) => await QuitAsync();
        ActivationRequested += (_, args) => Shell.HandleActivation(args.Arguments);

        if (!ComposeShell(Shell))
        {
            // Nothing visible keeps the app reachable until the tray lands, so do not linger.
            Log.Info("No tray composed yet; exiting");
            Shutdown();
            return;
        }
        _trayComposed = true;
        _ = RouteLaunchAsync(NotificationStartup?.Arguments ?? e.Args);
    }

    /// <summary>
    /// A first launch can carry a verb too (the Explorer menu when Chippi isn't running). Once the
    /// modules have started, the shell routes it and anything that arrived meanwhile.
    /// </summary>
    private async Task RouteLaunchAsync(string[] arguments)
    {
        await _modulesStarted;
        Shell.ModulesStarted(arguments);
        // A hold on Print Screen no shortcut uses any more puts Windows' setting back.
        Shell.Shortcuts.Started();
#if DEBUG
        // The capture layer's Sandbox check, before a capture feature exists (CHIPPI_CAPTURE_PROBE).
        // Its shortcuts keep it alive.
        Chippi.App.Shell.CaptureProbe.StartIfAsked(Shell);
#endif
    }

    public ShellContext Shell { get; private set; } = null!;

    /// <summary>
    /// Composition root, owned by the lead. Each workstream adds its wiring in its own slot below
    /// and nowhere else in this file, so merges stay one-line. Returns false while there is no tray
    /// to keep the app reachable.
    /// </summary>
    private bool ComposeShell(ShellContext shell)
    {
        // [WS4] Real modules.
        var keepAwake = new Chippi.Features.KeepAwake.KeepAwakeModule();
        shell.Registry.Register(keepAwake);
        Exit += (_, _) => keepAwake.ReleaseOnExit();
        shell.Registry.Register(new Chippi.Features.Downloads.DownloadsModule());
        // [Phase 3] Sound. Routing never lists VB-CABLE (recognized by device identity); Mic
        // processing's "Highest priority" source is routing's microphone order, a pick first.
        var routing = new Chippi.Features.Sound.Routing.AudioRoutingModule(null, Chippi.Audio.VbCable.IsCable);
        shell.Registry.Register(routing);
        shell.Registry.Register(new Chippi.Features.Sound.Processing.MicProcessingModule(null, () => routing.Order(Chippi.Audio.AudioFlow.Capture),
            Path.Combine(shell.Services.DataRoot, Chippi.Features.Sound.Processing.MicProcessingModule.Id)));

        shell.Registry.Register(new Chippi.Features.Sound.Volume.AppVolumeModule());
        shell.Registry.Register(new Chippi.Features.Sound.Soundboard.SoundboardModule(
            Path.Combine(shell.Services.DataRoot, Chippi.Features.Sound.Soundboard.SoundboardModule.Id),
            () => routing.Order(Chippi.Audio.AudioFlow.Capture)));
        // The Sound page's Microphone row sets the level of the mic routing has in use, never VB-CABLE's.
        shell.Registry.Register(new Chippi.Features.Sound.Mute.MicMuteModule(microphone: () => routing.InUse(Chippi.Audio.AudioFlow.Capture)));
        // [Phase 4] System (the System monitor until 2026-10-31): replaces its catalog placeholder.
        shell.Registry.Register(new Chippi.Features.SystemMonitor.SystemMonitorModule());
        shell.Registry.Register(new Chippi.Features.Scratchpad.ScratchpadModule());
        RegisterClipboardFeatures(shell);
        shell.Registry.Register(new Chippi.Features.Shelf.ShelfModule());
        // [Phase 5.5] Cleaning mode, and Quick actions, which offers it as Cleaning while it's installed.
        var cleaning = new Chippi.Features.CleaningMode.CleaningModeModule();
        shell.Registry.Register(cleaning);
        shell.Registry.Register(new Chippi.Features.QuickActions.QuickActionsModule(() => cleaning.Installed ? cleaning.Start : null));
        var commandBar = new Chippi.Features.CommandBar.CommandBarModule();
        shell.Registry.Register(commandBar);
        // [Phase 5.5] The Radial menu: closed during Cleaning mode, wide awake with Keep awake, searching where the Command bar does.
        var radial = new Chippi.Features.RadialMenu.RadialMenuModule(() => cleaning.IsOn, () => keepAwake.IsOn, () => commandBar.Settings.WebSearch);
        shell.Registry.Register(radial);
        // [Phase 5.5] AI agent usage: replaces its catalog placeholder.
        shell.Registry.Register(new Chippi.Features.AgentUsage.AgentUsageModule());
        // [Phase 6] Click and key debounce: replaces its catalog placeholder.
        shell.Registry.Register(new Chippi.Features.Debounce.DebounceModule(Environment.GetEnvironmentVariable("CHIPPI_DEBOUNCE_INJECTED") == "1"));
        // [Phase 6] Smooth and linear scrolling: replaces its catalog placeholder.
        shell.Registry.Register(new Chippi.Features.SmoothScrolling.SmoothScrollingModule(Environment.GetEnvironmentVariable("CHIPPI_SMOOTH_INJECTED") == "1"));
        // [Phase 6] Shortcuts: replaces the Mouse buttons, Super key and Shortcut manager placeholders. Actions run on the shell's
        // runner; the Sandbox probe's host takes its place when CHIPPI_SHORTCUTS_PROBE names a file.
        var shortcutProbe = Environment.GetEnvironmentVariable("CHIPPI_SHORTCUTS_PROBE");
        shell.Registry.Register(shell.ShortcutsFeature = new Chippi.Features.Shortcuts.ShortcutsModule(
            () => string.IsNullOrEmpty(shortcutProbe) ? null : new Chippi.Features.Shortcuts.ProbeActionHost(shortcutProbe),
            radialHolds: button => shell.Registry.Features.Any(f => f.Id == Chippi.Features.RadialMenu.RadialMenuModule.Id && f.IsRunning)
                && button switch
                {
                    Chippi.Features.Shortcuts.MouseTrigger.Middle => radial.Settings.HoldMiddle,
                    Chippi.Features.Shortcuts.MouseTrigger.Back => radial.Settings.HoldBack,
                    Chippi.Features.Shortcuts.MouseTrigger.Forward => radial.Settings.HoldForward,
                    _ => false,
                },
            searchUrl: text => Chippi.Features.CommandBar.WebEngines.SearchUrl(commandBar.Settings.WebSearch, text)));

        // [Phase 6] Text snippets: replaces its catalog placeholder.
        shell.Registry.Register(new Chippi.Features.TextSnippets.TextSnippetsModule());

        shell.Registry.Register(new Chippi.Features.CloseProtection.CloseProtectionModule(() =>
            new Chippi.Features.CloseProtection.WindowsCloseDesktop()));

        // [Phase 6] App switcher: replaces its catalog placeholder. Its tile, Search entry and Radial slices fire app-switcher/open.
        shell.Registry.Register(new Chippi.Features.AppSwitcher.AppSwitcherModule());

        // Native tracking: constructed lazily, acquired only when installed and switched on.
        shell.Registry.Register(new Chippi.Features.FocusFollowsMouse.FocusFollowsMouseModule(() =>
        {
            // A dev run must neither change user-wide tracking nor recover the owner's journal.
            if (!string.Equals(shell.Services.DataRoot, Chippi.Core.Modules.ModuleServices.DefaultDataRoot, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Native focus tracking unavailable in dev profiles");
            return new Chippi.Features.FocusFollowsMouse.FocusTrackingEngine(
                new Chippi.Features.FocusFollowsMouse.WindowsTrackingSettings(),
                new Chippi.Features.FocusFollowsMouse.TrackingJournal(Path.Combine(Chippi.Core.Modules.ModuleServices.DefaultDataRoot,
                    Chippi.Features.FocusFollowsMouse.FocusFollowsMouseModule.Id, "ownership.json")));
        }));

        // [Phase 7] Displays and brightness: replaces its catalog placeholder.
        shell.Registry.Register(new Chippi.Features.Displays.DisplaysModule());


        var windowLayouts = new Chippi.Features.WindowLayouts.WindowLayoutsModule(SnapDisplaySource.Connected);
        windowLayouts.PointerDisplayId = SnapDisplaySource.PointerId;
        windowLayouts.CreateArrangementDesktop = context => new ArrangementDesktop(context.Actions!,
            () => shell.Config.GetSection<AppSettings>(AppSettings.SectionName).PauseRules(), () => windowLayouts.ArrangementsLocked);
        shell.Registry.Register(windowLayouts);
        // [Phase 7] Text and QR: on the capture layer.
        var screenText = new Chippi.Features.ScreenText.ScreenTextModule(shell.Capture);
        shell.Registry.Register(screenText);
        var screenCapture = new Chippi.Features.ScreenCapture.ScreenCaptureModule(shell.Capture)
        {
            TextAvailable = () => shell.Registry.Find("screen-text")?.IsRunning == true,
            ReadText = (image, qr) => screenText.ReadSelectionAsync(image, qr ? Chippi.Features.ScreenText.ScreenTextVerb.ScanQr : Chippi.Features.ScreenText.ScreenTextVerb.CopyText),
        };
        shell.Registry.Register(screenCapture);
        shell.Registry.Changed += (_, _) => Dispatcher.BeginInvoke(screenCapture.RefreshProviders);
        // [Phase 7] Color picker: on the capture layer.
        var colorPicker = new Chippi.Features.ColorPicker.ColorPickerModule(shell.Capture);
        shell.Registry.Register(colorPicker);
        var recording = shell.Recording = new Chippi.Features.Recording.RecordingModule();
        recording.CanStart = () => shell.Capture.Current == null;
        shell.Registry.Register(recording);
        var mediaTools = new Chippi.Features.MediaTools.MediaToolsModule();
        shell.Registry.Register(mediaTools);
        shell.AppUpdates = new Chippi.Features.AppUpdates.AppUpdatesModule();
        shell.Registry.Register(shell.AppUpdates);
        if (shell.Capture is Chippi.UI.Capture.CaptureLayer layer)
            layer.CanStart = () => recording.Model?.Active != true;
        shell.CapturePanel = new Chippi.App.Island.CapturePanelHost(shell, screenCapture, screenText, colorPicker, recording, mediaTools);
        if (shell.Services.Actions is Chippi.UI.Actions.WindowsActionPc windowPc)
        {
            windowPc.SnapZones = windowLayouts.ResolveZone;
            windowPc.SnapFailed = target => windowLayouts.ReportFailure(target.App);
        }
        windowLayouts.CreateOverlay = () => new Chippi.Features.WindowLayouts.Views.ZoneOverlay();

        // [WS2] "Coming soon" placeholders for the PLAN.md features.
        Settings.Features.FeatureCatalog.RegisterPlaceholders(shell.Registry);

        // Start installed modules before any surface reads the registry.
        _modulesStarted = StartModulesAsync(shell);

        // [WS3] Island: create the island host here.
        (_islandHost = new Island.IslandHost(shell)).Start();

        // [WS2] Settings: create the settings controller and call shell.Commands.RegisterSettings(...).
        Settings.SettingsController.Compose(shell);

        // [WS1] Tray: create it (it registers itself with shell.Commands), then return true.
        return Tray.TrayController.Compose(shell);
    }

    /// <summary>Registration also sets the Clipboard &amp; text Settings order (refresh Part E).</summary>
    internal static void RegisterClipboardFeatures(ShellContext shell)
    {
        // [Phase 5] Clipboard history and Clean URL share the Clipboard tile.
        var clipboard = new Chippi.Features.Clipboard.ClipboardFeatures();
        shell.Registry.Register(new Chippi.Features.Clipboard.ClipboardHistoryModule(clipboard,
            System.IO.Path.Combine(shell.Services.DataRoot, Chippi.Features.Clipboard.ClipboardHistoryModule.Id)));
        shell.Registry.Register(new Chippi.Features.PlainPaste.PlainPasteModule());
        shell.Registry.Register(new Chippi.Features.Clipboard.CleanUrlModule(clipboard));
    }

    private static async Task StartModulesAsync(ShellContext shell)
    {
        try
        {
            await shell.Registry.StartAsync();
        }
        catch (Exception ex)
        {
            Log.Error("Starting modules failed", ex);
        }
    }

    private bool _quitting;

    /// <summary>Stops every module (releasing what they hold, such as Keep awake's execution state), then exits.</summary>
    private async Task QuitAsync()
    {
        if (_quitting)
        {
            // A second Quit while the first waits on a slow module: stop waiting.
            Shutdown();
            return;
        }
        if (Shell.Recording?.Model?.Active == true && System.Windows.MessageBox.Show("Stop and save the recording, then quit Chippi?", "Chippi", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        if (Shell.AppUpdates?.Engine?.IsRunning == true && System.Windows.MessageBox.Show("Stop updating and quit?", "Chippi", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        _quitting = true;
        // Stopping isn't removing: a Print Screen shortcut keeps Windows' setting off for the next start.
        Shell.Shortcuts.Closing();
        _modulesStopAttempted = true;
        await ShellLifetime.StopWithinAsync(Shell.Registry.StopAllAsync, ShellLifetime.QuitLimit);
        Shutdown();
    }

    internal static void EnsureWindowsDirectoryEnvironment()
    {
        // Some launchers (scheduled tasks, a few installers) start processes without %windir%,
        // which breaks WPF's font and theme lookups. Restore it before anything touches them.
        var windowsDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (string.IsNullOrWhiteSpace(windowsDirectory) || !Directory.Exists(windowsDirectory))
            windowsDirectory = @"C:\Windows";

        Environment.SetEnvironmentVariable("windir", windowsDirectory);
        Environment.SetEnvironmentVariable("WINDIR", windowsDirectory);
    }

    /// <summary>
    /// Velopack starts Chippi in its install folder, and every program Chippi starts without a working folder of its own
    /// inherits it. One that outlives Chippi (a restarted Explorer, an app opened from it) keeps the folder open, and
    /// Setup can't replace it (2026-11-17). Chippi resolves its own files from <see cref="AppContext.BaseDirectory"/>.
    /// A run started anywhere else (a terminal, <c>dotnet run</c>) keeps its folder, so relative arguments such as
    /// <c>--settings-snapshot shots</c> still land there.
    /// </summary>
    internal static void LeaveInstallFolder()
    {
        try
        {
            if (InInstallFolder(Environment.CurrentDirectory, AppContext.BaseDirectory))
                Environment.CurrentDirectory = NeutralWorkingFolder();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            Log.Warn("Startup: leaving the install folder failed", ex);
        }
    }

    /// <summary>Whether <paramref name="workingFolder"/> is the exe's folder's parent (Velopack's root) or inside it.</summary>
    internal static bool InInstallFolder(string workingFolder, string baseDirectory)
    {
        var exeFolder = Path.TrimEndingDirectorySeparator(Path.GetFullPath(baseDirectory));
        var root = Path.GetDirectoryName(exeFolder) ?? exeFolder;
        var folder = Path.TrimEndingDirectorySeparator(Path.GetFullPath(workingFolder));
        return folder.Equals(root, StringComparison.OrdinalIgnoreCase)
            || folder.StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The user's profile, as Actions use for the commands they run; System32 when it's missing.</summary>
    internal static string NeutralWorkingFolder()
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return !string.IsNullOrEmpty(profile) && Directory.Exists(profile) ? profile : Environment.SystemDirectory;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // Logoff, shutdown and the other ways out skip Quit: stop the modules here so what they changed
        // on the PC (mic routing, execution state) is put back.
        if (!_modulesStopAttempted && _trayComposed && Shell is not null)
        {
            _modulesStopAttempted = true;
            Shell.Shortcuts.Closing();
            ShellLifetime.StopWithin(Dispatcher, Shell.Registry.StopAllAsync, _sessionEnding ? ShellLifetime.SessionEndLimit : ShellLifetime.QuitLimit);
        }
        _islandHost?.Dispose();
        _toasts?.Dispose();
        Shell?.Clipboard.Dispose();
        Shell?.Input.Dispose();
        Shell?.Admin.Dispose();
        Shell?.CapturePanel?.Dispose();
        Shell?.Hotkeys.Dispose();
        (Shell?.Services.Actions as IDisposable)?.Dispose();
        _instance?.Dispose();
        LoggerFactory?.Dispose();
        base.OnExit(e);
    }
}
