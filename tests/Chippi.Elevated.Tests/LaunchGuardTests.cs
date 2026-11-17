// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors
using Chippi.UI.Admin;

namespace Chippi.Elevated.Tests;

public sealed class LaunchGuardTests
{
    [Fact]
    public void A_held_helper_cannot_be_written_renamed_or_deleted_until_released()
    {
        string folder = Path.Combine(Path.GetTempPath(), "chippi-guard-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        string file = Path.Combine(folder, "Chippi.Elevated.exe");
        File.WriteAllBytes(file, [1, 2, 3]);
        try
        {
            var hold = LaunchGuard.Hold(file);
            Assert.ThrowsAny<IOException>(() => File.WriteAllBytes(file, [9]));
            Assert.ThrowsAny<IOException>(() => File.Delete(file));
            Assert.ThrowsAny<IOException>(() => File.Move(file, file + ".old"));
            using (File.Open(file, FileMode.Open, FileAccess.Read, FileShare.Read)) { } // execution and reads still work
            hold.Dispose();
            File.WriteAllBytes(file, [4]);
        }
        finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public void Relative_and_missing_paths_are_refused()
    {
        Assert.Throws<UnauthorizedAccessException>(() => LaunchGuard.Hold("Chippi.Elevated.exe"));
        Assert.ThrowsAny<IOException>(() => LaunchGuard.Hold(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "x.exe")));
    }
}
