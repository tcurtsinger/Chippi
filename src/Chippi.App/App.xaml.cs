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
    private SingleInstance? _instance;
    internal Notifications.ToastStartup? NotificationStartup { get; init; }
    private Notifications.ClassicToastPresenter? _toasts;

    /// <summary>Another launch handed this instance its arguments (a verb, or nothing: show the island's pages). Raised on the UI thread.</summary>
    public event EventHandler<ActivationEventArgs>? ActivationRequested;

    private Task _modulesStarted = Task.CompletedTask;

    public static new App Current => (App)Application.Current;

    public ILoggerFactory LoggerFactory { get; private set; } = null!;

    public ConfigStore Config { get; private set; } = null!;

    /// <summary>True when launched by the Run-at-startup entry: start quietly in the tray.</summary>
    public bool StartedAtLogin { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Log any unhandled failure so a silent crash is never a mystery, and keep the tray app
        // alive through a transient UI exception rather than vanishing.
        DispatcherUnhandledException += (_, args) =>
        {
            Log.Error("Dispatcher unhandled exception", args.Exception);
            args.Handled = true;
        };
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

        StartedAtLogin = e.Args.Any(a => string.Equals(a, StartupRegistration.StartupArgument, StringComparison.OrdinalIgnoreCase));
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
        // The Sound page's Microphone row sets the level of the mic routing has in use, never VB-CABLE's.
        shell.Registry.Register(new Chippi.Features.Sound.Mute.MicMuteModule(microphone: () => routing.InUse(Chippi.Audio.AudioFlow.Capture)));
        // [Phase 4] System monitor: replaces its catalog placeholder.
        shell.Registry.Register(new Chippi.Features.SystemMonitor.SystemMonitorModule());
        shell.Registry.Register(new Chippi.Features.Scratchpad.ScratchpadModule());
        RegisterClipboardFeatures(shell);
        shell.Registry.Register(new Chippi.Features.Shelf.ShelfModule());

        // [WS2] "Coming soon" placeholders for the PLAN.md features.
        Settings.Features.FeatureCatalog.RegisterPlaceholders(shell.Registry);

        // Start installed modules before any surface reads the registry.
        _modulesStarted = StartModulesAsync(shell);

        // [WS3] Island: create the island host here.
        new Island.IslandHost(shell).Start();

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
            return;
        _quitting = true;
        try
        {
            await Shell.Registry.StopAllAsync();
        }
        catch (Exception ex)
        {
            Log.Error("Stopping modules on quit failed", ex);
        }
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

    protected override void OnExit(ExitEventArgs e)
    {
        _toasts?.Dispose();
        Shell?.Clipboard.Dispose();
        _instance?.Dispose();
        LoggerFactory?.Dispose();
        base.OnExit(e);
    }
}
