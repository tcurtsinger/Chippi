// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.IO;

namespace Chippi.Features.ScreenCapture.Tests;

public sealed class CaptureSavedFileTests
{
    [Fact]
    public void Open_folder_refuses_a_missing_folder_without_launching_anything()
    {
        Assert.False(CaptureFiles.OpenFolder(null));
        Assert.False(CaptureFiles.OpenFolder(Path.Combine(Path.GetTempPath(), "chippi-missing-" + Guid.NewGuid().ToString("N"))));
    }

    [Fact]
    public void Replaced_file_is_refused_before_any_recycle_operation()
    {
        string folder = Path.Combine(Path.GetTempPath(), "chippi-file-id-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            string path = Path.Combine(folder, "saved.png");
            File.WriteAllText(path, "original");
            var identity = CaptureSavedFile.Identity(path);
            Assert.NotNull(identity);
            var capture = new RecentCapture(Guid.NewGuid(), "unused.png", DateTimeOffset.Now, 1, 1, path, identity);
            File.Move(path, Path.Combine(folder, "original.png"));
            File.WriteAllText(path, "replacement");
            Assert.NotEqual(identity, CaptureSavedFile.Identity(path));
            Assert.False(CaptureSavedFile.Recycle(capture));
            Assert.Equal("replacement", File.ReadAllText(path));
            Assert.Equal("original", File.ReadAllText(Path.Combine(folder, "original.png")));
        }
        finally { Directory.Delete(folder, true); }
    }
}
