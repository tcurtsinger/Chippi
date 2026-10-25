// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Diagnostics;
using System.Runtime.InteropServices;
using Chippi.Audio.Interop;
using Microsoft.Extensions.Logging;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace Chippi.Audio;

/// <summary>
/// <see cref="IDeviceSystem"/> over Windows Core Audio (NAudio), from Hopper's AudioDeviceWatcher
/// and DefaultDeviceController. Active endpoints and sessions stay open between calls so their
/// volume and session callbacks keep arriving; every callback only raises <see cref="Changed"/>.
/// Must be created, used and disposed on one MTA thread (the engine's).
/// </summary>
public sealed class CoreAudioDeviceSystem : IDeviceSystem, IMMNotificationClient
{
    // PKEY_Device_DeviceDesc (2) and PKEY_Device_FriendlyName (14) share a format ID; the adapter
    // name is PKEY_DeviceInterface_FriendlyName. Other property changes don't affect routing.
    private static readonly Guid DeviceNameFormat = new("a45c254e-df1c-4efd-8020-67d146a850e0");
    private static readonly Guid AdapterNameFormat = new("026e516e-b814-414b-83cd-856d6fef4822");

    private readonly ILogger _logger;
    private readonly MMDeviceEnumerator _enumerator = new();
    private readonly Dictionary<string, MMDevice> _active = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, TrackedSession> _sessions = new(StringComparer.OrdinalIgnoreCase);
    private bool _disposed;

    public CoreAudioDeviceSystem(ILogger logger)
    {
        _logger = logger;
        _enumerator.RegisterEndpointNotificationCallback(this);
    }

    public event Action<DeviceChange>? Changed;

    public IReadOnlyList<AudioEndpoint> GetEndpoints(AudioFlow flow)
    {
        var list = new List<AudioEndpoint>();
        var states = DeviceState.Active | DeviceState.Disabled | DeviceState.Unplugged;
        foreach (var device in _enumerator.EnumerateAudioEndPoints(ToDataFlow(flow), states))
        {
            var id = device.ID;
            var state = (EndpointState)device.State;
            list.Add(new AudioEndpoint(id, Read(() => device.FriendlyName) ?? id, flow, state, Read(() => device.DeviceFriendlyName) ?? ""));

            if (state == EndpointState.Active && !_active.ContainsKey(id))
                Track(id, device);
            else
                device.Dispose();
        }

        var activeIds = list.Where(e => e.IsActive).Select(e => e.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var (id, device) in _active.Where(a => a.Value.DataFlow == ToDataFlow(flow) && !activeIds.Contains(a.Key)).ToList())
            Untrack(id, device);
        return list;
    }

    public string? GetDefault(AudioFlow flow, AudioRole role)
    {
        try
        {
            if (!_enumerator.HasDefaultAudioEndpoint(ToDataFlow(flow), (Role)role))
                return null;
            using var device = _enumerator.GetDefaultAudioEndpoint(ToDataFlow(flow), (Role)role);
            return device.ID;
        }
        catch (COMException)
        {
            return null; // no active endpoint for this flow and role
        }
    }

    public void SetDefault(string endpointId, AudioRole role)
    {
        var client = new PolicyConfigClient();
        try
        {
            var hr = ((IPolicyConfig)client).SetDefaultEndpoint(endpointId, role);
            if (hr != 0)
                Marshal.ThrowExceptionForHR(hr);
        }
        finally
        {
            Marshal.ReleaseComObject(client);
        }
    }

    public EndpointLevel GetLevel(string endpointId)
    {
        var volume = Device(endpointId).AudioEndpointVolume;
        return new EndpointLevel(volume.MasterVolumeLevelScalar, volume.Mute);
    }

    public void SetVolume(string endpointId, float volume) => Device(endpointId).AudioEndpointVolume.MasterVolumeLevelScalar = volume;

