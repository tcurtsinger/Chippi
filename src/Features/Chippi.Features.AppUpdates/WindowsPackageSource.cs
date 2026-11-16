// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Management.Deployment;
using Microsoft.Win32;

namespace Chippi.Features.AppUpdates;

/// <summary>Official WinGet projection over its registered local COM server. Construction performs no native work.</summary>
public sealed class WindowsPackageSource(bool allowUpdates = true) : IPackageSource, IPackageSourceStatus
{
    public string? Warning { get; private set; }

    public Task<IReadOnlyList<PackageUpdate>> CheckAsync(bool includeUnknown, CancellationToken ct = default) => Task.Run(async () =>
    {
        ct.ThrowIfCancellationRequested();
        var manager = WingetObjects.Manager();
        var updates = new List<PackageUpdate>();
        var failed = new List<string>(); int connected = 0;
        foreach (string source in new[] { "winget", "msstore" })
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var catalog = await OpenAsync(manager, source, ct).ConfigureAwait(false);
                if (catalog is null) continue;
                var find = WingetObjects.Find();
                var found = await catalog.FindPackagesAsync(find).AsTask(ct).ConfigureAwait(false);
                if (found.Status != FindPackagesResultStatus.Ok || found.WasLimitExceeded) throw new InvalidOperationException("Package check failed.");
                connected++;
                foreach (var match in CopyVector(found.Matches))
                {
                    ct.ThrowIfCancellationRequested();
                    var package = match.CatalogPackage;
                    var installed = package.InstalledVersion; var available = package.DefaultInstallVersion;
                    if (installed is null || available is null) continue;
                    string version = installed.Version;
                    bool unknown = string.IsNullOrWhiteSpace(version) || version.Equals("Unknown", StringComparison.OrdinalIgnoreCase);
                    if (unknown && !includeUnknown || !unknown && !package.IsUpdateAvailable) continue;
                    var scope = ParseScope(installed.GetMetadata(PackageVersionMetadataField.InstalledScope));
                    var identities = CopyVector(installed.ProductCodes);
                    var registry = ReadInstalledIdentity(identities, scope);
                    updates.Add(new(package.Id, package.Name, version, available.Version, source, scope)
                    {
                        IconPath = registry.Icon,
                        InstallLocation = Nonempty(installed.GetMetadata(PackageVersionMetadataField.InstalledLocation)) ?? registry.Location,
                        PackageFamilyName = CopyVector(installed.PackageFamilyNames).FirstOrDefault(),
                        ProductCodes = Array.AsReadOnly(identities), Channel = available.Channel
                    });
                }
            }
            catch (Exception ex) when (ex is COMException or InvalidOperationException or UnauthorizedAccessException)
            { failed.Add(source == "msstore" ? "Microsoft Store" : "winget"); }
        }
        Warning = failed.Count == 0 ? null : "Check failed · " + string.Join(", ", failed);
        if (connected == 0) throw new InvalidOperationException("Package check failed.");
        return (IReadOnlyList<PackageUpdate>)Array.AsReadOnly(updates.ToArray());
    }, ct);

    public Task<PackageInstallResult> UpdateAsync(PackageUpdate package, IProgress<PackageInstallProgress>? progress = null, CancellationToken ct = default)
    {
        // Guard before activation: a dev profile or explicit read-only probe cannot start any installer or consent UI.
        if (!allowUpdates || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CHIPPI_DEV_PROFILE")))
            return Task.FromResult(new PackageInstallResult(UpdateState.Failed, "Updates are unavailable in this profile"));
        if (!package.IsStore && package.Scope != UpdateScope.User)
            return Task.FromResult(new PackageInstallResult(UpdateState.NeedsAdmin, "Needs admin"));
        if (package.Source is not ("winget" or "msstore"))
            return Task.FromResult(new PackageInstallResult(UpdateState.Failed, "Update source isn’t supported"));
        return Task.Run(async () =>
        {
            ct.ThrowIfCancellationRequested();
            var manager = WingetObjects.Manager();
            var catalog = await OpenAsync(manager, package.Source, ct).ConfigureAwait(false);
            if (catalog is null) return new PackageInstallResult(UpdateState.Failed, "Update source unavailable");
            var find = WingetObjects.Find(); var exact = WingetObjects.Filter();
            exact.Field = PackageMatchField.Id; exact.Option = PackageFieldMatchOption.EqualsCaseInsensitive; exact.Value = package.Id;
            find.Filters.Add(exact);
            var found = await catalog.FindPackagesAsync(find).AsTask(ct).ConfigureAwait(false);
            if (found.Status != FindPackagesResultStatus.Ok) return new PackageInstallResult(UpdateState.Failed, "Check failed");
            var matches = CopyVector(found.Matches).Select(m => m.CatalogPackage).Where(p => p.Id.Equals(package.Id, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (matches.Length != 1 || matches[0].InstalledVersion is not { } installed)
                return new PackageInstallResult(UpdateState.Skipped, "No update available");
            var current = matches[0];
            if (!current.IsUpdateAvailable && !package.UnknownVersion) return new PackageInstallResult(UpdateState.Skipped, "No update available");
            if (!package.IsStore && ParseScope(installed.GetMetadata(PackageVersionMetadataField.InstalledScope)) != UpdateScope.User)
                return new PackageInstallResult(UpdateState.Failed, "Installed scope changed · Check again");
            if (!string.Equals(installed.Version, package.InstalledVersion, StringComparison.Ordinal))
                return new PackageInstallResult(UpdateState.Skipped, "Installed version changed · Check again");
            var version = CopyVector(current.AvailableVersions).FirstOrDefault(v => v.Version == package.AvailableVersion && v.Channel == package.Channel);
            if (version is null) return new PackageInstallResult(UpdateState.Skipped, "Update version changed · Check again");
            var target = current.GetPackageVersionInfo(version);
            var options = WingetObjects.Install();
            options.PackageVersionId = version; options.PackageInstallScope = PackageInstallScope.User;
            options.PackageInstallMode = PackageInstallMode.Silent; options.AcceptPackageAgreements = true;
            options.AllowHashMismatch = false; options.AllowUpgradeToUnknownVersion = package.UnknownVersion; options.Force = false;
            options.AuthenticationArguments = WingetObjects.SilentAuthentication();
            var installer = target.GetApplicableInstaller(options);
            if (installer is null) return new PackageInstallResult(UpdateState.Failed, "No matching user installer");
            if (!UserInstallerAllowed(installer)) return new PackageInstallResult(UpdateState.NeedsAdmin, "Installer needs admin");
            ct.ThrowIfCancellationRequested();
            var operation = manager.UpgradePackageAsync(current, options);
            operation.Progress = (_, value) => progress?.Report(TranslateProgress(value));
            // Once started, cancellation only affects later queue items, never this installer or its cleanup.
            var result = await operation.AsTask().ConfigureAwait(false);
            return TranslateResult(result.Status, result.RebootRequired, result.ExtendedErrorCode?.HResult, result.InstallerErrorCode);
        }, ct);
    }

    private static async Task<PackageCatalog?> OpenAsync(PackageManager manager, string source, CancellationToken ct)
    {
        var reference = manager.GetPackageCatalogByName(source);
        if (reference is null) return null;
        reference.AuthenticationArguments = WingetObjects.SilentAuthentication();
        // Check never silently accepts new source terms or starts interactive authentication.
        reference.AcceptSourceAgreements = false;
        var options = WingetObjects.Composite(); options.Catalogs.Add(reference);
        options.CompositeSearchBehavior = CompositeSearchBehavior.LocalCatalogs;
        var composite = manager.CreateCompositePackageCatalog(options);
        var connected = await composite.ConnectAsync().AsTask(ct).ConfigureAwait(false);
        if (connected.Status != ConnectResultStatus.Ok || connected.PackageCatalog is null) throw new InvalidOperationException("Package source unavailable.");
        return connected.PackageCatalog;
    }

    internal static UpdateScope ParseScope(string? scope) => scope?.ToLowerInvariant() switch
    { "user" => UpdateScope.User, "machine" or "system" => UpdateScope.Machine, _ => UpdateScope.Unknown };

    internal static bool UserInstallerAllowed(PackageInstallerInfo installer) => UserInstallerAllowed(installer.Scope, installer.ElevationRequirement, installer.InstallerType);

    internal static bool UserInstallerAllowed(PackageInstallerScope scope, ElevationRequirement elevation, PackageInstallerType type)
    {
        if (scope == PackageInstallerScope.System || elevation is ElevationRequirement.ElevationRequired or ElevationRequirement.ElevatesSelf) return false;
        // The selected install options require User scope. MSIX/Store servicing does not invoke a self-elevating EXE.
        if (type is PackageInstallerType.Msix or PackageInstallerType.MSStore) return true;
        // A confirmed user installer may ask for its own permission; unknown elevation alone does not change its scope.
        return scope == PackageInstallerScope.User;
    }

    internal static PackageInstallProgress TranslateProgress(InstallProgress value)
    {
        string stage = value.State switch { PackageInstallProgressState.Downloading => "Downloading", PackageInstallProgressState.Installing => "Installing", PackageInstallProgressState.PostInstall => "Finishing", _ => "Queued" };
        double? fraction = value.State switch { PackageInstallProgressState.Downloading => value.DownloadProgress, PackageInstallProgressState.Installing when value.InstallationProgress > 0 => value.InstallationProgress, _ => null };
        return new(fraction is { } p && double.IsFinite(p) ? Math.Clamp(p, 0, 1) : null, stage);
    }

    internal static PackageInstallResult TranslateResult(InstallResultStatus status, bool reboot, int? error, uint installerCode) => status switch
    {
        InstallResultStatus.Ok => reboot ? new(UpdateState.RestartRequired, "Restart to finish", error) : new(UpdateState.Updated, Code: error),
        InstallResultStatus.NoApplicableUpgrade => new(UpdateState.Skipped, "No update available", error),
        InstallResultStatus.BlockedByPolicy => new(UpdateState.Failed, "Update blocked by policy", error),
        InstallResultStatus.DownloadError => new(UpdateState.Failed, "Download failed", error),
        InstallResultStatus.NoApplicableInstallers => new(UpdateState.Failed, "No matching installer", error),
        _ when installerCode is 3010 or 1641 => new(UpdateState.RestartRequired, "Restart to finish", unchecked((int)installerCode)),
        _ => new(UpdateState.Failed, "Update failed" + (error is { } code && code != 0 ? $" · 0x{code:X8}" : ""), error)
    };

    private static (string? Icon, string? Location) ReadInstalledIdentity(IEnumerable<string> productCodes, UpdateScope scope)
    {
        foreach (string code in productCodes.Where(c => !string.IsNullOrWhiteSpace(c) && !c.Contains('\\') && !c.Contains('/')).Take(32))
        foreach (var hive in scope == UpdateScope.User ? new[] { RegistryHive.CurrentUser } : new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            try
            {
                using var root = RegistryKey.OpenBaseKey(hive, view);
                using var key = root.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\" + code);
                if (key is null) continue;
                return (IconFile(key.GetValue("DisplayIcon") as string), Nonempty(key.GetValue("InstallLocation") as string));
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException) { }
        }
        return (null, null);
    }

    internal static string? IconFile(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        string path = raw.Trim();
        if (path.StartsWith('"')) { int end = path.IndexOf('"', 1); if (end < 0) return null; path = path[1..end]; }
        else { int comma = path.LastIndexOf(','); if (comma > 0 && int.TryParse(path[(comma + 1)..].Trim(), out _)) path = path[..comma]; }
        try { return Path.IsPathFullyQualified(path) && File.Exists(path) ? path : null; }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException) { return null; }
    }
    private static string? Nonempty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    // IVectorView guarantees indexed access. Its optional iterable projection is not available on every App Installer build.
    internal static T[] CopyVector<T>(IReadOnlyList<T> values)
    {
        var copy = new T[values.Count];
        for (int i = 0; i < copy.Length; i++) copy[i] = values[i];
        return copy;
    }
}

/// <summary>Use the official package's typed native factory, which activates App Installer's out-of-process server.</summary>
internal static class WingetObjects
{
    public static PackageManager Manager()
    {
        try { return new PackageManager(); }
        catch (COMException ex) when (ex.HResult is unchecked((int)0x80040154) or unchecked((int)0x800401F3))
        { throw new WingetUnavailableException("App updates need winget", ex); }
    }
    public static FindPackagesOptions Find() => new();
    public static CreateCompositePackageCatalogOptions Composite() => new();
    public static PackageMatchFilter Filter() => new();
    public static InstallOptions Install() => new();
    public static AuthenticationArguments SilentAuthentication() => new() { AuthenticationMode = AuthenticationMode.Silent };
}
