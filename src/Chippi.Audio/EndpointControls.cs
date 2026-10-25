// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Microsoft.Extensions.Logging;

namespace Chippi.Audio;

/// <summary>Endpoint volume and mute for render and capture endpoints. Engine thread only.</summary>
internal sealed class EndpointControls(ILogger logger)
{
    private Dictionary<string, EndpointLevel> _levels = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, EndpointLevel> Levels => _levels;

    /// <summary>Re-reads every active endpoint. One that vanishes mid-read is skipped until the next refresh.</summary>
    public void Refresh(IDeviceSystem system, IEnumerable<AudioEndpoint> active)
    {
        var levels = new Dictionary<string, EndpointLevel>(StringComparer.OrdinalIgnoreCase);
        foreach (var endpoint in active)
        {
            try { levels[endpoint.Id] = system.GetLevel(endpoint.Id); }
            catch (Exception ex) { logger.LogDebug(ex, "Reading level of {Id} failed", endpoint.Id); }
        }
        _levels = levels;
    }

    public void SetVolume(IDeviceSystem system, string id, float volume) =>
        system.SetVolume(id, Math.Clamp(volume, 0f, 1f));

    /// <summary>
    /// Sets mute on each endpoint and returns the IDs that failed, so a caller never reports
    /// "all muted" after a partial failure (behavior spec, Mic mute shortcut).
    /// </summary>
    public IReadOnlyList<string> SetMute(IDeviceSystem system, IReadOnlyCollection<string> ids, bool muted)
    {
        var failed = new List<string>();
        foreach (var id in ids)
        {
            try { system.SetMute(id, muted); }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Setting mute={Muted} on {Id} failed", muted, id);
                failed.Add(id);
            }
        }
        return failed;
    }
}
