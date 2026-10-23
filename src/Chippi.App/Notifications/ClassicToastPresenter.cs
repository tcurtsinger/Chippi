// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using Chippi.Core.Diagnostics;
using Chippi.Core.Notifications;
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;

namespace Chippi.App.Notifications;

internal sealed class ClassicToastPresenter : IToastPresenter, IDisposable
{
    private readonly Dispatcher _dispatcher;
    private readonly ToastIdentity _identity;
    private readonly Queue<string[]> _waiting = new();
    private readonly ClassFactory _factory;
    private Action<string[]>? _route;
    private uint _cookie;
    private bool _disposed;
    internal event Action? Received;
    internal event Action? Unavailable;

    private ClassicToastPresenter(ToastIdentity identity, Dispatcher dispatcher)
    {
        _identity = identity;
        _dispatcher = dispatcher;
        new ToastRegistration(identity).Apply();
        Marshal.ThrowExceptionForHR(ToastNative.SetCurrentProcessExplicitAppUserModelID(identity.AppId));
        _factory = new ClassFactory(identity.AppId, encoded =>
        {
            if (!ToastActivation.TryDecode(encoded, out _, out var arguments)) return;
            _dispatcher.BeginInvoke(() =>
            {
                if (_disposed) return;
                if (_route is { } route) route(arguments);
                else if (_waiting.Count < 64) _waiting.Enqueue(arguments);
                Received?.Invoke();
            });
        });
        var id = identity.Activator;
        Marshal.ThrowExceptionForHR(ToastNative.CoRegisterClassObject(ref id, _factory, 4, 1, out _cookie));
    }

    internal static ClassicToastPresenter? TryCreate(string? profile, Dispatcher dispatcher)
    {
        try
        {
            var identity = ToastIdentity.Create(profile, Environment.ProcessPath ?? throw new InvalidOperationException("Executable missing"),
                Environment.GetFolderPath(Environment.SpecialFolder.Programs));
            return new ClassicToastPresenter(identity, dispatcher);
        }
        catch (Exception ex) { Log.Warn("Windows notifications unavailable", ex); return null; }
    }

    internal bool TryTake(out string[] arguments) => _waiting.TryDequeue(out arguments!);

    internal void RouteTo(Action<string[]> route)
    {
        _route = route;
        while (_waiting.TryDequeue(out var arguments)) route(arguments);
    }

    public void Show(string moduleId, Notice notice)
    {
        try
        {
            string xml = ToastPayload.Build(moduleId, notice);
            if (_dispatcher.CheckAccess()) Deliver(xml);
            else _dispatcher.BeginInvoke(() => Deliver(xml));
        }
        catch (Exception ex) { Log.Warn("Windows notification payload could not be built", ex); }
    }

    private void Deliver(string payload)
    {
        if (_disposed) return;
        try
        {
            var xml = new XmlDocument(); xml.LoadXml(payload);
            ToastNotificationManager.CreateToastNotifier(_identity.AppId).Show(new ToastNotification(xml));
        }
        catch (Exception ex)
        {
            Log.Warn("Windows notifications unavailable", ex);
            Unavailable?.Invoke();
        }
    }

    internal static bool Uninstall(string? profile = null)
    {
        try
        {
            var identity = ToastIdentity.Create(profile, Environment.ProcessPath ?? throw new InvalidOperationException("Executable missing"),
                Environment.GetFolderPath(Environment.SpecialFolder.Programs));
            try { ToastNotificationManager.History.Clear(identity.AppId); }
            catch (Exception ex) { Log.Warn("Notification history cleanup failed", ex); }
            new ToastRegistration(identity).Remove();
            return true;
        }
        catch (Exception ex) { Log.Warn("Notification registration cleanup failed", ex); return false; }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_cookie != 0)
        {
            try { Marshal.ThrowExceptionForHR(ToastNative.CoRevokeClassObject(_cookie)); }
            catch (Exception ex) { Log.Warn("Notification COM server revoke failed", ex); }
            _cookie = 0;
        }
        _waiting.Clear();
        GC.KeepAlive(_factory);
    }
}