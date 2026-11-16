// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors
using System.IO;
using System.Text.Json;

namespace Chippi.Features.AppUpdates;

/// <summary>Feature-owned preferences/history only: no commands, paths or installer output.</summary>
public sealed class JsonAppUpdatesStore(string dataDirectory) : IAppUpdatesStore
{
    private readonly string _file = Path.Combine(Path.GetFullPath(dataDirectory), "app-updates.json");
    public AppUpdatesPreferences Load()
    {
        if (!File.Exists(_file)) return new();
        if (new FileInfo(_file).Length > 1024 * 1024) throw new InvalidDataException("App update settings are too large.");
        return JsonSerializer.Deserialize<AppUpdatesPreferences>(File.ReadAllText(_file)) ?? throw new JsonException("Invalid app update settings.");
    }
    public void Save(AppUpdatesPreferences preferences)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
        string temporary = _file + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            string json = JsonSerializer.Serialize(preferences);
            if (System.Text.Encoding.UTF8.GetByteCount(json) > 1024 * 1024) throw new InvalidDataException("App update settings are too large.");
            File.WriteAllText(temporary, json);
            File.Move(temporary, _file, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
