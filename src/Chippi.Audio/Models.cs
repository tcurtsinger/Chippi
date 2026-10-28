// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

namespace Chippi.Audio;

/// <summary>Playback (render) or recording (capture).</summary>
public enum AudioFlow { Render, Capture }

/// <summary>Windows' three default-device roles. Values match Core Audio's <c>ERole</c>.</summary>
public enum AudioRole { Console = 0, Multimedia = 1, Communications = 2 }

/// <summary>Values match Core Audio's <c>DEVICE_STATE_*</c> flags.</summary>
public enum EndpointState { Active = 1, Disabled = 2, NotPresent = 4, Unplugged = 8 }

/// <summary>Which Sound feature currently sets a default role (architecture §3).</summary>
public enum RoleOwner { None, Routing, Processing }

/// <summary>One default role of one flow.</summary>
public readonly record struct RoleKey(AudioFlow Flow, AudioRole Role);

/// <summary>
/// An endpoint as Windows lists it. <see cref="Id"/> is the endpoint ID string (compare it
/// case-insensitively); <see cref="AdapterName"/> is the device it belongs to ("VB-Audio Virtual
/// Cable", "Realtek(R) Audio") and is display data, never identity. <see cref="Filter"/> is the
/// driver's KS filter pin the endpoint connects to (<c>{2}.\\?\usb#vid_...#{6994ad04-...}\global/00010001</c>):
/// set by the driver and its INF, not renameable, so it identifies a driver's endpoints (<see cref="VbCable"/>).
/// </summary>
public sealed record AudioEndpoint(string Id, string Name, AudioFlow Flow, EndpointState State, string AdapterName = "", string Filter = "")
{
    public bool IsActive => State == EndpointState.Active;
}

/// <summary>Endpoint master volume (0 to 1) and mute.</summary>
public readonly record struct EndpointLevel(float Volume, bool Muted);

public enum SessionState { Inactive, Active, Expired }

/// <summary>
/// One render session as the device system reports it. <see cref="AppName"/> is already resolved
/// for display (session name, else the exe's description, else its file name).
/// </summary>
public sealed record AudioSessionInfo(
    string SessionId,
    string EndpointId,
    int ProcessId,
    string? ExePath,
    string AppName,
    bool IsSystemSounds,
    SessionState State,
    float Volume,
    bool Muted);

/// <summary>
/// Stable identity of an app across its sessions and processes: the exe path, else the session's
/// display name, else system sounds. This is what App volume remembers, never a process ID.
/// </summary>
public readonly partial record struct AppKey(string Value)
{
    public static AppKey SystemSounds { get; } = new("system");

    /// <summary>
    /// Squirrel-installed apps (Discord, Slack) run from a versioned folder, "app-1.0.9265"; the
    /// version is dropped so the key survives the app's updates.
    /// </summary>
    public static AppKey ForExe(string path) => new("exe:" + VersionFolder().Replace(path, @"\app\").ToUpperInvariant());

    [System.Text.RegularExpressions.GeneratedRegex(@"\\app-\d+(\.\d+)*\\", System.Text.RegularExpressions.RegexOptions.IgnoreCase)]
    private static partial System.Text.RegularExpressions.Regex VersionFolder();

    public static AppKey ForName(string name) => new("name:" + name.ToUpperInvariant());

    public override string ToString() => Value;
}

/// <summary>All of one app's render sessions, grouped. <see cref="Volume"/> is the first active session's.</summary>
public sealed record AppAudio(
    AppKey Key,
    string Name,
    string? ExePath,
    float Volume,
    bool Muted,
    bool IsActive,
    IReadOnlyList<string> EndpointIds)
{
    public IReadOnlyList<int> ProcessIds { get; init; } = [];
}

/// <summary>What a Core Audio notification was about. Devices and Defaults are debounced; the rest coalesce.</summary>
public enum DeviceChange { Devices, Defaults, Levels, Sessions }

public enum RoleChangeKind
{
    /// <summary>Applied because an owner claimed the role or changed its choice (startup, list edits). Never announced.</summary>
    Claim,

    /// <summary>The owner's choice moved because devices changed (a higher device connected, the winner left).</summary>
    Switch,

    /// <summary>The choice was unchanged but something else moved the default; the arbiter put it back.</summary>
    Repair,
}

/// <summary>The arbiter set <paramref name="EndpointId"/> as default for <paramref name="Roles"/>.</summary>
public sealed record RoleChange(AudioFlow Flow, IReadOnlyList<AudioRole> Roles, string EndpointId, RoleOwner Owner, RoleChangeKind Kind);

/// <summary>Peaks (0 to 1) for the endpoints and apps a meter watcher asked for.</summary>
/// <remarks><paramref name="ProcessingInput"/> and <paramref name="ProcessingOutput"/> are Mic processing's raw and cleaned peaks, 0 when it isn't live.</remarks>
public sealed record MeterReadings(IReadOnlyDictionary<string, float> Endpoints, IReadOnlyDictionary<AppKey, float> Apps, float ProcessingInput = 0, float ProcessingOutput = 0);

/// <summary>Picks the endpoint a claimed role should point at, from the flow's active endpoints. Null leaves Windows alone.</summary>
public delegate string? RoleTarget(IReadOnlyList<AudioEndpoint> active);
