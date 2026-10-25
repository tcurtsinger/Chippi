// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.Audio.Processing;

namespace Chippi.Audio;

/// <summary>
/// Everything the views show about audio at one moment, built on the engine thread after each
/// operation. Immutable: views bind to it and never read COM objects. Meters are separate
/// (<see cref="MeterReadings"/>).
/// </summary>
public sealed record AudioSnapshot(
    long Version,
    IReadOnlyList<AudioEndpoint> Endpoints,
    IReadOnlyDictionary<RoleKey, string> Defaults,
    IReadOnlyDictionary<RoleKey, RoleOwner> Owners,
    IReadOnlyDictionary<string, EndpointLevel> Levels,
    IReadOnlyList<AppAudio> Apps)
{
    /// <summary>Where Mic processing actually is.</summary>
    public ProcessingStatus Processing { get; init; } = ProcessingStatus.Off;

    public static AudioSnapshot Empty { get; } = new(
        0, [], new Dictionary<RoleKey, string>(), new Dictionary<RoleKey, RoleOwner>(),
        new Dictionary<string, EndpointLevel>(StringComparer.OrdinalIgnoreCase), []);

    /// <summary>Endpoints of a flow that Windows lists, active or not.</summary>
    public IReadOnlyList<AudioEndpoint> Known(AudioFlow flow) => Endpoints.Where(e => e.Flow == flow).ToList();

    public IReadOnlyList<AudioEndpoint> Active(AudioFlow flow) => Endpoints.Where(e => e.Flow == flow && e.IsActive).ToList();

    public string? Default(AudioFlow flow, AudioRole role) => Defaults.GetValueOrDefault(new RoleKey(flow, role));

    public RoleOwner Owner(AudioFlow flow, AudioRole role) => Owners.GetValueOrDefault(new RoleKey(flow, role));

    public AudioEndpoint? Find(string id) => Endpoints.FirstOrDefault(e => PriorityPolicy.Eq(e.Id, id));
}
