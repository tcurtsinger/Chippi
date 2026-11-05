// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Text.Json;

using System.IO;

namespace Chippi.Features.FocusFollowsMouse;

public sealed class TrackingJournal(string path) : ITrackingJournal
{
    public Dictionary<TrackingSetting, TrackingOwnership> Load()
    {
        if (!File.Exists(path)) return [];
        try
        {
            var ownership = JsonSerializer.Deserialize<Dictionary<TrackingSetting, TrackingOwnership>>(File.ReadAllText(path));
            if (ownership is null || ownership.Any(pair => !Enum.IsDefined(pair.Key) || pair.Value is null
                || (pair.Key != TrackingSetting.Delay && (pair.Value.Original > 1 || pair.Value.Applied > 1))))
                throw new InvalidDataException("Focus ownership unreadable");
            return ownership;
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException)
        {
            // Marge's task 277: damaged ownership must not block enabling or uninstalling.
            // No original values can be inferred; the next enable captures the current settings.
            Chippi.Core.Diagnostics.Log.Warn("Focus follows mouse: ownership journal unreadable, treating as empty", ex);
            return [];
        }
    }
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
