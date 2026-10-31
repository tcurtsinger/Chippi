// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.Audio;
using Chippi.Features.Sound.Routing;

namespace Chippi.Features.Sound.Tests;

public sealed class DeviceNameTests
{
    [Theory]
    [InlineData("Speakers (INZONE Buds - Game)", "INZONE Buds - Game")]
    [InlineData("Headset Microphone (INZONE Buds - Chat)", "INZONE Buds - Chat")]
    [InlineData("Microphone Array (Intel® Smart Sound Technology for Digital Microphones)", "Intel® Smart Sound Technology for Digital Microphones")]
    [InlineData("Digital Audio (S/PDIF) (Realtek(R) Audio)", "Realtek(R) Audio")]
    [InlineData("LG ULTRAGEAR (NVIDIA High Definition Audio)", "LG ULTRAGEAR (NVIDIA High Definition Audio)")] // a monitor's name is the device
    [InlineData("Elgato Wave:3 (Wave Link)", "Elgato Wave:3 (Wave Link)")]
    [InlineData("CABLE Input", "CABLE Input")]
    [InlineData("Speakers ()", "Speakers ()")]
    [InlineData("Lautsprecher (Realtek(R) Audio)", "Lautsprecher (Realtek(R) Audio)")] // other languages keep Windows' name
    public void Rows_lead_with_the_device_only_after_an_endpoint_kind(string name, string shown) =>
        Assert.Equal(shown, DeviceName.Short(name));

    [Fact]
    public void Two_rows_that_would_read_the_same_keep_their_full_names()
    {
        PriorityEntry[] rows =
        [
            new("a", "Speakers (Realtek(R) Audio)"), new("b", "Digital Audio (S/PDIF) (Realtek(R) Audio)"),
            new("c", "Speakers (INZONE Buds - Game)"), new("d", "Headset Earphone (INZONE Buds - Chat)"),
        ];
        var shown = DeviceName.Shown(rows);
        Assert.Equal(["Speakers (Realtek(R) Audio)", "Digital Audio (S/PDIF) (Realtek(R) Audio)", "INZONE Buds - Game", "INZONE Buds - Chat"],
            rows.Select(r => shown[r.Id]));
    }
}
