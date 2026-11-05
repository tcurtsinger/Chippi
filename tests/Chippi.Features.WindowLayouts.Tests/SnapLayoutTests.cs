// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.Core.Modules;

namespace Chippi.Features.WindowLayouts.Tests;

public sealed class SnapLayoutTests
{
    [Fact]
    public void SplitResizeRemovePreservesOriginalZoneIdentity()
    {
        var original = new ZoneNode { Id = "reading" };
        var split = original.Divide("reading", SplitDirection.SideBySide);
        Assert.Equal("reading", split.Flatten().Zones[0].Id);
        var resized = split.Resize(split.Id, .7);
        Assert.Equal(.7, resized.Flatten().Zones[0].Width);
        Assert.Equal(original, resized.Remove(resized.Second!.Id));
        Assert.True(original.IsLeaf);
    }

    [Fact]
    public void BuiltInsTileTheUnitSquare()
    {
        foreach (var layout in SnapLayouts.Defaults())
        {
            var (zones, dividers) = layout.Root.Flatten();
            Assert.Equal(1, zones.Sum(z => z.Width * z.Height), 10);
            Assert.Equal(zones.Count - 1, dividers.Count);
            Assert.All(zones, z => Assert.True(z.IsValid));
        }
    }

    [Fact]
    public void MalformedTreesAreRejected()
    {
        var leaf = new ZoneNode();
        Assert.False(ZoneNode.Split(leaf, leaf, SplitDirection.Stacked).IsValid());
        Assert.False((leaf with { First = new ZoneNode() }).IsValid());
        Assert.False(ZoneNode.Split(new(), new(), SplitDirection.Stacked, double.NaN).IsValid());
        var deep = leaf;
        for (int i = 0; i < 18; i++) deep = ZoneNode.Split(deep, new(), SplitDirection.Stacked);
        Assert.False(deep.IsValid());
    }

    [Fact]
    public void DuplicateNamesAssignmentsAndDeleteArePersistent()
    {
        var settings = new MemorySettings();
        var store = new SnapLayoutStore(settings);
        int changes = 0;
        store.Changed += () => changes++;
        var copy = store.Duplicate("halves");
        Assert.Equal("Halves 2", copy.Name);
        Assert.Equal("Halves 3", store.Duplicate("halves").Name);
        store.AssignDisplay("display", copy.Id);
        Assert.Equal(copy.Id, store.ForDisplay("DISPLAY").Id);
        store.Move(copy.Id, 0);
        Assert.Equal(copy.Id, store.Settings.Layouts[0].Id);
        store.Delete(copy.Id);
        Assert.Equal("halves", store.ForDisplay("display").Id);
        Assert.Equal(5, changes);
        Assert.Equal(store.Settings, settings.Value);
    }

    [Fact]
    public void FailedSaveDoesNotPublishDraft()
    {
        var settings = new MemorySettings { FailSave = true };
        var store = new SnapLayoutStore(settings);
        bool changed = false;
        store.Changed += () => changed = true;
        Assert.Throws<IOException>(() => store.Duplicate("halves"));
        Assert.Equal(5, store.Settings.Layouts.Count);
        Assert.False(changed);
    }

    [Fact]
    public void SavedBuiltInsCannotReplaceTrustedTemplates()
    {
        var settings = new MemorySettings
        {
            Value = new WindowLayoutsSettings
            {
                WindowGap = 100,
                ScreenEdgeGap = -1,
                Layouts = [new("halves", "Hijacked", new ZoneNode()), new("bad", "Bad",
                    ZoneNode.Split(new(), new(), SplitDirection.Stacked, double.PositiveInfinity))]
            }
        };
        var store = new SnapLayoutStore(settings);
        Assert.Equal(5, store.Settings.Layouts.Count);
        Assert.Equal("Halves", store.Settings.Layouts[0].Name);
        Assert.Equal(24, store.Settings.WindowGap);
        Assert.Equal(0, store.Settings.ScreenEdgeGap);
        Assert.False(store.Settings.ShiftDragEnabled);
        Assert.Throws<InvalidOperationException>(() => store.Delete("halves"));
        Assert.Throws<InvalidOperationException>(() => store.Save(store.Settings.Layouts[0]));
    }

    [Fact]
    public void NamesSurviveEditingAndSettingsRoundTrip()
    {
        var settings = new MemorySettings();
        var store = new SnapLayoutStore(settings);
        var copy = store.Duplicate("halves");
        var root = copy.Root.Rename("left", " Main ");
        store.Save(copy with { Root = root });
        settings.Value = System.Text.Json.JsonSerializer.Deserialize<WindowLayoutsSettings>(
            System.Text.Json.JsonSerializer.Serialize(settings.Value))!;
        var loaded = new SnapLayoutStore(settings).Settings.Layouts.Single(l => l.Id == copy.Id);
        Assert.Equal("Main", loaded.Root.Flatten().Zones[0].Name);
        Assert.Equal("Left half", copy.Root.Flatten().Zones[0].Name);
        Assert.Equal("Main", root.Divide("left", SplitDirection.Stacked).Flatten().Zones[0].Name);
        Assert.NotEqual(new SnapZoneKey("halves", "left"), new SnapZoneKey(copy.Id, "left"));
        Assert.Throws<ArgumentException>(() => root.Rename("left", " "));
        Assert.Throws<ArgumentOutOfRangeException>(() => root.Rename("left", new string('x', 81)));
        Assert.Throws<ArgumentException>(() => root.Rename(root.Id, "Divider"));
        Assert.False((new ZoneNode { Name = "" }).IsValid());
    }

    [Fact]
    public void BuiltInNamesMatchPlacementNames()
    {
        foreach (var layout in SnapLayouts.Defaults())
            foreach (var zone in layout.Root.Flatten().Zones)
                Assert.Equal(SnapGeometry.BuiltIns.Single(z => z.Id == zone.Id).Name, zone.Name);
    }

    [Fact]
    public void NestedDividerSpansConvertPointerToLocalRatio()
    {
        var root = ZoneNode.Split(new(), ZoneNode.Split(new(),
            ZoneNode.Split(new(), new(), SplitDirection.Stacked, .6),
            SplitDirection.Stacked, .25), SplitDirection.SideBySide, .4);
        var dividers = root.Flatten().Dividers;
        var nested = dividers[2];
        Assert.Equal(.25, nested.AxisStart);
        Assert.Equal(.75, nested.AxisLength);
        Assert.Equal(.6, nested.Ratio);
        Assert.Equal(.6, (nested.Y - nested.AxisStart) / nested.AxisLength, 10);
        Assert.Equal(.6, nested.Length, 10);
        var thirds = SnapLayouts.Defaults().Single(l => l.Id == "thirds").Root.Flatten().Dividers[1];
        Assert.Equal(1d / 3, thirds.AxisStart, 10);
        Assert.Equal(2d / 3, thirds.AxisLength, 10);
        Assert.Equal(thirds.Ratio, (thirds.X - thirds.AxisStart) / thirds.AxisLength, 10);
    }

    private sealed class MemorySettings : IModuleSettings
    {
        public WindowLayoutsSettings Value = new();
        public bool FailSave;
        public T Get<T>() where T : class, new() => (T)(object)Value;
        public void Save<T>(T value) where T : class
        {
            if (FailSave) throw new IOException("Disk full");
            Value = (WindowLayoutsSettings)(object)value;
        }
    }
}
