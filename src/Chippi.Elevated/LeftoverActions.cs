// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors
using Chippi.Elevated.Protocol;

namespace Chippi.Elevated;

/// <summary>One installed app's Uninstall entry, from HKLM (either view) or the owner's HKCU (Hive "HKCU"). Read only to learn what is another app's.</summary>
public sealed record InstalledApp(string Hive, int View, string KeyName, string? DisplayName, string? InstallLocation, string? UninstallString, string? DisplayIcon);

public interface IInstalledApps
{
    /// <summary>Every Uninstall entry the helper can see: both HKLM views and the verified owner's HKCU (through the owner's hive under HKU).</summary>
    IReadOnlyList<InstalledApp> List();
}

/// <summary>What the helper found on a registry key. Unsafe: the key or one under it is a registry link, or the tree could not be read in full.</summary>
public sealed record KeyProbe(bool Exists, bool Unsafe, bool StandardCanDelete);

public interface ILeftoverRegistry
{
    /// <summary>Looks at HKLM\path in the view without following a link, and at every key under it.</summary>
    KeyProbe Probe(int view, string path);
    /// <summary>RegDeleteTree on the key itself.</summary>
    bool DeleteTree(int view, string path);
}

public enum RecycleOutcome { Removed, InUse, Failed }

/// <summary>The owner's machine as DeleteLeftovers needs it: the fixed deny list, the backup folder, and the one place a file leaves (the owner's Recycle Bin).</summary>
public interface ILeftoverSystem
{
    string Expand(string text);
    /// <summary>The Windows System32 folder, where reg.exe lives.</summary>
    string SystemDirectory { get; }
    /// <summary>Folders refused as themselves and as anything that contains them (Program Files, Common Files, ProgramData, user profile roots).</summary>
    IReadOnlyList<string> ProtectedRoots { get; }
    /// <summary>Folders refused with everything inside and everything that contains them (Windows, Chippi's own folders).</summary>
    IReadOnlyList<string> ProtectedTrees { get; }
    /// <summary>The fixed folder under the verified owner's Chippi data folder where registry exports go. Chippi is never told it.</summary>
    string BackupFolder { get; }
    /// <summary>True when something is at the path (a link counts, and is not followed).</summary>
    bool Exists(string path);
    /// <summary>Creates this one folder (its parent exists).</summary>
    bool CreateFolder(string path);
    /// <summary>SHFileOperation with FOF_ALLOWUNDO and FOF_WANTNUKEWARNING: the owner's Recycle Bin. A reparse point inside goes as a link.</summary>
    RecycleOutcome Recycle(string path);
}

public interface ILeftoverActions { Task<AdminReply> RunAsync(AdminRequest request, CancellationToken ct); }

