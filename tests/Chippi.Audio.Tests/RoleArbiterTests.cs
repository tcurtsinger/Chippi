// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Microsoft.Extensions.Logging.Abstractions;
using static Chippi.Audio.Tests.FakeDeviceSystem;

namespace Chippi.Audio.Tests;

public class RoleArbiterTests
{
    private static readonly AudioRole[] All = [AudioRole.Console, AudioRole.Multimedia, AudioRole.Communications];
    private static readonly AudioRole[] NoCalls = [AudioRole.Console, AudioRole.Multimedia];

    private readonly FakeDeviceSystem _sys = new();
    private readonly DeviceCatalog _catalog = new();
    private readonly ManualClock _clock = new();
    private readonly RoleArbiter _arbiter;

    public RoleArbiterTests() => _arbiter = new RoleArbiter(NullLogger.Instance, _clock);

    private static RoleTarget List(params string[] ids) =>
        active => PriorityPolicy.Resolve(ids.Select(id => new PriorityEntry(id, id)).ToList(), active);

    private IReadOnlyList<RoleChange> Enforce()
    {
        _catalog.Refresh(_sys);
        return _arbiter.Enforce(_catalog, _sys);
    }

    private string? Default(AudioFlow flow, AudioRole role = AudioRole.Multimedia) => _sys.DefaultOf(flow, role);

    [Fact]
    public void Claim_points_every_role_at_the_winner_and_reports_one_quiet_change()
    {
        _sys.With(Speaker("A"), Speaker("B")).WithDefault("B");
        _arbiter.Claim(RoleOwner.Routing, AudioFlow.Render, All, List("A", "B"));

        var change = Assert.Single(Enforce());

        Assert.Equal(RoleChangeKind.Claim, change.Kind);
        Assert.Equal("A", change.EndpointId);
        Assert.Equal(All, change.Roles);
        Assert.All(All, r => Assert.Equal("A", Default(AudioFlow.Render, r)));
    }

    [Fact]
    public void Nothing_is_written_when_the_winner_is_already_default_for_every_role()
    {
        _sys.With(Speaker("A")).WithDefault("A");
        _arbiter.Claim(RoleOwner.Routing, AudioFlow.Render, All, List("A"));

        Assert.Empty(Enforce());
        Assert.Empty(_sys.Writes);
    }

    [Fact]
    public void Only_drifted_roles_are_repaired()
    {
        _sys.With(Speaker("A"), Speaker("B")).WithDefault("A");
        _arbiter.Claim(RoleOwner.Routing, AudioFlow.Render, All, List("A"));
        Enforce();
        _sys.ChangeDefault("B", AudioRole.Communications);

        var change = Assert.Single(Enforce());

        Assert.Equal(RoleChangeKind.Repair, change.Kind);
        Assert.Equal([AudioRole.Communications], change.Roles);
        Assert.Equal([("A", AudioRole.Communications)], _sys.Writes);
    }

    [Fact]
    public void Connecting_a_higher_device_switches_and_disconnecting_falls_back()
    {
        _sys.With(Speaker("B"), Speaker("C")).WithDefault("C");
        _arbiter.Claim(RoleOwner.Routing, AudioFlow.Render, All, List("A", "B", "C"));
        Assert.Equal(RoleChangeKind.Claim, Assert.Single(Enforce()).Kind);

        _sys.Connect(Speaker("A"));
        var up = Assert.Single(Enforce());
        Assert.Equal((RoleChangeKind.Switch, "A"), (up.Kind, up.EndpointId));

        _sys.Disconnect("A", windowsPicks: "C");
        var down = Assert.Single(Enforce());
        Assert.Equal((RoleChangeKind.Switch, "B"), (down.Kind, down.EndpointId));
        Assert.Equal("B", Default(AudioFlow.Render));
    }