    public void SetMute(string endpointId, bool muted) => Device(endpointId).AudioEndpointVolume.Mute = muted;

    public float GetPeak(string endpointId) => Device(endpointId).AudioMeterInformation.MasterPeakValue;

    public IReadOnlyList<AudioSessionInfo> GetSessions(string renderEndpointId)
    {
        var manager = Device(renderEndpointId).AudioSessionManager;
        manager.RefreshSessions();
        var all = manager.Sessions;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var list = new List<AudioSessionInfo>();
        for (var i = 0; i < all.Count; i++)
        {
            var control = all[i];
            var sessionId = control.GetSessionInstanceIdentifier;
            seen.Add(sessionId);
            if (!_sessions.TryGetValue(sessionId, out var tracked))
                _sessions[sessionId] = tracked = TrackSession(renderEndpointId, sessionId, control);
            list.Add(Describe(tracked));
        }

        foreach (var (id, tracked) in _sessions.Where(s => PriorityPolicy.Eq(s.Value.EndpointId, renderEndpointId) && !seen.Contains(s.Key)).ToList())
            UntrackSession(id, tracked);
        return list;
    }

    public void SetSessionVolume(string sessionId, float volume) => Session(sessionId).SimpleAudioVolume.Volume = volume;

    public void SetSessionMute(string sessionId, bool muted) => Session(sessionId).SimpleAudioVolume.Mute = muted;

