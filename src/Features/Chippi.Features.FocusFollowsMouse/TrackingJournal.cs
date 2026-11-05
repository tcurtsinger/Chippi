// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Text.Json;

namespace Chippi.Features.FocusFollowsMouse;

public sealed class TrackingJournal(string path) : ITrackingJournal
{
    public Dictionary<TrackingSetting, TrackingOwnership> Load() => File.Exists(path)
        ? JsonSerializer.Deserialize<Dictionary<TrackingSetting, TrackingOwnership>>(File.ReadAllText(path))
            ?? throw new InvalidDataException("Focus ownership unreadable")
        : [];

    public void Save(IReadOnlyDictionary<TrackingSetting, TrackingOwnership> ownership)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temp = path + ".tmp";
        using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None,
                   4096, FileOptions.WriteThrough))
        {
            JsonSerializer.Serialize(stream, ownership);
            stream.Flush(flushToDisk: true);
        }
        File.Move(temp, path, overwrite: true);
    }
}
