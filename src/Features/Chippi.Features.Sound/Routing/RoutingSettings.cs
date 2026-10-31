// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.Audio;
using Chippi.Features.Sound.Mute;

namespace Chippi.Features.Sound.Routing;

public sealed record RoutingList
{
    public bool Enabled { get; init; } = true;
    public bool Calls { get; init; } = true;
    public bool Seeded { get; init; }
    public IReadOnlyList<PriorityEntry> Devices { get; init; } = [];
    public IReadOnlyList<PriorityEntry> Removed { get; init; } = [];

    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(Devices);
        ArgumentNullException.ThrowIfNull(Removed);
        foreach (var list in new[] { Devices, Removed })
        {
            if (list.Any(e => e is null || string.IsNullOrWhiteSpace(e.Id) || string.IsNullOrWhiteSpace(e.Name)) ||
                list.Select(e => e.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() != list.Count)
                throw new ArgumentException("Invalid priority list");
        }
    }

    public RoutingList Refresh(IReadOnlyList<AudioEndpoint> known, string? currentDefault)
    {
        var devices = Seeded ? PriorityPolicy.Repair(Devices, known) : PriorityPolicy.Seed(known, currentDefault);
        devices = devices.Select(entry => known.FirstOrDefault(e => string.Equals(e.Id, entry.Id, StringComparison.OrdinalIgnoreCase))
            is { } endpoint ? entry with { Name = endpoint.Name } : entry).ToList();
        var removed = PriorityPolicy.Repair(Removed, known);
        devices = PriorityPolicy.AppendNew(devices, known, removed.Select(e => e.Id).ToHashSet(StringComparer.OrdinalIgnoreCase));
        return Seeded && Devices.SequenceEqual(devices) && Removed.SequenceEqual(removed)
            ? this : this with { Seeded = true, Devices = devices, Removed = removed };
    }
}

public sealed record RoutingSettings
{
    public RoutingList Output { get; init; } = new();
    public RoutingList Microphone { get; init; } = new();

    /// <summary>Switches to the next connected output; not set until the user records one.</summary>
    public MicShortcut? SwitchShortcut { get; init; }

    public RoutingList For(AudioFlow flow) => flow == AudioFlow.Render ? Output : Microphone;
    public RoutingSettings With(AudioFlow flow, RoutingList list) =>
        flow == AudioFlow.Render ? this with { Output = list } : this with { Microphone = list };

    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(Output);
        ArgumentNullException.ThrowIfNull(Microphone);
        Output.Validate();
        Microphone.Validate();
    }
}