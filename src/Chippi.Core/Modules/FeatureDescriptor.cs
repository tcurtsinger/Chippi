// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

namespace Chippi.Core.Modules;

/// <summary>Where a feature sits in the Features hub. Order here is the hub's group order.</summary>
public enum FeatureGroup
{
    Files,
    Sound,
    System,
    ClipboardText,
    Windows,
    Capture,
    Apps,
}

public static class FeatureGroupExtensions
{
    /// <summary>Sentence-case group name for the hub eyebrow (the shell uppercases it).</summary>
    public static string DisplayName(this FeatureGroup group) => group switch
    {
        FeatureGroup.ClipboardText => "Clipboard & text",
        _ => group.ToString(),
    };
}

/// <summary>The three badge tints from DESIGN.md §6.</summary>
public enum BadgeKind
{
    /// <summary>Violet tint, e.g. "From Hopper".</summary>
    Source,

    /// <summary>Amber tint, e.g. "Overlaps PowerToys".</summary>
    Overlap,

    /// <summary>Neutral, e.g. "Needs VB-CABLE".</summary>
    Needs,
}

public sealed record FeatureBadge(BadgeKind Kind, string Text)
{
    public static FeatureBadge Source(string text) => new(BadgeKind.Source, text);

    public static FeatureBadge Needs(string text) => new(BadgeKind.Needs, text);
}

/// <summary>Presets that pick a starting set of features. Toggling any single feature switches to <see cref="Custom"/>.</summary>
public enum FeaturePreset
{
    Essentials,
    Audio,
    PowerUser,
    Everything,
    Custom,
}

public static class FeaturePresetExtensions
{
    public static string DisplayName(this FeaturePreset preset) => preset switch
    {
        FeaturePreset.PowerUser => "Power user",
        _ => preset.ToString(),
    };
}

/// <summary>Which named presets include a feature. <see cref="FeaturePreset.Everything"/> includes every installable feature.</summary>
[Flags]
public enum PresetMembership
{
    None = 0,
    Essentials = 1,
    Audio = 2,
    PowerUser = 4,
}

/// <summary>
/// Everything the shell needs to show a feature without loading it: the hub card, badges,
/// preset membership and the capabilities for the Permissions page. Coming-soon placeholders
/// are a descriptor with no module behind it.
/// </summary>
/// <param name="Id">Stable kebab-case id, e.g. "keep-awake". Also the module's config section name.</param>
/// <param name="Name">Sentence-case name, e.g. "Keep awake".</param>
/// <param name="Description">One line, sentence case, no trailing period.</param>
/// <param name="Group">Hub group.</param>
public sealed record FeatureDescriptor(string Id, string Name, string Description, FeatureGroup Group)
{
    public IReadOnlyList<Capability> Requires { get; init; } = [];

    public IReadOnlyList<FeatureBadge> Badges { get; init; } = [];

    public PresetMembership Presets { get; init; }

    /// <summary>Icon key the shell resolves to a Lucide geometry (a property name on <c>LucideIcons</c>, e.g. "Coffee").</summary>
    public string? Icon { get; init; }

    /// <summary>
    /// Something outside Chippi the feature needs before it can be installed (Mic processing's
    /// VB-CABLE driver). Add features creates one while it shows the feature's row and disposes it
    /// after; null for features that need nothing.
    /// </summary>
    public Func<IFeatureRequirement>? Requirement { get; init; }
}
