// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

namespace Chippi.Audio;

/// <summary>
/// The engine thread's view of endpoints and default roles, re-read from the device system on
/// every reconcile. Hopper's AudioDeviceWatcher, minus the notifications (the service owns those).
/// </summary>
internal sealed class DeviceCatalog
{
    private static readonly AudioRole[] Roles = [AudioRole.Console, AudioRole.Multimedia, AudioRole.Communications];

    private readonly Dictionary<AudioFlow, IReadOnlyList<AudioEndpoint>> _endpoints = new()
    {
        [AudioFlow.Render] = [],
        [AudioFlow.Capture] = [],
    };
    private readonly Dictionary<RoleKey, string> _defaults = [];

    public IReadOnlyList<AudioEndpoint> Endpoints(AudioFlow flow) => _endpoints[flow];

    public IReadOnlyList<AudioEndpoint> Active(AudioFlow flow) => _endpoints[flow].Where(e => e.IsActive).ToList();

    public IReadOnlyDictionary<RoleKey, string> Defaults => _defaults;

    public string? Default(RoleKey key) => _defaults.GetValueOrDefault(key);

    public void Refresh(IDeviceSystem system)
    {
        foreach (var flow in (AudioFlow[])[AudioFlow.Render, AudioFlow.Capture])
            _endpoints[flow] = system.GetEndpoints(flow);
        RefreshDefaults(system);
    }

    public void RefreshDefaults(IDeviceSystem system)
    {
        _defaults.Clear();
        foreach (var flow in (AudioFlow[])[AudioFlow.Render, AudioFlow.Capture])
        {
            foreach (var role in Roles)
            {
                if (system.GetDefault(flow, role) is { } id)
                    _defaults[new RoleKey(flow, role)] = id;
            }
        }
    }
}
