// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors
using System.IO;
using System.Text.RegularExpressions;

namespace Chippi.Features.Uninstaller;

/// <summary>
/// The leftover scan: read-only, off the UI thread, as the standard user. It lists the former install folder, the app's folders in the user and
/// machine data folders, its shortcuts and its keys under <c>Software</c>, and gives each a class. Ownership is Chippi's own rule: Safe only when
/// verified (inside the recorded install folder, or named by the app's package or uninstall entry); a name match is Check first; anything another
/// installed app's folder or key is, contains or sits inside is Shared and never offered. With no record the scan offers only Check first.
/// </summary>
public sealed partial class LeftoverScanner(UninstallerSystem system, LeftoverRoots roots)
{
    private const int MaxEntries = 20_000, ProbeFiles = 48;
    private static readonly string[] Generic = ["bin", "app", "apps", "application", "program", "programs", "win32", "win64", "x64", "x86", "current", "release", "software", "data", "files"];
    private static readonly string[] DeniedKeys = ["microsoft", "windows", "classes", "policies", "wow6432node", "clients", "registeredapplications", "chippi", "windowsnt", "javasoft"];
    private static readonly EnumerationOptions Quiet = new() { IgnoreInaccessible = true, AttributesToSkip = 0, RecurseSubdirectories = false, ReturnSpecialDirectories = false };
    private const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall";

    private sealed record Other(string Name, string PublisherKey, IReadOnlyList<string> Folders, string? UninstallKeyPath);
    private sealed record Candidate(LeftoverKind Kind, string Path, RegHive? Hive, string? KeyPath, LeftoverClass Class, string Reason);

    /// <summary><paramref name="installed"/> is the list read now (the app itself, if its entry is still there, is left out).</summary>
    public LeftoverScan Scan(AppRecord record, IReadOnlyList<AppEntry> installed, CancellationToken ct = default)
    {
        bool hasRecord = record.HasRecord;
        var others = installed.Where(a => !(a.Source == record.Source && string.Equals(a.Key, record.Key, StringComparison.OrdinalIgnoreCase))).Select(Describe).ToArray();
        var names = NameKeys(record);
        var publisher = PublisherKeys(record.Publisher);
        var found = new List<Candidate>();

        InstallFolders(record, found);
        DataFolders(record, names, publisher, others, found, ct);
        Shortcuts(record, names, found, ct);
        Keys(record, names, publisher, others, found, ct);

        var items = new List<LeftoverItem>();
        foreach (var c in found)
        {
            ct.ThrowIfCancellationRequested();
            if (Forbidden(c)) continue;
            string? shared = SharedWith(c, others, publisher, record);
            var klass = shared is not null ? LeftoverClass.Shared : !hasRecord && c.Class == LeftoverClass.Safe ? LeftoverClass.CheckFirst : c.Class;
            items.Add(ToItem(c, klass, shared, ct));
        }
        // An item inside another listed item of the same kind is part of it, never its own row.
        var kept = items.Where(i => !items.Any(o => !ReferenceEquals(o, i) && o.Class != LeftoverClass.Shared && Inside(o, i))).ToList();
        var ordered = kept.GroupBy(i => i.Id).Select(g => g.First()).OrderBy(i => i.Class).ThenBy(i => i.Path, StringComparer.OrdinalIgnoreCase).ToList();
        return new LeftoverScan(record, ordered, hasRecord);
    }

    private static bool Inside(LeftoverItem parent, LeftoverItem child) =>
        parent.Kind == LeftoverKind.Key
            ? child.Kind == LeftoverKind.Key && parent.Hive == child.Hive && parent.KeyPath is { } pk && child.KeyPath is { } ck && PathRules.Strictly(pk, ck)
            : parent.Kind == LeftoverKind.Folder && child.Kind != LeftoverKind.Key && PathRules.Strictly(parent.Path, child.Path);

    // ---- candidates ----

    private void InstallFolders(AppRecord record, List<Candidate> found)
    {
        string? location = record.InstallLocation;
        if (location is not null && Directory.Exists(location))
            found.Add(new(LeftoverKind.Folder, PathRules.Norm(location), null, null, LeftoverClass.Safe, "Install folder"));
        foreach (string folder in record.Folders)
        {
            if (!Directory.Exists(folder) || (location is not null && PathRules.Contains(location, folder))) continue;
            found.Add(new(LeftoverKind.Folder, PathRules.Norm(folder), null, null, LeftoverClass.Safe, "Uninstaller folder"));
        }
        if (record.PackageFamilyName is { Length: > 0 } family)
        {
            string packages = Path.Combine(roots.LocalAppData, "Packages", family);
            if (Directory.Exists(packages)) found.Add(new(LeftoverKind.Folder, packages, null, null, LeftoverClass.Safe, "Package folder"));
        }
    }

