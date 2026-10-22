// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.Core.SystemIntegration;
using Velopack;

namespace Chippi.App;

/// <summary>
/// Process entry. Velopack must run before any WPF type is created: its install, update and
/// uninstall hooks launch this exe with special arguments and exit inside <c>Run()</c>.
/// </summary>
public static class Program
{
    [STAThread]
    public static void Main()
    {
        // Before Velopack too: the processes it starts can arrive without %windir%.
        App.EnsureWindowsDirectoryEnvironment();

        VelopackApp.Build()
            // Never leave a Run entry pointing at a deleted exe. Installed builds use the real
            // value name; dev profiles never install.
            .OnBeforeUninstallFastCallback(_ =>
            {
                // A registry failure must never block the uninstall itself.
                try { new StartupRegistration().SetEnabled(false); }
                catch (Exception) { }
                try { new ExplorerRegistration().SetEnabled(false); } catch (Exception) { }
            })
            .Run();

        var app = new App();
        app.InitializeComponent();
        app.Run();
    }
}
