// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Runtime.InteropServices;

namespace Chippi.Audio;

/// <summary>
/// The reason fragment a failure shows in Settings, the flyout, the island and notices (DESIGN.md §3:
/// errors are fragments). The full exception belongs in the log, never on screen.
/// </summary>
public static class AudioErrors
{
    private const int DeviceInvalidated = unchecked((int)0x88890004);
    private const int DeviceInUse = unchecked((int)0x8889000A);

    public static string Reason(Exception error) => error switch
    {
        COMException { HResult: DeviceInvalidated } => "Device removed",
        COMException { HResult: DeviceInUse } => "Device in use",
        COMException or ExternalException => "Device error",
        DllNotFoundException or BadImageFormatException or EntryPointNotFoundException => "Noise suppression unavailable",
        UnauthorizedAccessException => "Access denied",
        _ when IsFragment(error.Message) => error.Message,
        _ => "Audio error",
    };

    // Chippi's own reasons are short and unpunctuated; anything else is diagnostic text.
    private static bool IsFragment(string message) =>
        message.Length is > 0 and <= 40 && !message.EndsWith('.') && !message.Contains('{') && !message.Contains(';');
}
