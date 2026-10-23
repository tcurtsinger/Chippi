// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace Chippi.App.Notifications;

/// <summary>Opaque, versioned data. A callback never becomes a shell command.</summary>
internal static class ToastActivation
{
    private const string Prefix = "chippi-v1:";
    private sealed record Envelope(int Version, string Module, string[] Arguments);

    internal static string Encode(string module, IReadOnlyList<string> arguments)
    {
        Validate(module, arguments);
        string encoded = Prefix + Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(new Envelope(1, module, [.. arguments])));
        if (encoded.Length > 16384) throw new ArgumentException("Notification activation is too long");
        return encoded;
    }

    internal static bool TryDecode(string? encoded, out string module, out string[] arguments)
    {
        module = ""; arguments = [];
        if (encoded is null || encoded.Length > 16384 || !encoded.StartsWith(Prefix, StringComparison.Ordinal)) return false;
        try
        {
            var envelope = JsonSerializer.Deserialize<Envelope>(Convert.FromBase64String(encoded[Prefix.Length..]));
            if (envelope is null || envelope.Version != 1) return false;
            Validate(envelope.Module, envelope.Arguments);
            module = envelope.Module; arguments = envelope.Arguments;
            return true;
        }
        catch (Exception ex) when (ex is JsonException or FormatException or ArgumentException) { return false; }
    }

    internal static string[] Parse(string? command)
    {
        if (string.IsNullOrWhiteSpace(command)) return [];
        if (command.Length > 8192 || command.Contains('\0')) throw new ArgumentException("Invalid notification action");
        // A fixed executable token keeps CommandLineToArgvW's special first-token rule out of the data.
        nint argv = CommandLineToArgvW("Chippi.exe " + command, out int count);
        if (argv == 0) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            if (count > 33) throw new ArgumentException("Too many notification arguments");
            var arguments = new string[count - 1];
            for (int i = 1; i < count; i++) arguments[i - 1] = Marshal.PtrToStringUni(Marshal.ReadIntPtr(argv, i * nint.Size))!;
            return arguments;
        }
        finally { LocalFree(argv); }
    }

    private static void Validate(string? module, IReadOnlyList<string>? arguments)
    {
        if (string.IsNullOrEmpty(module) || module.Length > 80 || module.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_'))
            throw new ArgumentException("Invalid notification module");
        if (arguments is null || arguments.Count > 32 || arguments.Any(a => a is null || a.Length > 4096 || a.Contains('\0')))
            throw new ArgumentException("Invalid notification arguments");
        if (arguments.Count > 0 && (arguments[0].Length < 3 || !arguments[0].StartsWith("--", StringComparison.Ordinal) ||
            arguments[0][2..].Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-')))
            throw new ArgumentException("Invalid notification verb");
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint CommandLineToArgvW(string commandLine, out int count);
    [DllImport("kernel32.dll")] private static extern nint LocalFree(nint memory);
}