// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.Core.Diagnostics;
using System.IO;
using Chippi.Core.SystemIntegration;
using Velopack;

namespace Chippi.App;

/// <summary>
/// Process entry. Velopack must run before any WPF type is created: its install, update and
/// uninstall hooks launch this exe with special arguments and exit inside <c>Run()</c>.
/// </summary>
public static class Program
{
    // Notification activation and test cleanup can log before App.OnStartup.
    internal static void ConfigureLog(string[] arguments)
    {
        var profile = ExplorerRegistration.EnvironmentProfile();
        try { profile = ExplorerRegistration.ResolveDevProfile(arguments); }
        catch (ArgumentException) { } // App.OnStartup reports malformed arguments and keeps the environment profile.
        Log.Default = new RollingFileLog(string.IsNullOrWhiteSpace(profile) ? RollingFileLog.DefaultDirectory : Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Chippi-dev", profile, "logs"));
    }
    [STAThread]
    public static int Main()
    {
        try { ConfigureLog(Environment.GetCommandLineArgs()[1..]); }
        catch (ArgumentException ex) { Console.Error.WriteLine(ex.Message); return 1; }

        // Before Velopack too: the processes it starts can arrive without %windir%.
        App.EnsureWindowsDirectoryEnvironment();

        VelopackApp.Build()
            // Never leave a Run entry pointing at a deleted exe. Installed builds use the real
            // value name; dev profiles never install.
            .OnBeforeUninstallFastCallback(_ =>
            {
                // A registry failure must never block the uninstall itself; it's only logged.
                try { new StartupRegistration().SetEnabled(false); }
                catch (Exception ex) { Log.Warn("Uninstall: removing the Run entry failed", ex); }
                try { new ExplorerRegistration().SetEnabled(false); }
                catch (Exception ex) { Log.Warn("Uninstall: removing the Explorer entry failed", ex); }
                Notifications.ClassicToastPresenter.Uninstall();
                // A crash can leave CABLE Output as the default mic; uninstall puts the real one back.
                // VB-CABLE itself is never removed.
                try { Chippi.Features.Sound.Processing.MicProcessingModule.RestoreOnUninstall(System.IO.Path.Combine(Chippi.Core.Modules.ModuleServices.DefaultDataRoot, Chippi.Features.Sound.Processing.MicProcessingModule.Id)); }
                catch (Exception ex) { Log.Warn("Uninstall: restoring the default mic failed", ex); }
            })
            .Run();

        var startup = Notifications.ToastStartup.Prepare(Environment.GetCommandLineArgs()[1..]);
        if (startup.Exit) return startup.ExitCode;
        var app = new App { NotificationStartup = startup };
        app.InitializeComponent();
        app.Run();
        return 0;
    }
}
