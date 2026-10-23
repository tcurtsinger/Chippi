// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.IO;
using System.Security.Cryptography;
using System.Text;
using Chippi.Core.SystemIntegration;

namespace Chippi.App.Notifications;

/// <summary>Stable per-profile identity. Construction never writes registration.</summary>
internal sealed record ToastIdentity(string AppId, Guid Activator, string Executable, string Shortcut, string? Profile)
{
    internal static ToastIdentity Create(string? profile, string executable, string programs)
    {
        if (!string.IsNullOrWhiteSpace(profile) && (profile.Length > 80 || profile.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_')))
            throw new ArgumentException("Invalid notification dev profile", nameof(profile));
        profile = string.IsNullOrWhiteSpace(profile) ? null : profile;
        // Separate namespace from both the production identity and the old spike.
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes("Chippi.Notifications.v1/" + (profile?.ToLowerInvariant() ?? "production")));
        string suffix = Convert.ToHexString(hash.AsSpan(0, 16)).ToLowerInvariant();
        return new ToastIdentity(profile is null ? "velopack.ChippiApp" : "Chippi.dev." + suffix,
            new Guid(hash.AsSpan(0, 16)), ExplorerRegistration.StableExecutable(executable),
            Path.Combine(programs, profile is null ? "Chippi.lnk" : $"Chippi-dev-{profile}.lnk"), profile);
    }

    internal bool CanRegister => Profile is not null || string.Equals(Path.GetFullPath(Executable),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ChippiApp", "current", "Chippi.exe"),
        StringComparison.OrdinalIgnoreCase);

    internal string ServerArguments => "--toast-server" + (Profile is null ? "" : $" --dev-profile \"{Profile}\"");
}