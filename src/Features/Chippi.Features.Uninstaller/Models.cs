// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors
using System.Globalization;
using Chippi.Core.Admin;
using Chippi.Core.Shortcuts;

namespace Chippi.Features.Uninstaller;

/// <summary>Where an app's entry was read: the machine's Uninstall key in the 64-bit or 32-bit view, the user's own, or the Store's package list.</summary>
public enum AppSource { Machine64, Machine32, User, Store }

/// <summary>Component, Self and Framework apps are listed only when asked for and get no verb.</summary>
public enum AppKind { App, Component, Self }

/// <summary>The registry hive of a leftover key: the user's, or the machine's in one of its two views.</summary>
public enum RegHive { User, Machine64, Machine32 }

/// <summary>
/// One row of the list. Nothing here is a secret or a path the helper would trust: the helper reads the real entry itself. <see cref="Key"/> is
/// the Uninstall key's name (a registry app) or the package full name (a Store app).
/// </summary>
public sealed record AppEntry(string Id, string Name, string Publisher, string Version, long? SizeBytes, DateTime? Installed, string? IconPath,
    AppSource Source, string Key, string? InstallLocation, AppKind Kind)
{
    /// <summary>The entry's command, as read: only the registry apps have one.</summary>
    public string? UninstallString { get; init; }
    public string? QuietUninstallString { get; init; }
    public string? ProductCode { get; init; }
    public bool IsMsi { get; init; }
    /// <summary>NoRemove=1: the app asks not to be removed from here.</summary>
    public bool NoRemove { get; init; }
    public string? PackageFamilyName { get; init; }

    public bool IsStore => Source == AppSource.Store;
    public bool IsMachine => Source is AppSource.Machine64 or AppSource.Machine32;

    /// <summary>Installed in the last 7 days.</summary>
    public bool IsRecent(DateTime now) => Installed is { } when && now - when <= TimeSpan.FromDays(7) && when <= now.AddDays(1);

    /// <summary>The row has a command to run. A Store app always does (its package); a registry app needs a quiet or normal string, or to be an MSI.</summary>
    public bool HasCommand => IsStore || IsMsi || !string.IsNullOrWhiteSpace(QuietUninstallString) || !string.IsNullOrWhiteSpace(UninstallString);

    /// <summary>The row gets Uninstall: not a component, not Chippi, not NoRemove, and it has a command to run.</summary>
    public bool CanUninstall => Kind == AppKind.App && !NoRemove && HasCommand;

    /// <summary>The row's second-line word when it has no verb: "No uninstaller" for an app that has no command; null otherwise.</summary>
    public string? NoVerbText => Kind == AppKind.App && !NoRemove && !HasCommand ? "No uninstaller" : null;

    /// <summary>The needs-admin shield: an app of the machine's Uninstall key runs through the helper.</summary>
    public bool NeedsAdmin => IsMachine;

    public AdminRegistryView? HelperView => Source switch { AppSource.Machine64 => AdminRegistryView.Registry64, AppSource.Machine32 => AdminRegistryView.Registry32, _ => null };
}

/// <summary>Where a row of the list is in a queue. The words are <see cref="AppStatus.Text"/>.</summary>
public enum AppRunState
{
    Idle, Queued, Uninstalling, Uninstalled, RestartNeeded, Failed, NeedsAdmin, Refused, Running, Cancelled,
}

/// <summary>"Uninstalled", "Restart to finish", "Uninstall failed", "Needs admin", "Running" (the app is open), "Queued", "Uninstalling", "Cancelled".</summary>
public sealed record AppStatus(AppRunState State, string? App = null)
{
    public string? Text => State switch
    {
        AppRunState.Queued => "Queued",
        AppRunState.Uninstalling => "Uninstalling",
        AppRunState.Uninstalled => "Uninstalled",
        AppRunState.RestartNeeded => "Restart to finish",
        AppRunState.Failed => "Uninstall failed",
        AppRunState.NeedsAdmin => "Needs admin",
        AppRunState.Refused => "Refused",
        AppRunState.Running => "Running",
        AppRunState.Cancelled => "Cancelled",
        _ => null,
    };

    /// <summary>The line the brief words as "Close Notepad++ first": the screens show it for a Running row when they have room.</summary>
    public string? Detail => State == AppRunState.Running && App is { } app ? "Close " + app + " first" : null;
    public bool IsFinal => State is AppRunState.Uninstalled or AppRunState.RestartNeeded or AppRunState.Failed or AppRunState.NeedsAdmin or AppRunState.Refused
        or AppRunState.Running or AppRunState.Cancelled;
}

public sealed record AppOutcome(AppEntry App, AppStatus Status, Guid? RecordId = null)
{
    public bool Succeeded => Status.State is AppRunState.Uninstalled or AppRunState.RestartNeeded;
}

/// <summary>Raised per app: Index is 1-based over the whole queue.</summary>
public sealed record UninstallProgress(AppEntry App, int Index, int Count, AppOutcome? Outcome = null);

/// <summary>
/// What Chippi kept of an app before its uninstaller ran, for the leftover scan: uninstallers usually delete their own entry. For a machine app
/// <see cref="HelperRecordId"/> is the helper's own record (30 days); without it the scan lists only Check first items and nothing admin is sent.
/// </summary>
public sealed record AppRecord(Guid Id, string Name, string Publisher, string Version, AppSource Source, string Key, string? InstallLocation,
    string? ProductCode, IReadOnlyList<string> Folders, string? PackageFamilyName, DateTime CreatedUtc, Guid? HelperRecordId = null, bool Cleaned = false, bool Restart = false)
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(30);

    /// <summary>With a machine app, the helper's record must exist; with the user's own or a Store app, Chippi's copy is the record.</summary>
    public bool HasRecord => Source is AppSource.User or AppSource.Store || HelperRecordId is not null;
}

/// <summary>The module's settings section: only its shortcuts.</summary>
public sealed record UninstallerSettings
{
    public Dictionary<string, KeyShortcut?> Shortcuts { get; init; } = [];
}

/// <summary>Sizes and names in the words of the mockups.</summary>
public static class UninstallerText
{
    /// <summary>"820 MB", "1.6 GB", "5.6 MB"; null is "".</summary>
    public static string Size(long? bytes)
    {
        if (bytes is not { } b || b <= 0) return "";
        var c = CultureInfo.InvariantCulture;
        double mb = b / 1048576.0;
        return mb >= 1024 ? (mb / 1024).ToString("0.0", c) + " GB" : mb < 10 ? mb.ToString("0.0", c) + " MB" : Math.Round(mb).ToString("0", c) + " MB";
    }

    /// <summary>"Uninstall Notepad++?" for one, "Uninstall 3 apps?" for several.</summary>
    public static string Question(IReadOnlyList<AppEntry> apps) => apps.Count == 1 ? "Uninstall " + apps[0].Name + "?" : "Uninstall " + apps.Count + " apps?";

    /// <summary>"Remove 14 items?"</summary>
    public static string RemoveQuestion(int count) => count == 1 ? "Remove 1 item?" : "Remove " + count + " items?";

    /// <summary>A key made of letters, digits, "+" and "#", for comparing names ("Notepad++" and "Notepad" differ; "Mozilla Firefox" and "mozillafirefox" match).</summary>
    public static string NameKey(string? text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var chars = text.Where(c => char.IsLetterOrDigit(c) || c is '+' or '#').Select(char.ToLowerInvariant).ToArray();
        return new string(chars);
    }
}