    public float GetSessionPeak(string sessionId) => Session(sessionId).AudioMeterInformation.MasterPeakValue;

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        try { _enumerator.UnregisterEndpointNotificationCallback(this); }
        catch (COMException ex) { _logger.LogDebug(ex, "Unregistering endpoint notifications failed"); }
        foreach (var (id, tracked) in _sessions.ToList())
            UntrackSession(id, tracked);
        foreach (var (id, device) in _active.ToList())
            Untrack(id, device);
        _enumerator.Dispose();
    }

    // IMMNotificationClient, called on a COM thread.
    void IMMNotificationClient.OnDeviceStateChanged(string deviceId, DeviceState newState) => Raise(DeviceChange.Devices);

    void IMMNotificationClient.OnDeviceAdded(string pwstrDeviceId) => Raise(DeviceChange.Devices);

    void IMMNotificationClient.OnDeviceRemoved(string deviceId) => Raise(DeviceChange.Devices);

    void IMMNotificationClient.OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId) => Raise(DeviceChange.Defaults);

    void IMMNotificationClient.OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key)
    {
        if ((key.formatId == DeviceNameFormat && key.propertyId is 2 or 14) || (key.formatId == AdapterNameFormat && key.propertyId == 2))
            Raise(DeviceChange.Devices);
    }

    private void Raise(DeviceChange change) => Changed?.Invoke(change);

    private MMDevice Device(string id) =>
        _active.TryGetValue(id, out var device) ? device : throw new InvalidOperationException($"Endpoint {id} isn't active.");

    private AudioSessionControl Session(string id) =>
        _sessions.TryGetValue(id, out var tracked) ? tracked.Control : throw new InvalidOperationException($"Session {id} is gone.");

    private void Track(string id, MMDevice device)
    {
        _active[id] = device;
        try
        {
            device.AudioEndpointVolume.OnVolumeNotification += OnVolumeNotification;
            if (device.DataFlow == DataFlow.Render)
                device.AudioSessionManager.OnSessionCreated += OnSessionCreated;
        }
        catch (COMException ex)
        {
            _logger.LogDebug(ex, "Subscribing to endpoint {Id} failed", id);
        }
    }

    private void Untrack(string id, MMDevice device)
    {
        _active.Remove(id);
        foreach (var (sessionId, tracked) in _sessions.Where(s => PriorityPolicy.Eq(s.Value.EndpointId, id)).ToList())
            UntrackSession(sessionId, tracked);
        try
        {
            device.AudioEndpointVolume.OnVolumeNotification -= OnVolumeNotification;
            if (device.DataFlow == DataFlow.Render)
                device.AudioSessionManager.OnSessionCreated -= OnSessionCreated;
        }
        catch (COMException) { } // the endpoint is already gone
        device.Dispose();
    }

    private TrackedSession TrackSession(string endpointId, string sessionId, AudioSessionControl control)
    {
        var processId = (int)control.GetProcessID;
        var exePath = control.IsSystemSoundsSession ? null : ProcessPaths.ExePath(processId);
        var events = new SessionEvents(this);
        try { control.RegisterEventClient(events); }
        catch (COMException ex) { _logger.LogDebug(ex, "Subscribing to session {Id} failed", sessionId); }
        return new TrackedSession(endpointId, control, events, processId, exePath);
    }

    private void UntrackSession(string id, TrackedSession tracked)
    {
        _sessions.Remove(id);
        try
        {
            tracked.Control.UnRegisterEventClient(tracked.Events);
            tracked.Control.Dispose();
        }
        catch (COMException) { } // the session is already gone
    }

    private static AudioSessionInfo Describe(TrackedSession tracked)
    {
        var control = tracked.Control;
        var state = control.State switch
        {
            AudioSessionState.AudioSessionStateActive => SessionState.Active,
            AudioSessionState.AudioSessionStateExpired => SessionState.Expired,
            _ => SessionState.Inactive,
        };
        var volume = control.SimpleAudioVolume;
        return new AudioSessionInfo(
            control.GetSessionInstanceIdentifier,
            tracked.EndpointId,
            tracked.ProcessId,
            tracked.ExePath,
            AppName(control, tracked.ExePath),
            control.IsSystemSoundsSession,
            state,
            volume.Volume,
            volume.Mute);
    }

    // The session's own name, unless it's empty or an unresolved resource reference ("@%SystemRoot%\..."),
    // then the exe's description, then its file name.
    private static string AppName(AudioSessionControl control, string? exePath)
    {
        var name = Read(() => control.DisplayName);
        if (!string.IsNullOrWhiteSpace(name) && !name.StartsWith('@'))
            return name;
        if (exePath is null)
            return "App";
        var description = Read(() => FileVersionInfo.GetVersionInfo(exePath).FileDescription);
        return string.IsNullOrWhiteSpace(description) ? Path.GetFileNameWithoutExtension(exePath) : description;
    }

    private static string? Read(Func<string?> read)
    {
        try { return read(); }
        catch (Exception ex) when (ex is COMException or IOException or UnauthorizedAccessException or ArgumentException) { return null; }
    }

    private static DataFlow ToDataFlow(AudioFlow flow) => flow == AudioFlow.Render ? DataFlow.Render : DataFlow.Capture;

    private void OnVolumeNotification(AudioVolumeNotificationData data) => Raise(DeviceChange.Levels);

    private void OnSessionCreated(object sender, IAudioSessionControl newSession) => Raise(DeviceChange.Sessions);

    private sealed record TrackedSession(string EndpointId, AudioSessionControl Control, SessionEvents Events, int ProcessId, string? ExePath);

    private sealed class SessionEvents(CoreAudioDeviceSystem owner) : IAudioSessionEventsHandler
    {
        public void OnVolumeChanged(float volume, bool isMuted) => owner.Raise(DeviceChange.Levels);

        public void OnDisplayNameChanged(string displayName) => owner.Raise(DeviceChange.Sessions);

        public void OnIconPathChanged(string iconPath) { }

        public void OnChannelVolumeChanged(uint channelCount, IntPtr newVolumes, uint channelIndex) { }

        public void OnGroupingParamChanged(ref Guid groupingId) { }

        public void OnStateChanged(AudioSessionState state) => owner.Raise(DeviceChange.Sessions);

        public void OnSessionDisconnected(AudioSessionDisconnectReason disconnectReason) => owner.Raise(DeviceChange.Sessions);
    }
}
