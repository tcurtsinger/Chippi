// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.Core.Modules;

namespace Chippi.Core.Permissions;

/// <summary>One row of the Permissions page.</summary>
/// <param name="Capability">The permission or dependency.</param>
/// <param name="UsedBy">Installed features that need it now.</param>
/// <param name="IsUnused">Something needed it before, but no installed feature does anymore, so the user can remove it.</param>
public sealed record CapabilityUsage(Capability Capability, IReadOnlyList<FeatureDescriptor> UsedBy, bool IsUnused);

public static class PermissionsModel
{
    /// <summary>
    /// Lists every capability any registered feature declares (placeholders included, so the page
    /// shows what is coming), with the installed features using it. A capability is unused when
    /// it appears in <paramref name="everUsed"/> and nothing installed needs it now. One that was
    /// never used is not flagged: there is nothing for the user to clean up.
    /// </summary>
    public static IReadOnlyList<CapabilityUsage> Build(IEnumerable<FeatureEntry> features, IEnumerable<string> everUsed)
    {
        var used = everUsed.ToHashSet(StringComparer.Ordinal);
        var all = features.ToList();
        return
        [
            .. all.SelectMany(f => f.Descriptor.Requires)
                .Distinct()
                .Select(capability =>
                {
                    var users = all
                        .Where(f => f.IsInstalled && f.Descriptor.Requires.Contains(capability))
                        .Select(f => f.Descriptor)
                        .ToList();
                    return new CapabilityUsage(capability, users, IsUnused: users.Count == 0 && used.Contains(capability.Id));
                }),
        ];
    }
}