    [Fact]
    public void Nothing_listed_available_leaves_windows_choice()
    {
        _sys.With(Speaker("A"), Speaker("D")).WithDefault("A");
        _arbiter.Claim(RoleOwner.Routing, AudioFlow.Render, All, List("A"));
        Enforce();

        _sys.Disconnect("A"); // Windows moves the default to D
        Assert.Empty(Enforce());
        Assert.Equal("D", Default(AudioFlow.Render));

        _sys.Connect(Speaker("A"));
        Assert.Equal(RoleChangeKind.Switch, Assert.Single(Enforce()).Kind);
    }

    [Fact]
    public void Call_role_off_leaves_communications_alone()
    {
        _sys.With(Speaker("A"), Speaker("Phone")).WithDefault("Phone");
        _arbiter.Claim(RoleOwner.Routing, AudioFlow.Render, NoCalls, List("A"));

        Assert.Equal(NoCalls, Assert.Single(Enforce()).Roles);
        Assert.Equal("Phone", Default(AudioFlow.Render, AudioRole.Communications));

        _sys.ChangeDefault("Phone", AudioRole.Console);
        Assert.Equal([AudioRole.Console], Assert.Single(Enforce()).Roles);
    }

    [Fact]
    public void Reclaiming_without_a_role_releases_it()
    {
        _sys.With(Speaker("A"), Speaker("B")).WithDefault("A");
        _arbiter.Claim(RoleOwner.Routing, AudioFlow.Render, All, List("A"));
        Enforce();
        _arbiter.Claim(RoleOwner.Routing, AudioFlow.Render, NoCalls, List("A"));

        _sys.ChangeDefault("B", AudioRole.Communications);
        Assert.Empty(Enforce());
        Assert.DoesNotContain(new RoleKey(AudioFlow.Render, AudioRole.Communications), _arbiter.Owners.Keys);
    }

    [Fact]
    public void Our_own_write_echo_is_not_a_repair()
    {
        _sys.With(Speaker("A"), Speaker("B")).WithDefault("B");
        _arbiter.Claim(RoleOwner.Routing, AudioFlow.Render, All, List("A"));
        Enforce();

        for (var i = 0; i < 5; i++)
            Assert.Empty(Enforce());
        Assert.Equal(3, _sys.Writes.Count);
    }

    [Fact]
    public void Repeated_repairs_back_off_then_resume()
    {
        AudioRole[] mm = [AudioRole.Multimedia];
        _sys.With(Speaker("A"), Speaker("B")).WithDefault("A");
        _arbiter.Claim(RoleOwner.Routing, AudioFlow.Render, mm, List("A"));
        Enforce();

        for (var i = 0; i < RoleArbiter.MaxRepairs; i++)
        {
            _clock.Advance(TimeSpan.FromSeconds(1));
            _sys.ChangeDefault("B", mm);
            Assert.Equal(RoleChangeKind.Repair, Assert.Single(Enforce()).Kind);
        }

        _clock.Advance(TimeSpan.FromSeconds(1));
        _sys.ChangeDefault("B", mm);
        Assert.Empty(Enforce());
        Assert.Equal("B", Default(AudioFlow.Render));

        _clock.Advance(RoleArbiter.BackoffPeriod);
        Assert.Equal(RoleChangeKind.Repair, Assert.Single(Enforce()).Kind);
        Assert.Equal("A", Default(AudioFlow.Render));
    }

    [Fact]
    public void Back_off_expires_even_when_the_role_is_already_right_again()
    {
        AudioRole[] mm = [AudioRole.Multimedia];
        _sys.With(Speaker("A"), Speaker("B")).WithDefault("A");
        _arbiter.Claim(RoleOwner.Routing, AudioFlow.Render, mm, List("A"));
        Enforce();
        for (var i = 0; i <= RoleArbiter.MaxRepairs; i++)
        {
            _sys.ChangeDefault("B", mm);
            Enforce();
        }
        Assert.NotNull(_arbiter.BackoffEnds);

        _sys.ChangeDefault("A", mm); // put right by someone else during the back-off
        _clock.Advance(RoleArbiter.BackoffPeriod);
        Assert.Empty(Enforce());

        Assert.Null(_arbiter.BackoffEnds);
    }

