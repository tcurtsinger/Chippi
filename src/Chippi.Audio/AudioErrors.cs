// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Runtime.InteropServices;

namespace Chippi.Audio;

/// <summary>A failure whose message is a reason fragment fit to show on screen (DESIGN.md §3).</summary>
public class AudioFailure(string reason, Exception? inner = null) : InvalidOperationException(reason, inner);

/// <summary>
/// The reason fragment a failure shows in Settings, the island and notices. Only an
/// <see cref="AudioFailure"/>'s own text is shown; every other message is diagnostic and stays in the log.
/// </summary>
public static class AudioErrors
{
    private const int DeviceInvalidated = unchecked((int)0x88890004);
    private const int DeviceInUse = unchecked((int)0x8889000A);

    public static string Reason(Exception error) => error switch
    {
        AudioFailure => error.Message,
        COMException { HResult: DeviceInvalidated } => "Device removed",
        COMException { HResult: DeviceInUse } => "Device in use",
        ExternalException => "Device error",
        DllNotFoundException or BadImageFormatException or EntryPointNotFoundException => "Noise suppression unavailable",
        UnauthorizedAccessException => "Access denied",
        _ => "Audio error",
    };
}
