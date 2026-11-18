// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors
namespace Chippi.Core.Admin;

/// <summary>The registry view an HKLM Uninstall entry was read from: the 64-bit view, or the 32-bit one (WOW6432Node).</summary>
public enum AdminRegistryView { Registry64 = 64, Registry32 = 32 }

/// <summary>One app to uninstall: the HKLM Uninstall key's name and view. AppName is for Chippi's own log (the helper never sees it); key names are never logged.</summary>
public sealed record AdminUninstallItem(AdminRegistryView View, string KeyName, string? AppName = null);

/// <summary>
/// Uninstalled: exit 0. RestartNeeded: exit 3010 (Restart to finish). Failed: any other exit, or the helper could not run it. Refused: one of the
/// helper's own checks said no (Reason says which), nothing ran. NeedsAdmin: the prompt was declined, nothing ran. Cancelled: skipped, nothing ran.
/// </summary>
public enum AdminUninstallState { Uninstalled, RestartNeeded, Failed, Refused, NeedsAdmin, Cancelled }

/// <summary>
/// RecordId is the helper's record of the app (admin-helper.md), written before the uninstaller ran; Chippi keeps it with its own copy of the
/// entry for the leftover scan. KeyGone is whether the Uninstall key no longer exists once the uninstaller's job ended.
/// </summary>
public sealed record AdminUninstallResult(AdminUninstallItem Item, AdminUninstallState State, int? ExitCode = null, bool KeyGone = false,
    Guid? RecordId = null, string? Reason = null)
{
    /// <summary>The status words of the approved mockup.</summary>
    public string Text => State switch
    {
        AdminUninstallState.Uninstalled => "Uninstalled",
        AdminUninstallState.RestartNeeded => "Restart to finish",
        AdminUninstallState.NeedsAdmin => "Needs admin",
        AdminUninstallState.Cancelled => "Cancelled",
        AdminUninstallState.Refused => "Refused",
        _ => "Uninstall failed",
    };
}

/// <summary>Index is 1-based. Result is null when the item starts and set when it ends.</summary>
public sealed record AdminUninstallProgress(AdminUninstallItem Item, int Index, int Count, AdminUninstallResult? Result = null);

/// <summary>The kind of a leftover the scan found. A RegistryKey's Path is the key's path under HKLM, starting with Software, in the item's View.</summary>
public enum AdminLeftoverKind { File, Folder, RegistryKey }

/// <summary>The class the review showed the item in. The helper checks the class again from its own record.</summary>
public enum AdminLeftoverClass { Safe, CheckFirst }

/// <summary>One leftover for the helper. Ticked says the owner chose it after the review; the helper refuses anything unticked. View is set for a registry key only.</summary>
public sealed record AdminLeftoverItem(AdminLeftoverKind Kind, string Path, AdminLeftoverClass Class, bool Ticked, AdminRegistryView? View = null);

/// <summary>
/// Removed: gone (moved to the owner's Recycle Bin, or the key deleted after an export). Kept: a helper check said no (Reason is its words, "Kept: ...").
/// InUse: something holds it open. Failed: it could not be removed. Yours: the owner's own account can delete it, so Chippi deletes it as him.
/// NeedsAdmin: the prompt was declined, nothing ran. Cancelled: skipped.
/// </summary>
public enum AdminLeftoverState { Removed, Kept, InUse, Failed, Yours, NeedsAdmin, Cancelled }

public sealed record AdminLeftoverResult(AdminLeftoverItem Item, AdminLeftoverState State, string? Reason = null)
{
    /// <summary>The status words of the approved mockup. Yours is Chippi's own step to do, so it has no word of the helper's.</summary>
    public string Text => State switch
    {
        AdminLeftoverState.Removed => "Removed",
        AdminLeftoverState.Kept => Reason ?? "Kept",
        AdminLeftoverState.InUse => "In use",
        AdminLeftoverState.NeedsAdmin => "Needs admin",
        AdminLeftoverState.Cancelled => "Cancelled",
        AdminLeftoverState.Yours => "",
        _ => Reason ?? "Remove failed",
    };
}
