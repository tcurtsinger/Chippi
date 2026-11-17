// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors
namespace Chippi.Features.AppUpdates;

public enum UpdateScope { Unknown, User, Machine }
public enum UpdateState { Available, Queued, Updating, Updated, Failed, NeedsAdmin, RestartRequired, Skipped, Cancelled }

/// <summary>A versioned, source-qualified update. Installed identity fields are hints for read-only app/window matching, never commands.</summary>
public sealed record PackageUpdate(string Id, string Name, string InstalledVersion, string AvailableVersion, string Source, UpdateScope Scope)
{
    public string Key => Source + "/" + Id;
    public bool UnknownVersion => string.IsNullOrWhiteSpace(InstalledVersion) || InstalledVersion.Equals("Unknown", StringComparison.OrdinalIgnoreCase);
    public bool IsStore => Source.Equals("msstore", StringComparison.OrdinalIgnoreCase);
    public string? IconPath { get; init; }
    public string? InstallLocation { get; init; }
    public string? PackageFamilyName { get; init; }
    public IReadOnlyList<string> ProductCodes { get; init; } = [];
    public string Channel { get; init; } = "";
}

public sealed record PackageInstallProgress(double? Fraction, string? Stage = null);
public sealed record PackageInstallResult(UpdateState State, string? Reason = null, int? Code = null);
public sealed record UpdateRow(PackageUpdate Package, UpdateState State = UpdateState.Available, double? Progress = null, string? Reason = null);
public sealed record SkippedUpdate(string Key, string Id, string Name, string? Version);
public sealed record UpdateHistoryEntry(string Id, string Name, string FromVersion, string ToVersion, DateTimeOffset At, UpdateState State, string? Reason = null);

public sealed record AppUpdatesPreferences
{
    public bool ShowUnknownVersions { get; init; }
    public IReadOnlyList<SkippedUpdate> Skipped { get; init; } = [];
    public IReadOnlyList<UpdateHistoryEntry> History { get; init; } = [];
}

/// <summary>The caller shows and confirms this exact snapshot before passing it to RunAsync.</summary>
public sealed record UpdateBatch(IReadOnlyList<PackageUpdate> Packages);

public interface IPackageSource
{
    Task<IReadOnlyList<PackageUpdate>> CheckAsync(bool includeUnknown, CancellationToken ct = default);
    /// <summary>Only user/Store updates. Machine and unknown scope must use the shared admin helper. Active installers finish even if ct is cancelled.</summary>
    Task<PackageInstallResult> UpdateAsync(PackageUpdate package, IProgress<PackageInstallProgress>? progress = null, CancellationToken ct = default);
}

public interface IPackageSourceStatus
{
    string? Warning { get; }
    /// <summary>Why each failed source failed on the last check, for the log: the warning names only the source.</summary>
    IReadOnlyList<string> SourceErrors => [];
}

public interface IAppUpdatesStore
{
    AppUpdatesPreferences Load();
    void Save(AppUpdatesPreferences preferences);
}