    private void DataFolders(AppRecord record, HashSet<string> names, HashSet<string> publisher, Other[] others, List<Candidate> found, CancellationToken ct)
    {
        foreach (string root in new[] { roots.AppData, roots.LocalAppData, roots.ProgramData, Path.Combine(roots.LocalAppData, "Programs") })
        {
            ct.ThrowIfCancellationRequested();
            foreach (string dir in Children(root))
            {
                string key = UninstallerText.NameKey(Path.GetFileName(dir));
                if (names.Contains(key)) found.Add(new(LeftoverKind.Folder, dir, null, null, LeftoverClass.CheckFirst, "App folder"));
                else if (publisher.Contains(key))
                {
                    foreach (string child in Children(dir))
                        if (names.Contains(UninstallerText.NameKey(Path.GetFileName(child)))) found.Add(new(LeftoverKind.Folder, child, null, null, LeftoverClass.CheckFirst, "App folder"));
                    // The publisher's own folder is never ours to take: it is listed only to say another app uses it.
                    if (others.Any(o => publisher.Contains(o.PublisherKey)))
                        found.Add(new(LeftoverKind.Folder, dir, null, null, LeftoverClass.CheckFirst, "Publisher folder"));
                }
            }
        }
    }

    private void Shortcuts(AppRecord record, HashSet<string> names, List<Candidate> found, CancellationToken ct)
    {
        var ours = new List<string>();
        if (record.InstallLocation is not null) ours.Add(record.InstallLocation);
        ours.AddRange(record.Folders);
        foreach (string menu in roots.StartMenuPrograms)
        {
            foreach (string lnk in Files(menu, "*.lnk", 3)) { ct.ThrowIfCancellationRequested(); ShortcutCandidate(lnk, ours, names, found); }
            foreach (string dir in Children(menu))
                if (names.Contains(UninstallerText.NameKey(Path.GetFileName(dir))) && Files(dir, "*.lnk", 1).All(l => !TargetExists(l)))
                    found.Add(new(LeftoverKind.Folder, dir, null, null, LeftoverClass.CheckFirst, "Start menu folder"));
        }
        foreach (string desktop in roots.Desktops)
            foreach (string lnk in Files(desktop, "*.lnk", 1)) { ct.ThrowIfCancellationRequested(); ShortcutCandidate(lnk, ours, names, found); }
    }

    private void ShortcutCandidate(string lnk, List<string> ours, HashSet<string> names, List<Candidate> found)
    {
        string? target = system.Shortcuts.Target(lnk);
        if (target is not null && ours.Any(o => PathRules.Contains(o, target)))
            found.Add(new(LeftoverKind.File, lnk, null, null, LeftoverClass.Safe, "Shortcut"));
        else if (names.Contains(UninstallerText.NameKey(Path.GetFileNameWithoutExtension(lnk))) && (target is null || !File.Exists(target) && !Directory.Exists(target)))
            found.Add(new(LeftoverKind.File, lnk, null, null, LeftoverClass.CheckFirst, "Shortcut"));
    }

    private bool TargetExists(string lnk) => system.Shortcuts.Target(lnk) is { } t && (File.Exists(t) || Directory.Exists(t));

    private void Keys(AppRecord record, HashSet<string> names, HashSet<string> publisher, Other[] others, List<Candidate> found, CancellationToken ct)
    {
        var hkcu = system.LeftoverRegistry;
        // The app's own uninstall entry, when its uninstaller left it behind.
        if (record.Source != AppSource.Store)
        {
            var hive = record.Source switch { AppSource.Machine64 => RegHive.Machine64, AppSource.Machine32 => RegHive.Machine32, _ => RegHive.User };
            string own = UninstallKey + "\\" + record.Key;
            if (hkcu.Exists(hive, own)) found.Add(new(LeftoverKind.Key, Display(hive, own), hive, own, LeftoverClass.Safe, "Uninstall entry"));
        }
        foreach (var hive in new[] { RegHive.User, RegHive.Machine64, RegHive.Machine32 })
        {
            ct.ThrowIfCancellationRequested();
            foreach (string sub in hkcu.SubKeys(hive, "Software"))
            {
                string key = UninstallerText.NameKey(sub);
                if (DeniedKeys.Contains(key)) continue;
                string path = "Software\\" + sub;
                if (names.Contains(key)) found.Add(new(LeftoverKind.Key, Display(hive, path), hive, path, LeftoverClass.CheckFirst, "App key"));
                else if (publisher.Contains(key))
                {
                    foreach (string child in hkcu.SubKeys(hive, path))
                        if (names.Contains(UninstallerText.NameKey(child)))
                            found.Add(new(LeftoverKind.Key, Display(hive, path + "\\" + child), hive, path + "\\" + child, LeftoverClass.CheckFirst, "App key"));
                    // The publisher's own key is listed only to say another app uses it.
                    if (others.Any(o => publisher.Contains(o.PublisherKey)))
                        found.Add(new(LeftoverKind.Key, Display(hive, path), hive, path, LeftoverClass.CheckFirst, "Publisher key"));
                }
            }
        }
    }

