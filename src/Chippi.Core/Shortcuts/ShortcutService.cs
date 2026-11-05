// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

namespace Chippi.Core.Shortcuts;

/// <summary>
/// One shortcut a feature offers (Mute microphone, a Quick action), set or not. It's listed in Settings › Shortcuts from
/// when it's added until it's disposed. Called on the UI thread; its action runs there.
/// </summary>
public interface IHotkey : IDisposable
{
    /// <summary>Takes <paramref name="shortcut"/> in place of the one it holds; false, keeping the old one, when it can't.</summary>
    bool Register(KeyShortcut shortcut, Action pressed);

    /// <summary>Frees its shortcut and stays listed, unset.</summary>
    void Unregister() { }

    /// <summary>Why the last <see cref="Register"/> failed: another of Chippi's shortcuts holds it, or another app does.</summary>
    string? Error => null;
}

/// <summary>Windows' side of the shortcuts: <c>RegisterHotKey</c> on one message-only window, its presses on the UI thread.</summary>
public interface IHotkeyRegistrar
{
    /// <summary>False when Windows refuses: another app holds it.</summary>
    bool Register(int id, KeyShortcut shortcut);

    void Unregister(int id);

    event Action<int>? Pressed;
}

/// <summary>A shortcut as Settings › Shortcuts lists it.</summary>
/// <param name="Owner">The feature that offers it ("mic-mute").</param>
/// <param name="Name">What it does ("Mute microphone").</param>
/// <param name="Shortcut">The keys it holds; null while unset.</param>
/// <param name="Error">Why the last change didn't take, else null.</param>
public sealed record ShortcutEntry(string Owner, string Name, KeyShortcut? Shortcut, string? Error);

/// <summary>Every shortcut Chippi holds (docs/phase-6/architecture.md §4), in the order the features added them.</summary>
public interface IShortcutService
{
    /// <summary>Adds a shortcut <paramref name="owner"/> offers, unset. Called on the UI thread.</summary>
    IHotkey Add(string owner, string name);

    IReadOnlyList<ShortcutEntry> List { get; }

    /// <summary>A shortcut was added, set, refused or removed. Raised on the UI thread.</summary>
    event Action? Changed;
}

/// <summary>
/// The shortcut service. One of Chippi's shortcuts never takes keys another one holds; that's refused here with its name,
/// before Windows is asked. Windows refusing means another app holds them.
/// </summary>
public sealed class ShortcutService : IShortcutService
{
    public const string InUse = "Shortcut in use";

    private readonly IHotkeyRegistrar _registrar;
    private readonly List<Entry> _entries = [];
    private int _nextId;

    public ShortcutService(IHotkeyRegistrar registrar)
    {
        _registrar = registrar;
        _registrar.Pressed += id => _entries.FirstOrDefault(e => e.Id == id && id != 0)?.Pressed?.Invoke();
    }

    public event Action? Changed;

    public IReadOnlyList<ShortcutEntry> List => [.. _entries.Select(e => new ShortcutEntry(e.Owner, e.Name, e.Shortcut, e.Error))];

    public IHotkey Add(string owner, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var entry = new Entry(this, owner, name);
        _entries.Add(entry);
        Changed?.Invoke();
        return entry;
    }

    private bool Register(Entry entry, KeyShortcut shortcut, Action pressed)
    {
        if (entry.Disposed)
            return false;
        if (entry.Id != 0 && entry.Shortcut == shortcut)
        {
            entry.Pressed = pressed;
            entry.Error = null;
            return true;
        }
        if (_entries.FirstOrDefault(e => !ReferenceEquals(e, entry) && e.Id != 0 && e.Shortcut == shortcut) is { } holder)
            return Refuse(entry, $"Used by {holder.Name}");
        int id = ++_nextId;
        bool taken;
        try { taken = shortcut.IsValid && _registrar.Register(id, shortcut); }
        catch (Exception ex)
        {
            Diagnostics.Log.Warn("Shortcuts: registering failed", ex);
            taken = false;
        }
        if (!taken)
            return Refuse(entry, InUse);
        // The new keys are held before the old ones go, so a refused change keeps the shortcut working.
        if (entry.Id != 0)
            _registrar.Unregister(entry.Id);
        entry.Id = id;
        entry.Shortcut = shortcut;
        entry.Pressed = pressed;
        entry.Error = null;
        Changed?.Invoke();
        return true;
    }

    private bool Refuse(Entry entry, string error)
    {
        entry.Error = error;
        Changed?.Invoke();
        return false;
    }

    private void Unregister(Entry entry)
    {
        if (entry.Id != 0)
            _registrar.Unregister(entry.Id);
        entry.Id = 0;
        entry.Shortcut = null;
        entry.Pressed = null;
        entry.Error = null;
        Changed?.Invoke();
    }

    private void Remove(Entry entry)
    {
        if (entry.Disposed)
            return;
        if (entry.Id != 0)
            _registrar.Unregister(entry.Id);
        entry.Id = 0;
        entry.Pressed = null;
        entry.Disposed = true;
        _entries.Remove(entry);
        Changed?.Invoke();
    }

    private sealed class Entry(ShortcutService service, string owner, string name) : IHotkey
    {
        public string Owner { get; } = owner;
        public string Name { get; } = name;
        public int Id;
        public KeyShortcut? Shortcut;
        public Action? Pressed;
        public bool Disposed;

        public string? Error { get; set; }

        public bool Register(KeyShortcut shortcut, Action pressed) => service.Register(this, shortcut, pressed);

        public void Unregister() => service.Unregister(this);

        public void Dispose() => service.Remove(this);
    }
}

/// <summary>A registrar that refuses every shortcut: a context with no shell (tests) holds none.</summary>
public sealed class RefusingRegistrar : IHotkeyRegistrar
{
    public static RefusingRegistrar Instance { get; } = new();

    public bool Register(int id, KeyShortcut shortcut) => false;

    public void Unregister(int id) { }

    public event Action<int>? Pressed { add { } remove { } }
}