/// <summary>
/// DeleteLeftovers(recordId, items) and DropRecord(recordId), as admin-helper.md specifies. The app's identity is the helper's own stored record
/// and nothing Chippi sends: with none, every item is kept. Each item is checked again here, whatever class Chippi showed it in.
/// </summary>
public sealed class LeftoverActions(IRecordStore records, IInstalledApps apps, IFsProbe probe, ILeftoverRegistry registry, ILeftoverSystem system,
    IProcessLauncher launcher, TimeProvider? time = null) : ILeftoverActions
{
    public const string NoRecordReason = "Kept: no uninstall record", ProtectedReason = "Kept: protected folder", ChangedReason = "Kept: changed since the scan",
        NotThisAppsReason = "Kept: not this app's", NotSelectedReason = "Kept: not selected", BackupFailedReason = "Backup failed",
        FailedReason = "Admin helper failed", RecordsReason = "Records unavailable";
    public static string SharedReason(string app) => $"Kept: shared with {app}";
    public static readonly TimeSpan ExportCeiling = TimeSpan.FromMinutes(2);
    private const string TrustedInstaller = "S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464";
    private const uint DeleteRight = 0x10000, DeleteChildRight = 0x40;
    private const string UninstallRoot = @"Software\Microsoft\Windows\CurrentVersion\Uninstall";
    private readonly TimeProvider _time = time ?? TimeProvider.System;

    public Task<AdminReply> RunAsync(AdminRequest request, CancellationToken ct) => Task.Run(async () =>
    {
        try
        {
            if (!Messages.RequestAllowed(request) || !Messages.IsLeftoverVerb(request.Verb)) return new AdminReply(request.Id, Messages.LeftoversFailed, Reason: "Invalid request");
            return request.Verb == Messages.DropRecord ? Drop(request) : await DeleteAsync(request, ct).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Any failure answers the request: an exception must never end the helper's session. Nothing from it is logged or sent.
            return new AdminReply(request.Id, Messages.LeftoversFailed, Reason: FailedReason);
        }
    }, CancellationToken.None);

    private AdminReply Drop(AdminRequest r)
    {
        if (!records.Prepare()) return new AdminReply(r.Id, Messages.LeftoversFailed, Reason: RecordsReason);
        records.Delete(r.RecordId!.Value);
        return new AdminReply(r.Id, Messages.RecordDropped);
    }

    private async Task<AdminReply> DeleteAsync(AdminRequest r, CancellationToken ct)
    {
        var items = r.Items!;
        var results = new LeftoverResult[items.Length];
        UninstallRecord? record = null;
        if (records.Prepare())
        {
            record = records.Get(r.RecordId!.Value);
            if (record is not null && _time.GetUtcNow().UtcDateTime - record.CreatedUtc > UninstallActions.RecordLifetime) record = null;
        }
        if (record is null)
        {
            Array.Fill(results, Kept(NoRecordReason));
            return new AdminReply(r.Id, Messages.LeftoversDone, Results: results);
        }

        var run = new Run(record, system, apps);
        try
        {
            for (int i = 0; i < items.Length; i++)
            {
                try { results[i] = await ItemAsync(run, items[i], ct).ConfigureAwait(false); }
                catch (Exception) { results[i] = new LeftoverResult(Messages.ItemFailed); }
            }
        }
        finally { run.Dispose(); }
        return new AdminReply(r.Id, Messages.LeftoversDone, Results: results);
    }

    private static LeftoverResult Kept(string reason) => new(Messages.ItemKept, reason);

    // ---- one request's view of the record and of every other app ----

    private sealed class Run : IDisposable
    {
        public readonly UninstallRecord Record;
        private readonly ILeftoverSystem _system;
        private readonly Lazy<List<OtherApp>> _others;
        private readonly Lazy<List<string>> _folders;
        public List<IHeldNode> BackupHolds { get; } = [];
        public string? BackupFolder { get; set; }

        public Run(UninstallRecord record, ILeftoverSystem system, IInstalledApps apps)
        {
            Record = record; _system = system;
            _others = new(() => Others(apps));
            _folders = new(() => ScopeFolders());
        }
        public IReadOnlyList<OtherApp> OtherApps => _others.Value;
        /// <summary>The folders a Safe item may sit in: the record's InstallLocation and the folder of its uninstaller. One that is a protected folder, or holds one, covers nothing.</summary>
        public IReadOnlyList<string> Scope => _folders.Value;

        private List<string> ScopeFolders()
        {
            var folders = new List<string>();
            foreach (var candidate in new[] { Record.InstallLocation, Record.UninstallerFolder })
            {
                var folder = Folder(candidate, _system);
                if (folder is not null && PathRules.Refusal(folder) is null && !LeftoverPolicy.Denied(folder, _system)) folders.Add(folder);
            }
            return folders;
        }

        private List<OtherApp> Others(IInstalledApps apps)
        {
            var others = new List<OtherApp>();
            foreach (var app in apps.List())
            {
                // The app being removed isn't another app. Its key is named by the record, in the record's view.
                if (app.Hive == "HKLM" && app.View == Record.View && string.Equals(app.KeyName, Record.KeyName, StringComparison.OrdinalIgnoreCase)) continue;
                var folders = new List<string>();
                foreach (var folder in new[] { Folder(app.InstallLocation, _system), ExeFolder(app.UninstallString, _system), ExeFolder(app.DisplayIcon, _system) })
                    if (folder is not null && !folders.Contains(folder, StringComparer.OrdinalIgnoreCase)) folders.Add(folder);
                string name = Name(app.DisplayName, app.KeyName);
                others.Add(new OtherApp(name, folders, app.Hive == "HKLM" ? app.View : null, app.Hive == "HKLM" ? UninstallRoot + "\\" + app.KeyName : null));
            }
            return others;
        }

        public void Dispose() { foreach (var node in BackupHolds) node.Dispose(); BackupHolds.Clear(); }
    }

    private sealed record OtherApp(string Name, List<string> Folders, int? View, string? KeyPath);

    private static string Name(string? display, string key)
    {
        string name = string.IsNullOrWhiteSpace(display) ? key : display;
        name = new string([.. name.Where(c => !char.IsControl(c))]).Trim();
        return name.Length > 60 ? name[..60] : name.Length == 0 ? "another app" : name;
    }

    /// <summary>An absolute local folder from a registry value: quotes and a trailing backslash dropped, environment variables expanded; null for anything else.</summary>
    private static string? Folder(string? value, ILeftoverSystem system)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        string text = system.Expand(value.Trim().Trim('"')).TrimEnd('\\');
        return IsDrivePath(text) ? text : null;
    }

    /// <summary>The folder of the executable a command or icon path names ("C:\x\a.exe,0", "\"C:\x\a.exe\" /S"): up to the first .exe, or the quoted part.</summary>
    private static string? ExeFolder(string? value, ILeftoverSystem system)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        string text = system.Expand(value.Trim());
        string file;
        if (text[0] == '"') { int close = text.IndexOf('"', 1); file = close > 1 ? text[1..close] : text[1..]; }
        else
        {
            int exe = text.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            if (exe >= 0) file = text[..(exe + 4)];
            else { int comma = text.IndexOf(','); file = comma >= 0 ? text[..comma] : text; }
        }
        if (!IsDrivePath(file)) return null;
        string? folder = Path.GetDirectoryName(file);
        return string.IsNullOrEmpty(folder) ? null : folder.TrimEnd('\\');
    }

    private static bool IsDrivePath(string text) => text.Length >= 3 && char.IsAsciiLetter(text[0]) && text[1] == ':' && text[2] == '\\';

    private static bool Overlaps(string path, string folder) =>
        string.Equals(path, folder, StringComparison.OrdinalIgnoreCase) || PathRules.Contains(path, folder) || PathRules.Contains(folder, path);

    // ---- one item ----

    private async Task<LeftoverResult> ItemAsync(Run run, LeftoverItem item, CancellationToken ct)
    {
        if (!item.Ticked) return Kept(NotSelectedReason);
        return item.Kind == Messages.KindRegistryKey ? await KeyAsync(run, item, ct).ConfigureAwait(false) : File(run, item);
    }

    private LeftoverResult File(Run run, LeftoverItem item)
    {
        if (PathRules.Refusal(item.Path) is not null) return Kept(ProtectedReason);
        string path = item.Path.TrimEnd('\\');
        if (LeftoverPolicy.Denied(path, system)) return Kept(ProtectedReason);
        foreach (var other in run.OtherApps)
            if (other.Folders.Any(folder => Overlaps(path, folder))) return Kept(SharedReason(other.Name));
        // Safe means inside the record's own folders. Check first is the owner's own choice, ticked after the review; every other check still applies.
        if (item.Class == Messages.ClassSafe && !run.Scope.Any(folder => string.Equals(path, folder, StringComparison.OrdinalIgnoreCase) || PathRules.Contains(folder, path)))
            return Kept(NotThisAppsReason);

        // The folders above are held open (no FILE_SHARE_DELETE, no link followed) from here until the item is gone, so none can be renamed or
        // swapped for a junction in between.
        var holds = new List<IHeldNode>();
        try
        {
            var ancestors = new List<string>();
            for (var folder = Path.GetDirectoryName(path); !string.IsNullOrEmpty(folder); folder = Path.GetDirectoryName(folder)) ancestors.Add(folder);
            ancestors.Reverse();
            foreach (var folder in ancestors)
            {
                var node = probe.Open(folder, file: false);
                if (node is null) return system.Exists(path) ? new LeftoverResult(Messages.ItemFailed) : new LeftoverResult(Messages.ItemRemoved);
                holds.Add(node);
                if (node.IsReparsePoint || !node.PathMatches) return Kept(ChangedReason);
            }
            var parent = holds[^1];
            using (var node = probe.Open(path, item.Kind == Messages.KindFile))
            {
                if (node is null) return system.Exists(path) ? new LeftoverResult(Messages.ItemFailed) : new LeftoverResult(Messages.ItemRemoved);
                if (node.IsReparsePoint || !node.PathMatches) return Kept(ChangedReason);
                // Only what the owner can't delete himself. Chippi deletes the rest as him.
                if ((node.StandardGranted & DeleteRight) != 0 || (parent.StandardGranted & DeleteChildRight) != 0) return new LeftoverResult(Messages.ItemYours);
                if (node.OwnerSid is not ("S-1-5-18" or "S-1-5-32-544" or TrustedInstaller)) return Kept(ProtectedReason);
            }
            // The item's own handle is closed (a held folder can't be deleted); the folders above stay held while it goes.
            return system.Recycle(path) switch
            {
                RecycleOutcome.Removed => new LeftoverResult(Messages.ItemRemoved),
                RecycleOutcome.InUse => new LeftoverResult(Messages.ItemInUse),
                _ => system.Exists(path) ? new LeftoverResult(Messages.ItemFailed) : new LeftoverResult(Messages.ItemRemoved),
            };
        }
        finally { foreach (var node in holds) node.Dispose(); }
    }

    private async Task<LeftoverResult> KeyAsync(Run run, LeftoverItem item, CancellationToken ct)
    {
        int view = item.View!.Value;
        string path = item.Path;
        if (!LeftoverPolicy.KeyPathOk(path) || LeftoverPolicy.KeyDenied(path)) return Kept(ProtectedReason);
        foreach (var other in run.OtherApps)
            if (other.View == view && other.KeyPath is { } key && Overlaps(path, key)) return Kept(SharedReason(other.Name));
        if (!LeftoverPolicy.KeyNamedByRecord(run.Record, view, path)) return Kept(NotThisAppsReason);

        var found = registry.Probe(view, path);
        if (!found.Exists) return new LeftoverResult(Messages.ItemRemoved);
        if (found.Unsafe) return Kept(ChangedReason);
        if (found.StandardCanDelete) return new LeftoverResult(Messages.ItemYours);

        // Exported first: a key that can't be backed up isn't deleted.
        if (!await ExportAsync(run, view, path, ct).ConfigureAwait(false)) return new LeftoverResult(Messages.ItemFailed, BackupFailedReason);
        return registry.DeleteTree(view, path) ? new LeftoverResult(Messages.ItemRemoved) : new LeftoverResult(Messages.ItemFailed);
    }

    private async Task<bool> ExportAsync(Run run, int view, string path, CancellationToken ct)
    {
        if (!PrepareBackup(run)) return false;
        string file = Path.Combine(run.BackupFolder!, $"{run.Record.Id:N}-{Guid.NewGuid():N}.reg");
        string reg = Path.Combine(system.SystemDirectory, "reg.exe");
        string commandLine = $"\"{reg}\" export \"HKLM\\{path}\" \"{file}\" /y /reg:{view}";
        try
        {
            using var check = ExecutionCheck.Open(reg, probe);
            using var job = launcher.Start(reg, commandLine, system.SystemDirectory);
            check.Dispose();
            using var timeout = new CancellationTokenSource(ExportCeiling, _time);
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
            int exit = await job.WaitAsync(limit.Token).ConfigureAwait(false);
            return exit == 0 && system.Exists(file);
        }
        catch (Exception ex) when (ex is ExecutionRefusedException or System.ComponentModel.Win32Exception or IOException or UnauthorizedAccessException
            or InvalidOperationException or OperationCanceledException)
        { return false; }
    }

    /// <summary>
    /// The fixed backup folder, created level by level, each level held open without following a link until the request ends: nobody can swap a
    /// level for a junction between the check and reg.exe's write. The file name is random and new, so it can't be planted.
    /// </summary>
    private bool PrepareBackup(Run run)
    {
        if (run.BackupFolder is not null) return true;
        string folder = system.BackupFolder;
        if (PathRules.Refusal(folder) is not null) return false;
        var levels = new List<string>();
        for (var level = folder; !string.IsNullOrEmpty(level); level = Path.GetDirectoryName(level)) levels.Add(level);
        levels.Reverse();
        foreach (var level in levels)
        {
            var node = probe.Open(level, file: false);
            if (node is null)
            {
                if (!system.CreateFolder(level)) return false;
                node = probe.Open(level, file: false);
                if (node is null) return false;
            }
            run.BackupHolds.Add(node);
            if (node.IsReparsePoint || !node.PathMatches) return false;
        }
        run.BackupFolder = folder;
        return true;
    }
}

