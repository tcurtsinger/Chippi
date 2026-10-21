// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Chippi.Core.Config;

/// <summary>
/// settings.json: one versioned JSON document holding named sections ("app", "features") and
/// one section per module under "modules". The document is kept as a JSON tree, so sections
/// and properties this build does not know about (from a newer version, or an uninstalled
/// module) survive every save. Writes are atomic: temp file, flush, then replace.
/// </summary>
public sealed class ConfigStore
{
    public const int CurrentVersion = 1;

    private const string VersionKey = "version";
    private const string ModulesKey = "modules";

    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private readonly Lock _gate = new();
    private readonly ILogger _logger;
    private JsonObject _root = NewRoot();

    public ConfigStore(string? filePath = null, ILogger? logger = null)
    {
        FilePath = filePath ?? DefaultPath;
        _logger = logger ?? NullLogger.Instance;
    }

    /// <summary>%APPDATA%\Chippi\settings.json</summary>
    public static string DefaultPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Chippi", "settings.json");

    public string FilePath { get; }

    /// <summary>Raised after <see cref="Import"/> replaced the document. Listeners re-read their sections.</summary>
    public event EventHandler? Imported;

    /// <summary>
    /// Reads settings.json. A missing file starts empty. An unreadable one is moved aside to
    /// settings.corrupt-&lt;timestamp&gt;.json and the app starts on defaults instead of crashing.
    /// </summary>
    public void Load()
    {
        lock (_gate)
        {
            if (!File.Exists(FilePath))
            {
                _logger.LogInformation("No settings at {Path}; starting on defaults", FilePath);
                _root = NewRoot();
                return;
            }

            try
            {
                var root = Parse(File.ReadAllText(FilePath));

                // A shell section this build cannot read (hand-edited, or a bad value) would make
                // every reader throw. Keep a copy of the file, drop only that section so it falls
                // back to defaults, and keep everything else, unknown sections included.
                for (var bad = FirstInvalidSection(root); bad is not null; bad = FirstInvalidSection(root))
                {
                    var copy = AsidePath("invalid");
                    _logger.LogError("Settings section {Section} is invalid; resetting it (original kept at {Copy})", bad, copy);
                    TryCopy(FilePath, copy);
                    root.Remove(bad);
                }

                _root = root;
                _logger.LogInformation("Settings loaded from {Path} (version {Version})", FilePath, VersionOf(_root));
            }
            catch (Exception ex) when (ex is JsonException or InvalidDataException)
            {
                var aside = AsidePath("corrupt");
                _logger.LogError(ex, "Settings unreadable; moved to {Aside} and starting on defaults", aside);
                TryMove(FilePath, aside);
                _root = NewRoot();
            }
        }
    }

    /// <summary>Writes the whole document atomically.</summary>
    public void Save()
    {
        lock (_gate)
            WriteAtomic(FilePath, _root.ToJsonString(JsonOptions));
    }

    public bool HasSection(string name)
    {
        lock (_gate)
            return _root[name] is not null;
    }

    /// <summary>Reads a top-level section, or a new <typeparamref name="T"/> when absent.</summary>
    public T GetSection<T>(string name) where T : class, new()
    {
        lock (_gate)
            return Read<T>(_root[name], name);
    }

    /// <summary>Merges <paramref name="value"/> into a top-level section and saves. Unknown properties in the section are kept.</summary>
    public void SetSection<T>(string name, T value) where T : class
    {
        lock (_gate)
        {
            _root[name] = Merge(_root[name] as JsonObject, ToNode(value));
            WriteAtomic(FilePath, _root.ToJsonString(JsonOptions));
        }
    }

    public bool HasModuleSettings(string moduleId)
    {
        lock (_gate)
            return Modules(create: false)?[moduleId] is not null;
    }

    public T GetModuleSettings<T>(string moduleId) where T : class, new()
    {
        lock (_gate)
            return Read<T>(Modules(create: false)?[moduleId], $"{ModulesKey}.{moduleId}");
    }

    public void SetModuleSettings<T>(string moduleId, T value) where T : class
    {
        lock (_gate)
        {
            var modules = Modules(create: true)!;
            modules[moduleId] = Merge(modules[moduleId] as JsonObject, ToNode(value));
            WriteAtomic(FilePath, _root.ToJsonString(JsonOptions));
        }
    }

    /// <summary>Writes the current settings to <paramref name="path"/> as one JSON file.</summary>
    public void Export(string path)
    {
        lock (_gate)
            WriteAtomic(path, _root.ToJsonString(JsonOptions));
        _logger.LogInformation("Settings exported to {Path}", path);
    }

    /// <summary>
    /// Replaces the settings with an exported file and saves. Throws <see cref="InvalidDataException"/>
    /// when the file is not a valid Chippi settings export, and <see cref="IOException"/> when
    /// settings.json cannot be written; in both cases the current settings are unchanged.
    /// </summary>
    public void Import(string path)
    {
        JsonObject imported;
        try
        {
            imported = Parse(File.ReadAllText(path));
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("That file is not valid JSON.", ex);
        }
        if (FirstInvalidSection(imported) is { } bad)
            throw new InvalidDataException($"The \"{bad}\" section of that file is not valid Chippi settings.");

        lock (_gate)
        {
            // Commit to disk first: only a successful write replaces the live settings.
            WriteAtomic(FilePath, imported.ToJsonString(JsonOptions));
            _root = imported;
        }
        _logger.LogInformation("Settings imported from {Path}", path);
        Imported?.Invoke(this, EventArgs.Empty);
    }

