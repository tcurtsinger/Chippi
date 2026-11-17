// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors
using System.IO;

namespace Chippi.UI.Admin;

/// <summary>
/// Holds the helper executable open with no write or delete sharing, so a process running as the same user can't swap or rename it
/// between the check and the elevated launch. It does not make a per-user install trustworthy: see docs/phase-7/admin-helper.md.
/// </summary>
public static class LaunchGuard
{
    public static IDisposable Hold(string executable)
    {
        if (!Path.IsPathFullyQualified(executable)) throw new UnauthorizedAccessException("Unsafe helper path.");
        var stream = new FileStream(executable, FileMode.Open, FileAccess.Read, FileShare.Read, 1, FileOptions.None);
        try
        {
            string? folder = Path.GetDirectoryName(executable);
            if (HasReparsePoint(executable) || (folder is not null && HasReparsePoint(folder)))
                throw new UnauthorizedAccessException("Reparse point refused.");
            return stream;
        }
        catch { stream.Dispose(); throw; }
    }

    private static bool HasReparsePoint(string path) => (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
}