    private static string Display(RegHive hive, string path) => (hive == RegHive.User ? "HKCU\\" : "HKLM\\") + path;

    // ---- rules ----

    private bool Forbidden(Candidate c)
    {
        if (c.Kind == LeftoverKind.Key)
        {
            var parts = c.KeyPath!.Split('\\');
            if (parts.Length < 2 || !parts[0].Equals("Software", StringComparison.OrdinalIgnoreCase) || parts.Any(string.IsNullOrWhiteSpace)) return true;
            // The app's own uninstall entry is the one key under Microsoft that is its own.
            return c.Reason != "Uninstall entry" && DeniedKeys.Contains(UninstallerText.NameKey(parts[1]));
        }
        string path = c.Path;
        if (PathRules.Contains(roots.WindowsDirectory, path)) return true;
        if (roots.ProtectedTrees.Any(t => PathRules.Contains(t, path) || PathRules.Contains(path, t))) return true;
        // Never a container the user's PC shares, and never one that holds one.
        return roots.Containers().Any(r => PathRules.Contains(path, r)) || path.Length < 4 || PathRules.Norm(path).TrimEnd('\\').Length <= 3;
    }

    private string? SharedWith(Candidate c, Other[] others, HashSet<string> publisher, AppRecord record)
    {
        if (c.Kind == LeftoverKind.Key)
        {
            foreach (var other in others)
            {
                if (other.UninstallKeyPath is { } own && c.KeyPath is { } key && (PathRules.Contains(key, own) || PathRules.Contains(own, key))) return other.Name;
                if (c.Reason == "Publisher key" && publisher.Contains(other.PublisherKey)) return other.Name;
            }
            return null;
        }
        foreach (var other in others)
        {
            if (other.Folders.Any(f => PathRules.Contains(c.Path, f) || PathRules.Contains(f, c.Path))) return other.Name;
            if (c.Reason == "Publisher folder" && publisher.Contains(other.PublisherKey)) return other.Name;
        }
        return null;
    }

    private Other Describe(AppEntry app)
    {
        var folders = new List<string>();
        void Add(string? folder) { if (!string.IsNullOrWhiteSpace(folder) && folder.Length > 3) folders.Add(PathRules.Norm(folder)); }
        Add(app.InstallLocation);
        Add(UserCommand.FolderOf(app));
        Add(app.IconPath is { } icon ? Path.GetDirectoryName(icon) : null);
        string? uninstallKey = app.IsMachine ? UninstallKey + "\\" + app.Key : null;
        return new Other(app.Name, ShortKey(app.Publisher), folders, uninstallKey);
    }

    // ---- names ----

    private static HashSet<string> NameKeys(AppRecord record)
    {
        var set = new HashSet<string>();
        void Add(string? text)
        {
            string key = UninstallerText.NameKey(text);
            if (key.Length >= 3 && !Generic.Contains(key)) set.Add(key);
        }
        Add(record.Name);
        Add(StripVersion(record.Name));
        // "Mozilla Firefox" by "Mozilla" is also "Firefox": the folder under the publisher's.
        string full = UninstallerText.NameKey(StripVersion(record.Name)), maker = ShortKey(record.Publisher);
        if (maker.Length >= 3 && full.StartsWith(maker, StringComparison.Ordinal)) Add(full[maker.Length..]);
        if (record.InstallLocation is { } location) Add(Path.GetFileName(location.TrimEnd('\\')));
        return set;
    }

    private static HashSet<string> PublisherKeys(string publisher)
    {
        var set = new HashSet<string>();
        string full = UninstallerText.NameKey(publisher), shorter = ShortKey(publisher);
        if (full.Length >= 3) set.Add(full);
        if (shorter.Length >= 3) set.Add(shorter);
        return set;
    }

    /// <summary>"Mozilla Corporation" and "Mozilla" are the same publisher: the common company words are dropped.</summary>
    internal static string ShortKey(string publisher)
    {
        string text = Suffix().Replace(publisher.Trim(), "").Trim().Trim(',', '.', ' ');
        return UninstallerText.NameKey(text);
    }

