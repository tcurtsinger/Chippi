// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows.Automation.Peers;
using Chippi.App.Pet;
using Chippi.Core.Config;

namespace Chippi.Island.Tests;

public sealed class ChippiPetTests
{
    [Fact]
    public void Every_mood_color_and_size_has_the_named_pet()
    {
        WpfTestThread.Run(() =>
        {
            foreach (var color in Enum.GetValues<PetColor>())
                foreach (var size in new[] { 24.0, 32, 48, 72 })
                    foreach (var mood in Enum.GetValues<PetMood>())
                    {
                        var pet = new ChippiPet { Size = size, PetColor = color, Mood = mood };
                        var peer = UIElementAutomationPeer.CreatePeerForElement(pet)!;
                        Assert.Equal("ChippiPet", peer.GetClassName());
                        Assert.Equal($"Chippi, {ChippiPet.Describe(mood)}", peer.GetName());
                        Assert.Equal(size, pet.Width);
                    }
        });
    }
#if DEBUG
    [Fact]
    public void Render_approved_pet_sizes()
    {
        var output = Environment.GetEnvironmentVariable("CHIPPI_PET_SHOTS");
        if (output is not null)
            WpfTestThread.Run(() => PetSheet.Save(output));
    }
#endif
}
