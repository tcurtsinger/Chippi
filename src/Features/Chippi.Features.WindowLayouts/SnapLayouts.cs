// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

namespace Chippi.Features.WindowLayouts;

public enum SplitDirection { SideBySide, Stacked }

/// <summary>A leaf has a stable id and a name; a split has two children and a ratio. Records make editor drafts independent of saved layouts.</summary>
public sealed record ZoneNode
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string Name { get; init; } = "Zone";
    public SplitDirection Direction { get; init; }
    public double Ratio { get; init; } = .5;
    public ZoneNode? First { get; init; }
    public ZoneNode? Second { get; init; }
    public bool IsLeaf => First is null && Second is null;

    public static ZoneNode Split(ZoneNode first, ZoneNode second, SplitDirection direction, double ratio = .5) =>
        new() { First = first, Second = second, Direction = direction, Ratio = ratio };

    public bool IsValid()
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        return Check(this, 0, ids);
    }

    private static bool Check(ZoneNode node, int depth, HashSet<string> ids) =>
        depth <= 16 && !string.IsNullOrWhiteSpace(node.Id) && ids.Add(node.Id) &&
        (node.IsLeaf ? !string.IsNullOrWhiteSpace(node.Name) && node.Name.Length <= 80 : node.First is not null && node.Second is not null
            && Enum.IsDefined(node.Direction) && double.IsFinite(node.Ratio)
            && node.Ratio >= .05 && node.Ratio <= .95
            && Check(node.First, depth + 1, ids) && Check(node.Second, depth + 1, ids));

    public ZoneNode Divide(string zoneId, SplitDirection direction)
    {
        if (!Enum.IsDefined(direction))
            throw new ArgumentOutOfRangeException(nameof(direction));
        return Edit(zoneId, node => node.IsLeaf
            ? Split(node, new ZoneNode(), direction) : throw new ArgumentException("Not a zone"));
    }

    public ZoneNode Rename(string zoneId, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        name = name.Trim();
        if (name.Length > 80)
            throw new ArgumentOutOfRangeException(nameof(name));
        return Edit(zoneId, node => node.IsLeaf
            ? node with { Name = name } : throw new ArgumentException("Not a zone"));
    }

    public ZoneNode Resize(string dividerId, double ratio)
    {
        if (!double.IsFinite(ratio) || ratio < .05 || ratio > .95)
            throw new ArgumentOutOfRangeException(nameof(ratio));
        return Edit(dividerId, node => !node.IsLeaf ? node with { Ratio = ratio } : throw new ArgumentException("Not a divider"));
    }

    private ZoneNode Edit(string id, Func<ZoneNode, ZoneNode> change)
    {
        if (Id == id)
            return change(this);
        if (First?.Contains(id) == true)
            return this with { First = First.Edit(id, change) };
        if (Second?.Contains(id) == true)
            return this with { Second = Second.Edit(id, change) };
        throw new ArgumentException("Zone not found", nameof(id));
    }

    private bool Contains(string id) => Id == id || First?.Contains(id) == true || Second?.Contains(id) == true;

    public ZoneNode Remove(string zoneId)
    {
        if (IsLeaf || Id == zoneId)
            throw new ArgumentException("The last zone cannot be removed", nameof(zoneId));
        if (First!.Id == zoneId && First.IsLeaf)
            return Second!;
        if (Second!.Id == zoneId && Second.IsLeaf)
            return First;
        if (First.Contains(zoneId))
            return this with { First = First.Remove(zoneId) };
        if (Second.Contains(zoneId))
            return this with { Second = Second.Remove(zoneId) };
        throw new ArgumentException("Zone not found", nameof(zoneId));
    }

    public (IReadOnlyList<SnapZone> Zones, IReadOnlyList<ZoneDivider> Dividers) Flatten()
    {
        if (!IsValid())
            throw new ArgumentException("Invalid layout");
        var zones = new List<SnapZone>();
        var dividers = new List<ZoneDivider>();
        Walk(this, 0, 0, 1, 1, zones, dividers);
        return (zones, dividers);
    }

    private static void Walk(ZoneNode node, double x, double y, double width, double height,
        List<SnapZone> zones, List<ZoneDivider> dividers)
    {
        if (node.IsLeaf)
        {
            zones.Add(new(node.Id, node.Name, x, y, width, height));
            return;
        }
        bool side = node.Direction == SplitDirection.SideBySide;
        double cut = side ? width * node.Ratio : height * node.Ratio;
        dividers.Add(new(node.Id, node.Direction, side ? x + cut : x, side ? y : y + cut, side ? height : width, node.Ratio, side ? x : y, side ? width : height));
        Walk(node.First!, x, y, side ? cut : width, side ? height : cut, zones, dividers);
        Walk(node.Second!, side ? x + cut : x, side ? y : y + cut,
            side ? width - cut : width, side ? height : height - cut, zones, dividers);
    }
}

public sealed record ZoneDivider(string Id, SplitDirection Direction, double X, double Y, double Length,
    double Ratio, double AxisStart, double AxisLength);
/// <summary>Zone ids are local to a layout, including duplicated and built-in layouts.</summary>
public readonly record struct SnapZoneKey(string LayoutId, string ZoneId);
public sealed record SnapLayout(string Id, string Name, ZoneNode Root, bool BuiltIn = false);

public static class SnapLayouts
{
    private static ZoneNode Leaf(string id) => new() { Id = id, Name = SnapGeometry.BuiltIns.First(z => z.Id == id).Name };
    public static IReadOnlyList<SnapLayout> Defaults() =>
    [
        new("halves", "Halves", ZoneNode.Split(Leaf("left"), Leaf("right"), SplitDirection.SideBySide), true),
        new("thirds", "Thirds", ZoneNode.Split(Leaf("left-third"),
            ZoneNode.Split(Leaf("middle-third"), Leaf("right-third"), SplitDirection.SideBySide), SplitDirection.SideBySide, 1d / 3), true),
        new("wide-left", "Wide left", ZoneNode.Split(Leaf("left-two-thirds"), Leaf("right-third"), SplitDirection.SideBySide, 2d / 3), true),
        new("wide-right", "Wide right", ZoneNode.Split(Leaf("left-third"), Leaf("right-two-thirds"), SplitDirection.SideBySide, 1d / 3), true),
        new("quarters", "Quarters", ZoneNode.Split(
            ZoneNode.Split(Leaf("top-left"), Leaf("bottom-left"), SplitDirection.Stacked),
            ZoneNode.Split(Leaf("top-right"), Leaf("bottom-right"), SplitDirection.Stacked), SplitDirection.SideBySide), true),
    ];
}
