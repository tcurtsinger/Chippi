// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.App.Settings.Features;
using Chippi.UI.Controls;

namespace Chippi.Island.Tests;

public sealed class CatalogTests
{
    [Fact]
    public void Every_catalog_glyph_exists_and_quick_panel_is_gone()
    {
        foreach (var entry in FeatureCatalog.Placeholders)
            Assert.True(entry.Icon == "LayoutGrid" || !ReferenceEquals(LucideIcons.Get(entry.Icon), LucideIcons.LayoutGrid), entry.Id);
        Assert.DoesNotContain(FeatureCatalog.Placeholders, e => e.Id == "quick-panel");
    }

    [Theory]
    [InlineData("clipboard-history", "ClipboardList", null)]
    [InlineData("paste-plain-text", "ClipboardType", null)]
    [InlineData("clean-url", "Link", null)]
    [InlineData("scratchpad", "NotebookPen", null)]
    [InlineData("shelf", "Inbox", null)]
    public void Shipped_features_keep_their_glyphs_without_overlap_badges(string id, string icon, string? overlap)
    {
        // Shipped features have left the catalog; their modules carry the same descriptor.
        Core.Modules.FeatureDescriptor[] shipped =
        [
            new Features.Clipboard.ClipboardHistoryModule(new Features.Clipboard.ClipboardFeatures()).Descriptor,
            new Features.Clipboard.CleanUrlModule(new Features.Clipboard.ClipboardFeatures()).Descriptor,
            new Features.PlainPaste.PlainPasteModule().Descriptor,
            new Features.Scratchpad.ScratchpadModule().Descriptor,
            new Features.Shelf.ShelfModule().Descriptor,
        ];
        var entry = Assert.Single(FeatureCatalog.Placeholders.Concat(shipped), e => e.Id == id);
        Assert.Equal(icon, entry.Icon);
        Assert.Equal(overlap, entry.Badges.SingleOrDefault()?.Text);
    }
}
