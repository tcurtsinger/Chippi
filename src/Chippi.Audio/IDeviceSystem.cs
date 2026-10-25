// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

namespace Chippi.Audio;

/// <summary>
/// Everything the audio service asks of Windows. <see cref="CoreAudioDeviceSystem"/> is the real
/// one; tests use a fake, so no policy test touches a real device. Created, called and disposed on
/// the engine thread only. <see cref="Changed"/> may be raised on any thread.
/// </summary>
public interface IDeviceSystem : IDisposable
{
    event Action<DeviceChange>? Changed;

    /// <summary>Endpoints of a flow that are active, disabled or unplugged (not ones that are gone).</summary>
    IReadOnlyList<AudioEndpoint> GetEndpoints(AudioFlow flow);

    /// <summary>The default endpoint ID for a role, or null when the flow has no active endpoint.</summary>
    string? GetDefault(AudioFlow flow, AudioRole role);

    void SetDefault(string endpointId, AudioRole role);

    EndpointLevel GetLevel(string endpointId);

    void SetVolume(string endpointId, float volume);

    void SetMute(string endpointId, bool muted);

    float GetPeak(string endpointId);

    /// <summary>Sessions on one active render endpoint, expired ones included.</summary>
    IReadOnlyList<AudioSessionInfo> GetSessions(string renderEndpointId);

    void SetSessionVolume(string sessionId, float volume);

    void SetSessionMute(string sessionId, bool muted);

    float GetSessionPeak(string sessionId);
}
