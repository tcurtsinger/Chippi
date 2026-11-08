// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.UI.Displays;

namespace Chippi.Features.Displays.Tests;

public sealed class DisplayCatalogTests
{
    [Fact]
    public void Mirrored_targets_share_one_capture_id_without_losing_other_monitors()
    {
        DisplayInfo[] displays =
        [
            new("laptop", "Built-in display", 42, true),
            new("projector", "Projector", 42, true),
            new("side", "Side display", 84, false),
        ];

        var ids = DisplayCatalog.CaptureIds(displays);

        Assert.Equal(2, ids.Count);
        Assert.Equal("laptop", ids[42]);
        Assert.Equal("side", ids[84]);
    }

    [Fact]
    public void An_empty_display_catalog_has_no_capture_ids() =>
        Assert.Empty(DisplayCatalog.CaptureIds([]));
}
