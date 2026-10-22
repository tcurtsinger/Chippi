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

    /// <summary>Another launch asked this instance to show itself (WS1 opens the flyout). Raised on the UI thread.</summary>
    public event EventHandler? ActivationRequested;

    public static new App Current => (App)Application.Current;

    public ILoggerFactory LoggerFactory { get; private set; } = null!;

    public ConfigStore Config { get; private set; } = null!;

    /// <summary>True when launched by the Run-at-startup entry: start quietly in the tray.</summary>
    public bool StartedAtLogin { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        EnsureWindowsDirectoryEnvironment();
        Velopack.VelopackApp.Build().Run();
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
        string? devProfile = Environment.GetEnvironmentVariable("CHIPPI_DEV_PROFILE");
        string? devConfigPath = null;
        if (!string.IsNullOrWhiteSpace(devProfile))
        {
            Log.Default = new RollingFileLog(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Chippi-dev", devProfile, "logs"));
            devConfigPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Chippi-dev", devProfile, "settings.json");
        }

        // One instance per session. A second launch signals the first (which opens the flyout) and exits.
        _instance = string.IsNullOrWhiteSpace(devProfile) ? SingleInstance.TryAcquire() : SingleInstance.TryAcquire($"Chippi-{devProfile}");
        if (_instance is null)
        {
            Log.Info("Chippi is already running; asked it to open the flyout");
            Shutdown();
            return;
        }
        _instance.ActivationRequested += (_, _) =>
            Dispatcher.BeginInvoke(() => ActivationRequested?.Invoke(this, EventArgs.Empty));

        StartedAtLogin = e.Args.Any(a => string.Equals(a, StartupRegistration.StartupArgument, StringComparison.OrdinalIgnoreCase));
        Log.Info($"Starting Chippi {typeof(App).Assembly.GetName().Version} ({System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture})");

        LoggerFactory = Microsoft.Extensions.Logging.LoggerFactory.Create(b => b.AddProvider(new RollingFileLoggerProvider(Log.Default)));
        Config = new ConfigStore(devConfigPath, LoggerFactory.CreateLogger("Config"));
        Config.Load();

        var app = Config.GetSection<AppSettings>(AppSettings.SectionName);
        ThemeManager.Apply(app.Theme);
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

        bool debugMode = e.Args.Contains("--debug", StringComparer.OrdinalIgnoreCase)
            || Environment.GetEnvironmentVariable("CHIPPI_DEBUG") == "1";
        Shell = new ShellContext(Config, LoggerFactory, debugMode, devProfile);
        Shell.Commands.QuitRequested += async (_, _) => await QuitAsync();
        ActivationRequested += (_, _) => Shell.Commands.ShowFlyout();

        if (!ComposeShell(Shell))
        {
            // Nothing visible keeps the app reachable until the tray lands, so do not linger.
            Log.Info("No tray composed yet; exiting");
            Shutdown();
        }
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


        // [WS2] "Coming soon" placeholders for the PLAN.md features.

        // Start installed modules before any surface reads the registry.
        _ = StartModulesAsync(shell);

        // [WS3] Island: create the island host here.

        // [WS2] Settings: create the settings controller and call shell.Commands.RegisterSettings(...).

        // [WS1] Tray and flyout: create them, call shell.Commands.RegisterFlyout(...), then return true.

        return false;
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

    private static void EnsureWindowsDirectoryEnvironment()
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
        _instance?.Dispose();
        LoggerFactory?.Dispose();
        base.OnExit(e);
    }
}
