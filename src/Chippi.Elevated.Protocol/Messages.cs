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
/// </remarks>
public sealed record AdminRequest(Guid Id, string Verb, string? PackageId = null, string? ExpectedVersion = null, bool IncludeUnknown = false,
    string? Protocol = null, string? LocalAddress = null, int? LocalPort = null, int? Pid = null, long? StartTime = null, string? ServiceName = null);
public sealed record AdminReply(Guid Id, string State, string? PackageId = null, int? ExitCode = null, string? Reason = null);

[JsonSourceGenerationOptions(UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(AdminRequest))]
[JsonSerializable(typeof(AdminReply))]
public partial class AdminJson : JsonSerializerContext;

public static class Messages
{
    public const int MaxBytes = 64 * 1024;
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

    public static bool ServiceNameAllowed(string? name) => name is { Length: > 0 and <= 256 } && name.All(c => !char.IsControl(c) && c is not ('/' or '\\'));
    public static bool ProtocolAllowed(string? protocol) => protocol is "Tcp" or "Udp";
    public static bool LocalAddressAllowed(string? address) => address is { Length: > 0 and <= 64 } && System.Net.IPAddress.TryParse(address, out var ip)
        && ip.AddressFamily is System.Net.Sockets.AddressFamily.InterNetwork or System.Net.Sockets.AddressFamily.InterNetworkV6;
    private static bool EndpointAllowed(AdminRequest r) => ProtocolAllowed(r.Protocol) && LocalAddressAllowed(r.LocalAddress)
        && r.LocalPort is > 0 and <= 65535 && r.Pid is >= 0 && r.StartTime is > 0;
    private static bool NoEndpoint(AdminRequest r) => r.Protocol is null && r.LocalAddress is null && r.LocalPort is null && r.Pid is null && r.StartTime is null && r.ServiceName is null;

    public static bool RequestAllowed(AdminRequest r) => r.Id != Guid.Empty && r.Verb switch
    {
        "UpdatePackage" => PackageIdAllowed(r.PackageId) && VersionAllowed(r.ExpectedVersion) && NoEndpoint(r),
        "Done" => r.PackageId is null && r.ExpectedVersion is null && !r.IncludeUnknown && NoEndpoint(r),
        CloseProcess or EndProcess => r.PackageId is null && r.ExpectedVersion is null && !r.IncludeUnknown && r.ServiceName is null && EndpointAllowed(r),
        StopService => r.PackageId is null && r.ExpectedVersion is null && !r.IncludeUnknown && ServiceNameAllowed(r.ServiceName) && EndpointAllowed(r),
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
            if (!names.Add(property.Name)) throw new InvalidDataException("Duplicate field.");
        return bytes;
    }
}
