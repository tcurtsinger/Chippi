// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors
using System.Buffers.Binary;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Chippi.Elevated.Protocol;

/// <summary>UpdatePackage carries the version the owner reviewed; IncludeUnknown is set only when the snapshot had no installed version.</summary>
/// <remarks>
/// The port verbs (CloseProcess, EndProcess, StopService) carry the confirmed row's endpoint: Protocol ("Tcp" or "Udp"), LocalAddress (an IPv4 or IPv6
/// literal, which also fixes the family), LocalPort, Pid and StartTime (the process's creation time as a FILETIME). StopService adds ServiceName.
/// Uninstall carries View (64 or 32, the registry view) and KeyName (the HKLM Uninstall key's name) and nothing else; its reply carries ExitCode, KeyGone and RecordId.
/// DeleteLeftovers carries RecordId and Items (at most 200); its reply carries one Result per item, in order. DropRecord carries RecordId only.
/// </remarks>
public sealed record AdminRequest(Guid Id, string Verb, string? PackageId = null, string? ExpectedVersion = null, bool IncludeUnknown = false,
    string? Protocol = null, string? LocalAddress = null, int? LocalPort = null, int? Pid = null, long? StartTime = null, string? ServiceName = null,
    string? Category = null, int? View = null, string? KeyName = null, Guid? RecordId = null, LeftoverItem[]? Items = null);
/// <summary>
/// One leftover the scan found. Kind is File, Folder or RegistryKey; Path is an absolute local path, or for a key the path under HKLM (starting with Software) in
/// the named View (64 or 32). Class is the one it was shown in (Safe or CheckFirst) and Ticked says the owner chose it after the review.
/// </summary>
public sealed record LeftoverItem(string Kind, string Path, int? View, string Class, bool Ticked);
/// <summary>State is Removed, Kept (Reason says why), InUse, Failed or Yours (the owner's own token could delete it, so Chippi deletes it as the owner).</summary>
public sealed record LeftoverResult(string State, string? Reason = null);
/// <summary>A category reply echoes Category and carries Bytes, Files and InUse (files skipped because something held them open).</summary>
public sealed record AdminReply(Guid Id, string State, string? PackageId = null, int? ExitCode = null, string? Reason = null,
    string? Category = null, long? Bytes = null, int? Files = null, int? InUse = null, Guid? RecordId = null, bool? KeyGone = null, LeftoverResult[]? Results = null);

[JsonSourceGenerationOptions(UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(AdminRequest))]
[JsonSerializable(typeof(AdminReply))]
public partial class AdminJson : JsonSerializerContext;

