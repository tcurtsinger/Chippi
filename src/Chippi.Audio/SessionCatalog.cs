// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

namespace Chippi.Audio;

/// <summary>
/// Render sessions on every active output, grouped by app (<see cref="AppKey"/>). Chippi's own
/// sessions (Mic processing's render into the cable) are left out, so the mixer can never mute
/// the processed mic. Engine thread only.
/// </summary>
internal sealed class SessionCatalog(int ownProcessId)
{
    private IReadOnlyList<AudioSessionInfo> _sessions = [];

    public IReadOnlyList<AppAudio> Apps { get; private set; } = [];

    public void Refresh(IDeviceSystem system, IReadOnlyList<AudioEndpoint> activeRender)
    {
        _sessions = activeRender
            .SelectMany(e => system.GetSessions(e.Id))
            .Where(s => s.State != SessionState.Expired && s.ProcessId != ownProcessId)
            .ToList();
        Apps = Group(_sessions);
    }

    public IEnumerable<AudioSessionInfo> SessionsOf(AppKey app) => _sessions.Where(s => KeyOf(s) == app);

    public static AppKey KeyOf(AudioSessionInfo s) =>
        s.IsSystemSounds ? AppKey.SystemSounds
        : !string.IsNullOrEmpty(s.ExePath) ? AppKey.ForExe(s.ExePath)
        : AppKey.ForName(s.AppName);

    /// <summary>One entry per app, in first-seen order. An app reads as muted only when every session is.</summary>
    public static IReadOnlyList<AppAudio> Group(IReadOnlyList<AudioSessionInfo> sessions) =>
        sessions
            .GroupBy(KeyOf)
            .Select(g =>
            {
                var lead = g.FirstOrDefault(s => s.State == SessionState.Active) ?? g.First();
                return new AppAudio(
                    g.Key,
                    g.First().IsSystemSounds ? "System sounds" : lead.AppName,
                    lead.ExePath,
                    lead.Volume,
                    g.All(s => s.Muted),
                    g.Any(s => s.State == SessionState.Active),
                    g.Select(s => s.EndpointId).Distinct(StringComparer.OrdinalIgnoreCase).ToList()) { ProcessIds = g.Select(s => s.ProcessId).Distinct().ToList() };
            })
            .ToList();
}
