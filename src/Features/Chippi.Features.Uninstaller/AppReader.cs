// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors
using System.Globalization;
using System.Text.RegularExpressions;

namespace Chippi.Features.Uninstaller;

/// <summary>
/// Reads the installed apps as the standard user, from the machine's Uninstall key (both views), the user's own and the Store's packages. System
/// components and updates are skipped as the entries ask (SystemComponent, ParentKeyName); Windows components, Chippi and frameworks are listed
/// with no verb. The same app registered twice is one row. Call it off the UI thread: it reads the registry and the package list.
/// </summary>
public sealed partial class AppReader(IAppRegistry registry, IPackageSource packages, Func<string?, bool>? isSelfLocation = null)
{
    private static readonly string[] UpdateTypes = ["Update", "Hotfix", "Security Update", "Update Rollup", "Service Pack", "Critical Update"];

    private readonly Func<string?, bool> _isSelf = isSelfLocation ?? (location =>
        !string.IsNullOrEmpty(location) && AppContext.BaseDirectory.StartsWith(location.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase));

    public IReadOnlyList<AppEntry> Read(CancellationToken ct = default)
    {
        var all = new List<AppEntry>();
        foreach (var source in new[] { AppSource.Machine64, AppSource.Machine32, AppSource.User })
        {
            foreach (var raw in registry.Read(source))
            {
                ct.ThrowIfCancellationRequested();
                if (Registered(source, raw) is { } entry) all.Add(entry);
            }
        }
        foreach (var package in packages.List())
        {
            ct.ThrowIfCancellationRequested();
            if (Package(package) is { } entry) all.Add(entry);
        }
        var seen = new HashSet<(string, string, string)>();
        var list = new List<AppEntry>();
        foreach (var entry in all)   // registry sources come first, so they win over the same app's package
            if (seen.Add((UninstallerText.NameKey(entry.Name), entry.Version, UninstallerText.NameKey(entry.Publisher)))) list.Add(entry);
        list.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        return list;
    }

    /// <summary>One key as an app, or null when it isn't one the list shows.</summary>
    public AppEntry? Registered(AppSource source, RawApp raw)
    {
        string name = (raw.Str("DisplayName") ?? "").Trim();
        if (name.Length == 0) return null;
        if (raw.Int("SystemComponent") == 1 || !string.IsNullOrWhiteSpace(raw.Str("ParentKeyName"))) return null;
        if (raw.Str("ReleaseType") is { } type && UpdateTypes.Contains(type.Trim(), StringComparer.OrdinalIgnoreCase)) return null;
        if (UpdateName().IsMatch(name)) return null;
        string publisher = (raw.Str("Publisher") ?? "").Trim();
        string? location = Clean(raw.Str("InstallLocation"));
        bool self = name.Equals("Chippi", StringComparison.OrdinalIgnoreCase) || raw.Key.Equals("Chippi", StringComparison.OrdinalIgnoreCase) || _isSelf(location);
        bool component = !self && ComponentName().IsMatch(name) && publisher.Contains("Microsoft", StringComparison.OrdinalIgnoreCase);
        bool msi = raw.Int("WindowsInstaller") == 1 && IsProductCode(raw.Key);
        long? size = raw.Int("EstimatedSize") is { } kb and > 0 ? kb * 1024L : null;
        return new AppEntry((source switch { AppSource.Machine64 => "m64:", AppSource.Machine32 => "m32:", _ => "u:" }) + raw.Key, name, publisher,
            (raw.Str("DisplayVersion") ?? "").Trim(), size, ParseDate(raw.Str("InstallDate")), IconFile(raw.Str("DisplayIcon")), source, raw.Key, location,
            self ? AppKind.Self : component ? AppKind.Component : AppKind.App)
        {
            UninstallString = Trim(raw.Str("UninstallString")), QuietUninstallString = Trim(raw.Str("QuietUninstallString")),
            IsMsi = msi, ProductCode = msi ? raw.Key : null, NoRemove = raw.Int("NoRemove") == 1,
        };
    }

    private static AppEntry? Package(RawPackage p)
    {
        if (p.IsResource || string.IsNullOrWhiteSpace(p.DisplayName) || p.DisplayName.StartsWith("ms-resource:", StringComparison.OrdinalIgnoreCase)) return null;
        return new AppEntry("s:" + p.FullName, p.DisplayName.Trim(), p.Publisher.Trim(), p.Version, null, p.Installed, null, AppSource.Store, p.FullName,
            p.InstallLocation, p.IsFramework || p.IsSystem ? AppKind.Component : AppKind.App) { PackageFamilyName = p.FamilyName };
    }

    private static string? Trim(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    private static string? Clean(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        string p = Environment.ExpandEnvironmentVariables(path.Trim().Trim('"')).TrimEnd('\\');
        return p.Length < 3 ? null : p;
    }

    /// <summary>"C:\app\a.exe,0" and a quoted path with an index are the file; the index is dropped so the icon loader gets a path.</summary>
    public static string? IconFile(string? icon)
    {
        if (string.IsNullOrWhiteSpace(icon)) return null;
        string s = Environment.ExpandEnvironmentVariables(icon.Trim());
        if (s.StartsWith('"')) { int end = s.IndexOf('"', 1); s = end > 0 ? s[1..end] : s[1..]; }
        else { int comma = s.LastIndexOf(','); if (comma > 1 && int.TryParse(s[(comma + 1)..].Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out _)) s = s[..comma]; }
        return s.Length == 0 ? null : s;
    }

    private static DateTime? ParseDate(string? text) =>
        text is { Length: 8 } && DateTime.TryParseExact(text, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;

    public static bool IsProductCode(string name) => ProductCode().IsMatch(name);

    [GeneratedRegex(@"^\{[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}\}$")]
    private static partial Regex ProductCode();

    [GeneratedRegex(@"^(Security Update|Update|Hotfix|Cumulative Update) for .*\bKB\d+|^KB\d{6,}", RegexOptions.IgnoreCase)]
    private static partial Regex UpdateName();

    [GeneratedRegex(@"\bRuntime\b|Redistributable|\.NET|Windows SDK|Software Development Kit|Visual C\+\+|WebView2|Update Health Tools|ODBC|OLE DB|Desktop Runtime|Windows Driver|Windows App Certification", RegexOptions.IgnoreCase)]
    private static partial Regex ComponentName();
}