    /// <summary>"Python 3.13.0" is "Python"; "Foo (x64)" is "Foo"; "Firefox 132.0.1 (x64 en-US)" is "Firefox".</summary>
    internal static string StripVersion(string name)
    {
        string s = Parens().Replace(name, "").Trim();
        s = Version().Replace(s, "").Trim();
        return s;
    }

    [GeneratedRegex(@"[,\s]+(inc|llc|ltd|limited|corporation|corp|gmbh|co|company|foundation|team|project|software|technologies|ab|ag|sa|bv|s\.r\.o)\b\.?", RegexOptions.IgnoreCase)]
    private static partial Regex Suffix();

    [GeneratedRegex(@"\s*\([^)]*\)")]
    private static partial Regex Parens();

    [GeneratedRegex(@"[\s_-]+(v?\d+(\.\d+)*|x64|x86|64-bit|32-bit)\b.*$", RegexOptions.IgnoreCase)]
    private static partial Regex Version();

    // ---- describing an item ----

    private LeftoverItem ToItem(Candidate c, LeftoverClass klass, string? sharedWith, CancellationToken ct)
    {
        string id = LeftoverItem.IdFor(c.Kind, c.Kind == LeftoverKind.Key ? c.Path : c.Path);
        if (c.Kind == LeftoverKind.Key)
            return new(id, c.Kind, c.Path, c.Hive, c.KeyPath, klass, c.Reason, sharedWith, null, false, false, c.Hive != RegHive.User, false);
        bool link = IsLink(c.Path);
        long? bytes = null;
        bool inUse = false;
        if (!link) (bytes, inUse) = c.Kind == LeftoverKind.Folder ? Measure(c.Path, ct) : MeasureFile(c.Path);
        bool noBin = !link && bytes is { } b && !system.Recycler.CanRecycle(c.Path, b);
        bool admin = roots.AdminRoots.Any(r => PathRules.Contains(r, c.Path));
        return new(id, c.Kind, c.Path, null, null, klass, c.Reason, sharedWith, bytes, inUse, noBin, admin, link);
    }

    private static (long? Bytes, bool InUse) MeasureFile(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return (info.Length, Held(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return (null, false); }
    }

    /// <summary>The folder's size, links never followed, bounded; and whether a few of its programs are held open.</summary>
    private static (long? Bytes, bool InUse) Measure(string path, CancellationToken ct)
    {
        long total = 0;
        int seen = 0, probed = 0;
        bool inUse = false;
        var pending = new Stack<string>();
        pending.Push(path);
        while (pending.Count > 0 && seen < MaxEntries)
        {
            ct.ThrowIfCancellationRequested();
            string dir = pending.Pop();
            try
            {
                foreach (var entry in new DirectoryInfo(dir).EnumerateFileSystemInfos("*", Quiet))
                {
                    if (++seen > MaxEntries) break;
                    if (entry.Attributes.HasFlag(FileAttributes.ReparsePoint)) continue;
                    if (entry is DirectoryInfo sub) pending.Push(sub.FullName);
                    else if (entry is FileInfo file)
                    {
                        total += file.Length;
                        if (!inUse && probed < ProbeFiles && IsProgram(file.Extension)) { probed++; inUse = Held(file.FullName); }
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        return (total, inUse);
    }

    private static bool IsProgram(string extension) => extension.Equals(".exe", StringComparison.OrdinalIgnoreCase) || extension.Equals(".dll", StringComparison.OrdinalIgnoreCase)
        || extension.Equals(".sys", StringComparison.OrdinalIgnoreCase);

    /// <summary>Opening a file for reading with sharing denied fails while another program holds it: it changes nothing.</summary>
    private static bool Held(string path)
    {
        try { using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None); return false; }
        catch (IOException) { return true; }
        catch (UnauthorizedAccessException) { return false; }
    }

    private static bool IsLink(string path)
    {
        try { return new FileInfo(path).Attributes.HasFlag(FileAttributes.ReparsePoint); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }

    private static IEnumerable<string> Children(string root)
    {
        try
        {
            if (!Directory.Exists(root)) return [];
            return [.. new DirectoryInfo(root).EnumerateDirectories("*", Quiet).Select(d => d.FullName)];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return []; }
    }

    private static IEnumerable<string> Files(string root, string pattern, int depth)
    {
        var result = new List<string>();
        void Walk(string dir, int left)
        {
            try
            {
                if (!Directory.Exists(dir)) return;
                var info = new DirectoryInfo(dir);
                result.AddRange(info.EnumerateFiles(pattern, Quiet).Select(f => f.FullName));
                if (left > 1)
                    foreach (var sub in info.EnumerateDirectories("*", Quiet).Where(d => !d.Attributes.HasFlag(FileAttributes.ReparsePoint))) Walk(sub.FullName, left - 1);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        Walk(root, depth);
        return result;
    }
}
