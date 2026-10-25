// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using static Chippi.Audio.Tests.FakeDeviceSystem;

namespace Chippi.Audio.Tests;

public class PriorityPolicyTests
{
    private static readonly IReadOnlyList<PriorityEntry> Abc = [new("A", "Headset"), new("B", "Speakers"), new("C", "Monitor")];

    private static AudioEndpoint Off(string id, string? name = null) => Speaker(id, name) with { State = EndpointState.Unplugged };

    [Fact]
    public void Highest_active_listed_endpoint_wins()
    {
        Assert.Equal("B", PriorityPolicy.Resolve(Abc, [Speaker("C"), Speaker("B")]));
        Assert.Equal("A", PriorityPolicy.Resolve(Abc, [Speaker("C"), Speaker("B"), Speaker("A")]));
    }

    [Fact]
    public void Falls_back_to_the_next_available_entry_not_the_former_default()
    {
        Assert.Equal("C", PriorityPolicy.Resolve(Abc, [Off("A"), Off("B"), Speaker("C")]));
    }

    [Fact]
    public void Nothing_listed_available_leaves_windows_alone()
    {
        Assert.Null(PriorityPolicy.Resolve(Abc, [Speaker("D")]));
        Assert.Null(PriorityPolicy.Resolve([], [Speaker("A")]));
    }

    [Fact]
    public void Ids_compare_case_insensitively()
    {
        Assert.Equal("B", PriorityPolicy.Resolve(Abc, [Speaker("b")]));
    }

    [Fact]
    public void Excluded_endpoints_are_skipped()
    {
        var exclude = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "a" };
        Assert.Equal("B", PriorityPolicy.Resolve(Abc, [Speaker("A"), Speaker("B")], exclude));
    }

    [Fact]
    public void Seed_lists_active_endpoints_with_the_default_first()
    {
        var seeded = PriorityPolicy.Seed([Speaker("A"), Off("X"), Speaker("B")], "B");
        Assert.Equal(["B", "A"], seeded.Select(e => e.Id));
        Assert.Empty(PriorityPolicy.Seed([], null));
    }

    [Fact]
    public void New_devices_go_to_the_end_and_removed_ones_never_return()
    {
        var removed = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "R" };
        var list = PriorityPolicy.AppendNew(Abc, [Speaker("B"), Speaker("N"), Speaker("R"), Off("U")], removed);
        Assert.Equal(["A", "B", "C", "N"], list.Select(e => e.Id));
    }

    [Fact]
    public void Append_returns_the_same_list_when_nothing_is_new()
    {
        Assert.Same(Abc, PriorityPolicy.AppendNew(Abc, [Speaker("A")], new HashSet<string>()));
    }

    [Fact]
    public void Stale_id_is_rematched_when_exactly_one_endpoint_has_its_name()
    {
        var repaired = PriorityPolicy.Repair(Abc, [Speaker("A"), Speaker("B2", "Speakers"), Speaker("C")]);
        Assert.Equal(["A", "B2", "C"], repaired.Select(e => e.Id));
    }

    [Fact]
    public void Ambiguous_name_is_never_guessed()
    {
        var repaired = PriorityPolicy.Repair(Abc, [Speaker("A"), Speaker("X1", "Speakers"), Speaker("X2", "Speakers"), Speaker("C")]);
        Assert.Same(Abc, repaired);
    }

    [Fact]
    public void Repair_never_binds_an_endpoint_already_listed()
    {
        IReadOnlyList<PriorityEntry> list = [new("OLD", "Headset"), new("NEW", "Headset")];
        Assert.Same(list, PriorityPolicy.Repair(list, [Speaker("NEW", "Headset")]));
    }

    [Fact]
    public void Repair_can_bind_an_unplugged_endpoint()
    {
        var repaired = PriorityPolicy.Repair(Abc, [Speaker("A"), Off("B2", "Speakers"), Speaker("C")]);
        Assert.Equal("B2", repaired[1].Id);
    }
}