public static class Messages
{
    public const int MaxBytes = 256 * 1024;
    /// <summary>The helper kills a winget that runs longer than this for one package.</summary>
    public static readonly TimeSpan PackageCeiling = TimeSpan.FromMinutes(30);
    public static bool VersionAllowed(string? version) => version is { Length: > 0 and <= 64 }
        && version[0] != '-' && version.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '+' or '_' or '-' or '~');
    public static bool PackageIdAllowed(string? id) => id is { Length: > 0 and <= 128 }
        && id[0] != '-' && !id.StartsWith('.') && !id.EndsWith('.') && !id.Contains("..", StringComparison.Ordinal)
        && id.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '+' or '_' or '-');

    public static bool PipeNameAllowed(string? name) => name is { Length: 45 }
        && name.StartsWith("chippi-admin-", StringComparison.Ordinal) && Guid.TryParseExact(name[13..], "N", out _);

    public const string CloseProcess = "CloseProcess", EndProcess = "EndProcess", StopService = "StopService";
    /// <summary>Reply states of a port verb. "Done" means the action was carried out (the page reads the table again to say Freed).</summary>
    public const string PortDone = "Done", PortGone = "Gone", PortChanged = "Changed", PortKept = "Kept", PortFailed = "Failed";
    public static bool IsPortVerb(string? verb) => verb is CloseProcess or EndProcess or StopService;
    public static bool PortStateAllowed(string? state) => state is PortDone or PortGone or PortChanged or PortKept or PortFailed;

    public const string MeasureCategory = "MeasureCategory", CleanCategory = "CleanCategory";
    /// <summary>The closed list of Cleaner categories. The helper maps each name to folders fixed in its own code; no path ever crosses the pipe.</summary>
    public const string WindowsTemp = "WindowsTemp", WindowsUpdateDownloads = "WindowsUpdateDownloads", DeliveryOptimization = "DeliveryOptimization",
        SystemCrashDumps = "SystemCrashDumps";
    public static readonly IReadOnlyList<string> Categories = [WindowsTemp, WindowsUpdateDownloads, DeliveryOptimization, SystemCrashDumps];
    /// <summary>Reply states of a category verb: Measured answers MeasureCategory, Cleaned answers CleanCategory (Reason "Cancelled" when it stopped early).</summary>
    public const string CategoryMeasured = "Measured", CategoryCleaned = "Cleaned", CategoryUnavailable = "Unavailable", CategoryFailed = "Failed";
    public const string CancelledReason = "Cancelled";
    public static bool IsCategoryVerb(string? verb) => verb is MeasureCategory or CleanCategory;
    public static bool CategoryAllowed(string? name) => name is not null && Categories.Contains(name);
    public static bool CategoryStateAllowed(string? state) => state is CategoryMeasured or CategoryCleaned or CategoryUnavailable or CategoryFailed;

    public const string Uninstall = "Uninstall";
    /// <summary>Reply states of Uninstall: Uninstalled (exit 0), Restart (exit 3010: Restart to finish), Failed (any other exit, or it could not run), Refused (a check said no; Reason says which).</summary>
    public const string UninstallDone = "Uninstalled", UninstallRestart = "Restart", UninstallFailed = "Failed", UninstallRefused = "Refused";
    public const int RestartExitCode = 3010;
    public static bool IsUninstallVerb(string? verb) => verb == Uninstall;
    public static bool UninstallStateAllowed(string? state) => state is UninstallDone or UninstallRestart or UninstallFailed or UninstallRefused;
    public static bool ViewAllowed(int? view) => view is 32 or 64;
    /// <summary>A registry key name: no backslash (it can't leave the Uninstall key), no control characters, at most 255 characters (the registry's own limit).</summary>
    public static bool KeyNameAllowed(string? name) => name is { Length: > 0 and <= 255 } && name.All(c => !char.IsControl(c) && c != '\\');
    private static bool NoUninstall(AdminRequest r) => r.View is null && r.KeyName is null && r.RecordId is null && r.Items is null;

    public const string DeleteLeftovers = "DeleteLeftovers", DropRecord = "DropRecord";
    /// <summary>At most 200 items a request and 400 characters a path, so the biggest request stays inside <see cref="MaxBytes"/> even with every backslash escaped.</summary>
    public const int MaxLeftoverItems = 200, MaxLeftoverPath = 400;
    public const string KindFile = "File", KindFolder = "Folder", KindRegistryKey = "RegistryKey", ClassSafe = "Safe", ClassCheckFirst = "CheckFirst";
    /// <summary>Reply states of DeleteLeftovers (Leftovers: every item has a result) and DropRecord (Dropped, also when no such record exists).</summary>
    public const string LeftoversDone = "Leftovers", LeftoversFailed = "Failed", RecordDropped = "Dropped";
    public const string ItemRemoved = "Removed", ItemKept = "Kept", ItemInUse = "InUse", ItemFailed = "Failed", ItemYours = "Yours";
    public static bool IsLeftoverVerb(string? verb) => verb is DeleteLeftovers or DropRecord;
    public static bool LeftoverStateAllowed(string? state) => state is ItemRemoved or ItemKept or ItemInUse or ItemFailed or ItemYours;
    public static bool LeftoverItemAllowed(LeftoverItem? i) => i is not null
        && i.Class is ClassSafe or ClassCheckFirst
        && i.Path is { Length: > 0 and <= MaxLeftoverPath } && i.Path.All(c => !char.IsControl(c))
        && i.Kind switch
        {
            KindFile or KindFolder => i.View is null,
            KindRegistryKey => ViewAllowed(i.View) && i.Path[0] != '\\' && i.Path[^1] != '\\',
            _ => false,
        };
    private static bool OnlyRecord(AdminRequest r) => r.PackageId is null && r.ExpectedVersion is null && !r.IncludeUnknown && r.Category is null && r.View is null && r.KeyName is null
        && r.Protocol is null && r.LocalAddress is null && r.LocalPort is null && r.Pid is null && r.StartTime is null && r.ServiceName is null;

    public static bool ServiceNameAllowed(string? name) => name is { Length: > 0 and <= 256 } && name.All(c => !char.IsControl(c) && c is not ('/' or '\\'));
    public static bool ProtocolAllowed(string? protocol) => protocol is "Tcp" or "Udp";
    public static bool LocalAddressAllowed(string? address) => address is { Length: > 0 and <= 64 } && System.Net.IPAddress.TryParse(address, out var ip)
        && ip.AddressFamily is System.Net.Sockets.AddressFamily.InterNetwork or System.Net.Sockets.AddressFamily.InterNetworkV6;
    private static bool EndpointAllowed(AdminRequest r) => ProtocolAllowed(r.Protocol) && LocalAddressAllowed(r.LocalAddress)
        && r.LocalPort is > 0 and <= 65535 && r.Pid is >= 0 && r.StartTime is > 0;
    private static bool NoEndpoint(AdminRequest r) => r.Protocol is null && r.LocalAddress is null && r.LocalPort is null && r.Pid is null && r.StartTime is null && r.ServiceName is null && r.Category is null && NoUninstall(r);

    public static bool RequestAllowed(AdminRequest r) => r.Id != Guid.Empty && r.Verb switch
    {
        "UpdatePackage" => PackageIdAllowed(r.PackageId) && VersionAllowed(r.ExpectedVersion) && NoEndpoint(r),
        "Done" => r.PackageId is null && r.ExpectedVersion is null && !r.IncludeUnknown && NoEndpoint(r),
        MeasureCategory or CleanCategory => CategoryAllowed(r.Category) && r.PackageId is null && r.ExpectedVersion is null && !r.IncludeUnknown
            && r.Protocol is null && r.LocalAddress is null && r.LocalPort is null && r.Pid is null && r.StartTime is null && r.ServiceName is null && NoUninstall(r),
        CloseProcess or EndProcess => r.Category is null && r.PackageId is null && r.ExpectedVersion is null && !r.IncludeUnknown && r.ServiceName is null && NoUninstall(r) && EndpointAllowed(r),
        StopService => r.Category is null && r.PackageId is null && r.ExpectedVersion is null && !r.IncludeUnknown && NoUninstall(r) && ServiceNameAllowed(r.ServiceName) && EndpointAllowed(r),
        Uninstall => ViewAllowed(r.View) && KeyNameAllowed(r.KeyName) && r.PackageId is null && r.ExpectedVersion is null && !r.IncludeUnknown && r.Category is null
            && r.Protocol is null && r.LocalAddress is null && r.LocalPort is null && r.Pid is null && r.StartTime is null && r.ServiceName is null && r.RecordId is null && r.Items is null,
        DeleteLeftovers => OnlyRecord(r) && r.RecordId is { } id && id != Guid.Empty && r.Items is { Length: > 0 and <= MaxLeftoverItems } items && items.All(LeftoverItemAllowed),
        DropRecord => OnlyRecord(r) && r.RecordId is { } drop && drop != Guid.Empty && r.Items is null,
        _ => false,
    };

    public static Task WriteAsync(Stream stream, AdminRequest request, CancellationToken ct = default) =>
        WriteFrame(stream, JsonSerializer.SerializeToUtf8Bytes(request, AdminJson.Default.AdminRequest), ct);
    public static Task WriteAsync(Stream stream, AdminReply reply, CancellationToken ct = default) =>
        WriteFrame(stream, JsonSerializer.SerializeToUtf8Bytes(reply, AdminJson.Default.AdminReply), ct);
    public static async Task<AdminRequest?> ReadRequestAsync(Stream stream, CancellationToken ct = default)
    {
        var bytes = await ReadFrame(stream, ct).ConfigureAwait(false);
        if (bytes is null) return null;
        var value = JsonSerializer.Deserialize(bytes, AdminJson.Default.AdminRequest);
        if (value is null || !RequestAllowed(value)) throw new InvalidDataException("Invalid admin request.");
        return value;
    }
    public static async Task<AdminReply?> ReadReplyAsync(Stream stream, CancellationToken ct = default)
    {
        var bytes = await ReadFrame(stream, ct).ConfigureAwait(false);
        return bytes is null ? null : JsonSerializer.Deserialize(bytes, AdminJson.Default.AdminReply)
            ?? throw new InvalidDataException("Invalid admin response.");
    }
    private static async Task WriteFrame(Stream stream, byte[] bytes, CancellationToken ct)
    {
        if (bytes.Length is 0 or > MaxBytes) throw new InvalidDataException("Invalid frame length.");
        var prefix = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(prefix, bytes.Length);
        await stream.WriteAsync(prefix, ct).ConfigureAwait(false);
        await stream.WriteAsync(bytes, ct).ConfigureAwait(false);
        await stream.FlushAsync(ct).ConfigureAwait(false);
    }
    private static async Task<byte[]?> ReadFrame(Stream stream, CancellationToken ct)
    {
        var prefix = new byte[4];
        if (await stream.ReadAsync(prefix.AsMemory(0, 1), ct).ConfigureAwait(false) == 0) return null;
        await stream.ReadExactlyAsync(prefix.AsMemory(1), ct).ConfigureAwait(false);
        int length = BinaryPrimitives.ReadInt32LittleEndian(prefix);
        if (length is <= 0 or > MaxBytes) throw new InvalidDataException("Invalid frame length.");
        var bytes = new byte[length];
        await stream.ReadExactlyAsync(bytes, ct).ConfigureAwait(false);
        // Source generation rejects extra properties; duplicate fields must also be rejected.
        using var document = JsonDocument.Parse(bytes);
        if (document.RootElement.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Invalid frame.");
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in document.RootElement.EnumerateObject())
        {
            if (!names.Add(property.Name)) throw new InvalidDataException("Duplicate field.");
            // Items and results are objects in an array: a repeated field inside one is as wrong as one at the top.
            if (property.Value.ValueKind != JsonValueKind.Array) continue;
            foreach (var element in property.Value.EnumerateArray())
            {
                if (element.ValueKind != JsonValueKind.Object) continue;
                var inner = new HashSet<string>(StringComparer.Ordinal);
                foreach (var field in element.EnumerateObject())
                    if (!inner.Add(field.Name)) throw new InvalidDataException("Duplicate field.");
            }
        }
        return bytes;
    }
}
