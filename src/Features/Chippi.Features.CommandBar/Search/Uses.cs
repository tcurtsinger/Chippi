// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.IO;
using System.Text.Json;
using Chippi.Core.Diagnostics;

namespace Chippi.Features.CommandBar.Search;

/// <summary>
/// What you pick, stored only on your PC (uses.json in the feature's data folder): how often each result was run for the
/// letters typed, which ranks it higher next time, and in all, which fills the empty field with your most used.
/// </summary>
public sealed class Uses
{
    /// <summary>The longest typed text remembered; longer text counts toward its first letters.</summary>
    internal const int Letters = 12;

    /// <summary>At most this many typed texts are remembered; the least picked go first.</summary>
    internal const int Texts = 2000;

    private readonly string? _path;
    private readonly Lock _gate = new();
    private readonly Dictionary<string, int> _total = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Dictionary<string, int>> _typed = new(StringComparer.Ordinal);
    private Task _saving = Task.CompletedTask;

    /// <param name="path">uses.json; null keeps them in memory only (tests).</param>
    public Uses(string? path = null)
    {
        _path = path;
        if (path is null || !File.Exists(path))
            return;
        try
        {
            var saved = JsonSerializer.Deserialize<Stored>(File.ReadAllText(path));
            foreach (var (key, count) in saved?.Total ?? [])
                _total[key] = count;
            foreach (var (text, picks) in saved?.Typed ?? [])
                _typed[text] = new Dictionary<string, int>(picks, StringComparer.Ordinal);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            Log.Warn("Command bar: reading uses.json failed; starting over", ex);
        }
    }

    /// <summary>How often <paramref name="key"/> was picked for <paramref name="text"/>.</summary>
    public int For(string text, string key)
    {
        lock (_gate)
            return _typed.TryGetValue(Normal(text), out var picks) && picks.TryGetValue(key, out int count) ? count : 0;
    }

    /// <summary>How often <paramref name="key"/> was picked at all.</summary>
    public int Total(string key)
    {
        lock (_gate)
            return _total.GetValueOrDefault(key);
    }

    /// <summary>Every key ever picked, most first.</summary>
    public IReadOnlyList<string> MostUsed()
    {
        lock (_gate)
            return [.. _total.OrderByDescending(p => p.Value).ThenBy(p => p.Key, StringComparer.Ordinal).Select(p => p.Key)];
    }

    /// <summary><paramref name="key"/> was run after typing <paramref name="text"/>: it counts for each of its first letters.</summary>
    public void Picked(string text, string key)
    {
        string typed = Normal(text);
        lock (_gate)
        {
            _total[key] = _total.GetValueOrDefault(key) + 1;
            for (int length = 1; length <= typed.Length; length++)
            {
                string start = typed[..length];
                if (!_typed.TryGetValue(start, out var picks))
                    _typed[start] = picks = new(StringComparer.Ordinal);
                picks[key] = picks.GetValueOrDefault(key) + 1;
            }
            if (_typed.Count > Texts)
            {
                foreach (var least in _typed.OrderBy(p => p.Value.Values.Sum()).Take(_typed.Count - Texts).Select(p => p.Key).ToList())
                    _typed.Remove(least);
            }
        }
        Save();
    }

    /// <summary>Forgets <paramref name="key"/> (hidden results keep nothing).</summary>
    public void Forget(string key)
    {
        lock (_gate)
        {
            _total.Remove(key);
            foreach (var picks in _typed.Values)
                picks.Remove(key);
        }
        Save();
    }

    private static string Normal(string text)
    {
        string typed = string.Join(' ', text.Split(' ', StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();
        return typed.Length > Letters ? typed[..Letters] : typed;
    }

    /// <summary>Written off the UI thread, one write after another.</summary>
    private void Save()
    {
        if (_path is null)
            return;
        string json;
        lock (_gate)
            json = JsonSerializer.Serialize(new Stored(new(_total), _typed.ToDictionary(p => p.Key, p => new Dictionary<string, int>(p.Value))));
        lock (_gate)
            _saving = _saving.ContinueWith(_ =>
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                    string temp = _path + ".tmp";
                    File.WriteAllText(temp, json);
                    File.Move(temp, _path, overwrite: true);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    Log.Warn("Command bar: saving uses.json failed", ex);
                }
            }, TaskScheduler.Default);
    }

    /// <summary>The writes so far are done (tests).</summary>
    internal Task Saved => _saving;

    private sealed record Stored(Dictionary<string, int>? Total, Dictionary<string, Dictionary<string, int>>? Typed);
}
