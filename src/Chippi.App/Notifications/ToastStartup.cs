// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows.Threading;
using Chippi.Core.Diagnostics;
using Chippi.Core.SystemIntegration;

namespace Chippi.App.Notifications;

/// <summary>COM activation must finish before WPF's ordinary single-instance path.</summary>
internal sealed class ToastStartup
{
    internal string[] Arguments { get; private init; } = [];
    internal SingleInstance? Owner { get; private init; }
    internal ClassicToastPresenter? Presenter { get; private init; }
    internal bool Exit { get; private init; }
    internal int ExitCode { get; private init; }

    internal static ToastStartup Prepare(string[] arguments)
    {
        bool server = arguments.Contains("--toast-server", StringComparer.Ordinal);
        bool cleanup = arguments.Contains("--cleanup-dev-notifications", StringComparer.Ordinal);
        if (!server && !cleanup) return new() { Arguments = arguments };
        ClassicToastPresenter? presenter = null;
        try
        {
            string? profile = ExplorerRegistration.ResolveDevProfile(arguments);
            if (cleanup)
            {
                // A test-cleanup command must never fall back to production identity.
                if (string.IsNullOrWhiteSpace(profile)) throw new ArgumentException("Dev profile required for notification test cleanup");
                return new() { Exit = true, ExitCode = ClassicToastPresenter.Uninstall(profile) ? 0 : 1 };
            }
            var dispatcher = Dispatcher.CurrentDispatcher;
            presenter = ClassicToastPresenter.TryCreate(profile, dispatcher);
            if (presenter is null) return new() { Exit = true, ExitCode = 1 };
            var frame = new DispatcherFrame();
            void Received() => frame.Continue = false;
            presenter.Received += Received;
            var timer = new DispatcherTimer(TimeSpan.FromSeconds(10), DispatcherPriority.Normal,
                (_, _) => frame.Continue = false, dispatcher);
            try { Dispatcher.PushFrame(frame); }
            finally { timer.Stop(); presenter.Received -= Received; }
            if (!presenter.TryTake(out var decoded))
            {
                presenter.Dispose();
                Log.Warn("Notification activation timed out");
                return new() { Exit = true, ExitCode = 1 };
            }
            var owner = SingleInstance.TryAcquire(string.IsNullOrWhiteSpace(profile) ? "Chippi" : $"Chippi-{profile}", decoded);
            if (owner is null)
            {
                presenter.Dispose();
                return new() { Exit = true };
            }
            return new() { Arguments = decoded, Owner = owner, Presenter = presenter };
        }
        catch (Exception ex)
        {
            presenter?.Dispose();
            Log.Warn("Notification activation could not be routed", ex);
            return new() { Exit = true, ExitCode = 1 };
        }
    }
}