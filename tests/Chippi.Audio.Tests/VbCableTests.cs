// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using static Chippi.Audio.Tests.FakeDeviceSystem;

namespace Chippi.Audio.Tests;

public class VbCableTests
{
    private const string Second = @"{2}.\\?\root#media#0001#{6994ad04-93ef-11d0-a3cc-00a0c9223196}";

    [Fact]
    public void One_active_pair_is_ready()
    {
        var (state, pair) = VbCable.Find([Mic("Real"), CableInput(), CableOutput(), Speaker("Spk")]);
        Assert.Equal(CableState.Ready, state);
        Assert.Equal(new CablePair("CableIn", "CableOut"), pair);
    }

    [Fact]
    public void Names_are_not_identity()
    {
        var renamed = CableOutput() with { Name = "My Mic" };
        var lookalike = Mic("Fake", "CABLE Output (VB-Audio Virtual Cable)") with { AdapterName = "VB-Audio Virtual Cable" };

        Assert.True(VbCable.IsCableOutput(renamed));
        Assert.False(VbCable.IsCable(lookalike));
        Assert.Equal(CableState.Missing, VbCable.Find([lookalike, Speaker("CABLE Input")]).State);
    }

    [Fact]
    public void The_16_channel_endpoint_is_cable_but_never_the_pair()
    {
        var sixteen = Speaker("C16") with { Filter = CableDevice + @"\vbaudiovacwdm2022_out16_topo_e51a78a01fb100002" };
        Assert.True(VbCable.IsCable(sixteen));
        Assert.False(VbCable.IsCableInput(sixteen));
        Assert.Equal(CableState.Disabled, VbCable.Find([sixteen, CableOutput()]).State);
    }

    [Fact]
    public void Present_but_inactive_is_disabled()
    {
        var state = VbCable.Find([CableInput() with { State = EndpointState.Disabled }, CableOutput()]).State;
        Assert.Equal(CableState.Disabled, state);
    }

    [Fact]
    public void Two_installs_are_ambiguous_and_halves_of_different_installs_never_pair()
    {
        Assert.Equal(CableState.Ambiguous, VbCable.Find([CableInput(), CableOutput(), CableInput("In2", Second), CableOutput("Out2", Second)]).State);
        Assert.Equal(CableState.Disabled, VbCable.Find([CableInput(), CableOutput("Out2", Second)]).State);
    }

    [Fact]
    public void Real_device_filters_from_this_pc_are_not_cable()
    {
        // From the S3 topology probe on Travis's PC (read only).
        var rode = Mic("R") with { Filter = @"{2}.\\?\usb#vid_19f7&pid_001c&mi_00#9&ca801cc&0&0000#{6994ad04-93ef-11d0-a3cc-00a0c9223196}\global" };
        var nvidia = Speaker("N") with { Filter = @"{2}.\\?\hdaudio#func_01&ven_10de&dev_00a4&subsys_1462511a&rev_1001#5&169ef751&0&0001#{6994ad04-93ef-11d0-a3cc-00a0c9223196}\topo02" };
        Assert.False(VbCable.IsCable(rode));
        Assert.False(VbCable.IsCable(nvidia));
    }
}