    [Fact]
    public void Repairs_spread_over_more_than_the_window_never_back_off()
    {
        AudioRole[] mm = [AudioRole.Multimedia];
        _sys.With(Speaker("A"), Speaker("B")).WithDefault("A");
        _arbiter.Claim(RoleOwner.Routing, AudioFlow.Render, mm, List("A"));
        Enforce();

        for (var i = 0; i < 6; i++)
        {
            _clock.Advance(TimeSpan.FromSeconds(6));
            _sys.ChangeDefault("B", mm);
            Assert.Single(Enforce());
        }
    }

    [Fact]
    public void Processing_takes_the_capture_roles_and_hands_them_back_to_routing()
    {
        _sys.With(Mic("Real"), Mic("Cable")).WithDefault("Real");
        _arbiter.Claim(RoleOwner.Routing, AudioFlow.Capture, All, List("Real"));
        Enforce();

        _arbiter.Claim(RoleOwner.Processing, AudioFlow.Capture, All, _ => "Cable");
        var taken = Assert.Single(Enforce());
        Assert.Equal((RoleOwner.Processing, RoleChangeKind.Claim, "Cable"), (taken.Owner, taken.Kind, taken.EndpointId));
        Assert.Equal(RoleOwner.Processing, _arbiter.Owners[new RoleKey(AudioFlow.Capture, AudioRole.Multimedia)]);

        _arbiter.Release(RoleOwner.Processing, AudioFlow.Capture, null, _catalog, _sys);
        var back = Assert.Single(Enforce());
        Assert.Equal((RoleOwner.Routing, RoleChangeKind.Claim, "Real"), (back.Owner, back.Kind, back.EndpointId));
    }

    [Fact]
    public void Processing_without_routing_restores_what_it_replaced()
    {
        _sys.With(Mic("Real"), Mic("Cable")).WithDefault("Real");
        _arbiter.Claim(RoleOwner.Processing, AudioFlow.Capture, All, _ => "Cable");
        Enforce();

        _arbiter.Release(RoleOwner.Processing, AudioFlow.Capture, All.ToDictionary(r => r, _ => "Real"), _catalog, _sys);

        Assert.All(All, r => Assert.Equal("Real", Default(AudioFlow.Capture, r)));
        Assert.Empty(_arbiter.Owners);
    }

    [Fact]
    public void Restore_skips_an_endpoint_that_is_gone()
    {
        _sys.With(Mic("Real"), Mic("Cable")).WithDefault("Real");
        _arbiter.Claim(RoleOwner.Processing, AudioFlow.Capture, All, _ => "Cable");
        Enforce();
        _sys.Disconnect("Real");
        _catalog.Refresh(_sys);
        var before = _sys.Writes.Count;

        _arbiter.Release(RoleOwner.Processing, AudioFlow.Capture, All.ToDictionary(r => r, _ => "Real"), _catalog, _sys);

        Assert.Equal(before, _sys.Writes.Count);
    }

    [Fact]
    public void Release_by_an_owner_that_held_nothing_restores_nothing()
    {
        _sys.With(Mic("Real"), Mic("Other")).WithDefault("Other");
        _catalog.Refresh(_sys);

        _arbiter.Release(RoleOwner.Processing, AudioFlow.Capture, All.ToDictionary(r => r, _ => "Real"), _catalog, _sys);

        Assert.Empty(_sys.Writes);
    }

    [Fact]
    public void Unclaimed_flows_are_never_written()
    {
        _sys.With(Speaker("A"), Mic("M1"), Mic("M2")).WithDefault("M2");
        _arbiter.Claim(RoleOwner.Routing, AudioFlow.Render, All, List("A"));
        Enforce();

        Assert.DoesNotContain(_sys.Writes, w => w.Id.StartsWith('M'));
    }

    [Fact]
    public void A_failed_write_is_not_reported()
    {
        _sys.With(Speaker("A"), Speaker("B")).WithDefault("B");
        _sys.Failing.Add("A");
        _arbiter.Claim(RoleOwner.Routing, AudioFlow.Render, All, List("A"));

        Assert.Empty(Enforce());
        Assert.Equal("B", Default(AudioFlow.Render));
    }
}