/// <summary>The pure rules of DeleteLeftovers: the fixed deny lists and what the record names.</summary>
public static class LeftoverPolicy
{
    private const string UninstallRoot = @"Software\Microsoft\Windows\CurrentVersion\Uninstall";

    /// <summary>
    /// True for a path on the fixed deny list: a protected root itself, anything that contains a protected root or tree, and anything in or equal
    /// to a protected tree. Whole-segment, case-insensitive.
    /// </summary>
    public static bool Denied(string path, ILeftoverSystem system)
    {
        path = path.TrimEnd('\\');
        foreach (var root in system.ProtectedRoots)
            if (Same(path, root) || PathRules.Contains(path, root)) return true;
        foreach (var tree in system.ProtectedTrees)
            if (Same(path, tree) || PathRules.Contains(tree, path) || PathRules.Contains(path, tree)) return true;
        return false;
    }

    private static bool Same(string a, string b) => string.Equals(a.TrimEnd('\\'), b.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);

    // ---- registry ----

    private static readonly string[] ProtectedKeyTrees =
    [
        @"Software\Classes", @"Software\Policies", @"Software\Chippi", @"Software\Wow6432Node", @"Software\Clients", @"Software\RegisteredApplications",
        @"Software\Microsoft\Windows NT",
    ];
    private static readonly string[] ProtectedKeyRoots =
    [
        "Software", @"Software\Microsoft", @"Software\Microsoft\Windows", @"Software\Microsoft\Windows\CurrentVersion", UninstallRoot,
    ];

