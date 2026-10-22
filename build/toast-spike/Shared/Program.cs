// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.IO;
using System.Windows.Threading;
using Chippi.Core.Notifications;
using Velopack;

namespace Chippi.ToastSpike;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        // Uninstall runs cleanup without starting a dispatcher or showing a toast.
        VelopackApp.Build().OnBeforeUninstallFastCallback(_ =>
        {
            try { Presenter.Uninstall(); }
            catch (Exception ex) { Console.Error.WriteLine(ex); }
        }).Run();
        if (args.SequenceEqual(["--check"])) { Payload.Check(); return 0; }
        if (args.SequenceEqual(["--unregister"])) { Presenter.Uninstall(); return 0; }
        if (!args.Contains("--register-and-show") && !args.Contains("--toast-server") &&
            !args.Any(a => a.StartsWith("----AppNotificationActivated:", StringComparison.Ordinal)))
        {
            Console.WriteLine("No registration performed. --check is safe; --register-and-show, --toast-server and --unregister mutate Windows registration and require approval.");
            return 0;
        }
        var dispatcher = Dispatcher.CurrentDispatcher;
        using var presenter = new Presenter(argument => dispatcher.BeginInvoke(() =>
        {
            // Integration will pass decoded arguments to ShellContext.HandleActivation on the UI thread.
            Console.WriteLine($"Activation: {argument}");
            var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ChippiToastSpikes", typeof(Program).Assembly.GetName().Name!);
            Directory.CreateDirectory(folder);
            File.AppendAllText(Path.Combine(folder, "activation.log"), $"{DateTimeOffset.UtcNow:O} pid={Environment.ProcessId} argument={argument}{Environment.NewLine}");
        }));
        presenter.Register();
        if (args.Contains("--register-and-show"))
            presenter.Show("downloads", new Notice("held", "Ready to review")
            {
                Body = "1 file", Activation = "--downloads-review spike",
                Actions = [new("Review", "--downloads-review spike"), new("History", "--downloads-history spike")],
            });
        var timeout = new DispatcherTimer { Interval = TimeSpan.FromSeconds(60) };
        timeout.Tick += (_, _) => dispatcher.InvokeShutdown();
        timeout.Start();
        Dispatcher.Run();
        return 0;
    }
}