    private JsonObject? Modules(bool create)
    {
        if (_root[ModulesKey] is JsonObject modules)
            return modules;
        if (!create)
            return null;
        modules = [];
        _root[ModulesKey] = modules;
        return modules;
    }

    private static JsonObject NewRoot() => new() { [VersionKey] = CurrentVersion };

    private static int VersionOf(JsonObject root) =>
        root[VersionKey] is JsonValue v && v.TryGetValue(out int version) ? version : 0;

    /// <summary>Parses and validates a settings document, upgrading older versions in place.</summary>
    private static JsonObject Parse(string json)
    {
        if (JsonNode.Parse(json) is not JsonObject root)
            throw new InvalidDataException("Settings must be a JSON object.");
        if (root[VersionKey] is not JsonValue v || !v.TryGetValue(out int version) || version < 1)
            throw new InvalidDataException("Settings have no version, so they are not a Chippi settings file.");

        // Migrations go here as the schema changes: if (version < 2) { ...; root["version"] = 2; }
        // A newer file than this build is loaded as-is; its version number is kept so the
        // newer build does not mistake our save for an old schema.
        return root;
    }

    /// <summary>The shell sections this build owns, and what makes each readable. Module sections are typed by their module and checked when read.</summary>
    private static readonly (string Name, Func<JsonNode, bool> IsValid)[] KnownSections =
    [
        (AppSettings.SectionName, node => node is JsonObject && node.Deserialize<AppSettings>(JsonOptions) is not null),
        (Chippi.Core.Modules.FeatureState.SectionName, node => node is JsonObject
            && node.Deserialize<Chippi.Core.Modules.FeatureState>(JsonOptions) is { Installed: not null, CapabilitiesUsed: not null }),
        (ModulesKey, node => node is JsonObject),
    ];

    private static string? FirstInvalidSection(JsonObject root)
    {
        foreach (var (name, isValid) in KnownSections)
        {
            if (root[name] is not { } node)
                continue;
            try
            {
                if (!isValid(node))
                    return name;
            }
            catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidOperationException)
            {
                return name;
            }
        }
        return null;
    }

    /// <summary>A module's section can still hold values its type rejects; it then reads as defaults rather than throwing.</summary>
    private T Read<T>(JsonNode? node, string section) where T : class, new()
    {
        if (node is null)
            return new T();
        try
        {
            return node.Deserialize<T>(JsonOptions) ?? new T();
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidOperationException)
        {
            _logger.LogError(ex, "Settings section {Section} is invalid; using defaults", section);
            return new T();
        }
    }

    private string AsidePath(string reason) => Path.Combine(
        Path.GetDirectoryName(FilePath)!,
        $"{Path.GetFileNameWithoutExtension(FilePath)}.{reason}-{DateTime.Now:yyyyMMdd-HHmmss}.json");

    private static JsonObject ToNode<T>(T value) =>
        JsonSerializer.SerializeToNode(value, JsonOptions) as JsonObject
        ?? throw new ArgumentException("A settings section must serialize to a JSON object.", nameof(value));

    /// <summary>Copies <paramref name="update"/> over <paramref name="existing"/>, recursing into objects, so keys only the file knows survive.</summary>
    private static JsonObject Merge(JsonObject? existing, JsonObject update)
    {
        if (existing is null)
            return update;
        foreach (var (key, value) in update.ToList())
        {
            update.Remove(key);
            existing[key] = value is JsonObject child && existing[key] is JsonObject current
                ? Merge(current, child)
                : value;
        }
        return existing;
    }

    private static void WriteAtomic(string path, string json)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temp = path + ".tmp";
        using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
        using (var writer = new StreamWriter(stream))
        {
            writer.Write(json);
            writer.Flush();
            stream.Flush(flushToDisk: true);
        }

        // The rename is the commit point: a crash before it leaves the old file untouched. While
        // anything has the target open (antivirus, indexer, an editor), the rename is refused, so
        // retry with backoff for about 1.5s before giving up. A failure leaves the old file intact.
        const int attempts = 12;
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                File.Move(temp, path, overwrite: true);
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                if (attempt < attempts)
                {
                    Thread.Sleep(Math.Min(20 * attempt, 200));
                    continue;
                }
                try
                {
                    File.Delete(temp);
                }
                catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException)
                {
                    // Leaving the temp file is harmless; the next save overwrites it.
                }
                throw new IOException($"Could not save {Path.GetFileName(path)}: another program is using it.", ex);
            }
        }
    }

    private void TryCopy(string from, string to)
    {
        try
        {
            File.Copy(from, to, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Could not copy {From} aside", from);
        }
    }

    private void TryMove(string from, string to)
    {
        try
        {
            File.Move(from, to, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Could not move {From} aside", from);
        }
    }
}