    /// <summary>Under HKLM\Software, each segment plain: no empty segment, "." or "..", no quote or control character.</summary>
    public static bool KeyPathOk(string path)
    {
        if (!path.StartsWith(@"Software\", StringComparison.OrdinalIgnoreCase) || path.Length <= 9 || path.Length > Messages.MaxLeftoverPath) return false;
        foreach (var segment in path.Split('\\'))
            if (segment.Length == 0 || segment is "." or ".." || segment.Any(c => char.IsControl(c) || c == '"')) return false;
        return true;
    }

    /// <summary>The Software and Microsoft keys, anything one level under Microsoft, Windows' own keys, Classes, Policies, Chippi's own, and anything that holds one of them.</summary>
    public static bool KeyDenied(string path)
    {
        path = path.TrimEnd('\\');
        foreach (var root in ProtectedKeyRoots) if (Same(path, root)) return true;
        foreach (var tree in ProtectedKeyTrees) if (Same(path, tree) || PathRules.Contains(tree, path) || PathRules.Contains(path, tree)) return true;
        // Software\Microsoft\<one name>: Windows, Cryptography and the rest of Windows' own. A record's own Publisher and DisplayName can't point here.
        var segments = path.Split('\\');
        return segments.Length == 3 && segments[1].Equals("Microsoft", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Keys the record names: its own Uninstall key in its own view, and Software\DisplayName and Software\Publisher\DisplayName in either view, each with
    /// everything under it. Nothing else is the record's, whatever Chippi sends.
    /// </summary>
    public static bool KeyNamedByRecord(UninstallRecord record, int view, string path)
    {
        path = path.TrimEnd('\\');
        if (view == record.View && Segment(record.KeyName))
        {
            string uninstall = UninstallRoot + "\\" + record.KeyName;
            if (Same(path, uninstall) || PathRules.Contains(uninstall, path)) return true;
        }
        if (!Segment(record.DisplayName)) return false;
        string named = @"Software\" + record.DisplayName;
        if (Same(path, named) || PathRules.Contains(named, path)) return true;
        if (!Segment(record.Publisher)) return false;
        string byPublisher = @"Software\" + record.Publisher + "\\" + record.DisplayName;
        return Same(path, byPublisher) || PathRules.Contains(byPublisher, path);
    }

    private static bool Segment(string? name) => !string.IsNullOrWhiteSpace(name) && name.Length <= 120 && name is not ("." or "..")
        && name.All(c => !char.IsControl(c) && c is not ('\\' or '"' or '/'));
}
