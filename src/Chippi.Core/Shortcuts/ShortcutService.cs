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

/// <summary>
/// How Settings › Shortcuts changes a feature's shortcut: the feature keeps its keys in its own settings, and Change and
/// Remove register and save them there, as its own Settings row does.
/// </summary>
/// <param name="Saved">The keys in the feature's settings; null while unset.</param>
/// <param name="Change">Takes and saves these keys; null when it did, else why not ("Save failed").</param>
/// <param name="Remove">Frees the keys and saves the shortcut unset; it stays listed.</param>
public sealed record ShortcutEdits(Func<KeyShortcut?> Saved, Func<KeyShortcut, string?> Change, Action Remove)
{
    /// <summary>The row's glyph, a Lucide name ("Mic").</summary>
    public string? Icon { get; init; }
}

/// <summary>A shortcut as Settings › Shortcuts lists it.</summary>
/// <param name="Owner">The feature that offers it ("mic-mute").</param>
/// <param name="Name">What it does ("Mute microphone").</param>
/// <param name="Shortcut">The keys it holds; for one Settings can change, the keys saved, held or not. Null while unset.</param>
/// <param name="Error">Why the last change didn't take, else null.</param>
public sealed record ShortcutEntry(string Owner, string Name, KeyShortcut? Shortcut, string? Error)
{
    /// <summary>The same for the shortcut's whole life; what <see cref="IShortcutService.Change"/> and Remove take.</summary>
    public long Id { get; init; }

    /// <summary>Its feature passed <see cref="ShortcutEdits"/>, so Settings › Shortcuts can change it.</summary>
    public bool Editable { get; init; }

    public string? Icon { get; init; }

    /// <summary>Windows gave Chippi <see cref="Shortcut"/>. Saved keys that aren't held draw dimmed.</summary>
    public bool Held { get; init; }
}

/// <summary>Every shortcut Chippi holds (docs/phase-6/architecture.md §4), in the order the features added them.</summary>
public interface IShortcutService
{
    /// <summary>Adds a shortcut <paramref name="owner"/> offers, unset. Called on the UI thread.</summary>
    IHotkey Add(string owner, string name);

    /// <summary>Adds a shortcut that Settings › Shortcuts can change through <paramref name="edits"/>.</summary>
    IHotkey Add(string owner, string name, ShortcutEdits edits);

    IReadOnlyList<ShortcutEntry> List { get; }

    /// <summary>
    /// Why <paramref name="keys"/> can't be recorded, in the words the editor shows by Save; null when they can.
    /// <paramref name="except"/> is the shortcut being changed: its own keys, held now, are fine.
    /// </summary>
    string? Check(KeyShortcut keys, long? except = null);

    /// <summary>
    /// Checks the keys, then has the feature take and save them: null when it did, else why not, in the same words as
    /// <see cref="Check"/> (or the feature's own, "Save failed").
    /// </summary>
    string? Change(long id, KeyShortcut keys);

    /// <summary>Has the feature free its keys and save the shortcut unset.</summary>
    void Remove(long id);

    /// <summary>
    /// A shortcut was added, set, refused or removed. Raised at once on the thread that made the change, which is the UI
    /// thread: every call here is made there.
    /// </summary>
    event Action? Changed;
}

/// <summary>
/// The shortcut service. One of Chippi's shortcuts never takes keys another one holds; that's refused here with its name,
/// before Windows is asked. Windows refusing means another app holds them.
/// </summary>
public sealed class ShortcutService : IShortcutService
{
    public const string InUse = "Shortcut in use";

    // The editor's words by Save (docs/phase-6/shortcuts.md, Travis 2026-11-05).
    public const string PickText = "Pick a shortcut with Ctrl, Alt or the Windows key.";
    public const string WindowsText = "Windows uses this";
    public const string AnotherAppText = "Another app uses this";
    public const string NotFoundText = "Not found";

    private readonly IHotkeyRegistrar _registrar;
    private readonly List<Entry> _entries = [];
    private int _nextId;
    private long _nextKey;

    public ShortcutService(IHotkeyRegistrar registrar)
    {
        _registrar = registrar;
        _registrar.Pressed += id => _entries.FirstOrDefault(e => e.Id == id && id != 0)?.Pressed?.Invoke();
    }

    public event Action? Changed;

    public IReadOnlyList<ShortcutEntry> List => [.. _entries.Select(Listed)];

    private static ShortcutEntry Listed(Entry e)
    {
        var keys = e.Edits is { } edits ? edits.Saved() : e.Shortcut;
        return new ShortcutEntry(e.Owner, e.Name, keys, e.Error)
        {
            Id = e.Key,
            Editable = e.Edits is not null,
            Icon = e.Edits?.Icon,
            Held = e.Id != 0 && keys is not null && e.Shortcut == keys,
        };
    }

    public IHotkey Add(string owner, string name) => Add(owner, name, null);

    public IHotkey Add(string owner, string name, ShortcutEdits? edits)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var entry = new Entry(this, owner, name) { Key = ++_nextKey, Edits = edits };
        _entries.Add(entry);
        Changed?.Invoke();
        return entry;
    }

    public string? Check(KeyShortcut keys, long? except = null)
    {
        if (!keys.IsBindable)
            return PickText;
        var own = except is { } key ? _entries.FirstOrDefault(e => e.Key == key) : null;
        if (_entries.FirstOrDefault(e => !ReferenceEquals(e, own) && e.Id != 0 && e.Shortcut == keys) is { } holder)
            return $"{holder.Name} already uses this";
        if (WindowsKeys.IsReserved(keys))
            return WindowsText;
        // Its own keys, held now: nothing to ask. Saved keys another app took aren't held, so they're asked about again.
        if (own is { Id: not 0 } && own.Shortcut == keys)
            return null;
        // Windows says only whether the keys are free: ask it, and let go at once.
        int id = ++_nextId;
        bool free = false;
        try { free = _registrar.Register(id, keys); }
        catch (Exception ex) { Diagnostics.Log.Warn("Shortcuts: checking a shortcut failed", ex); }
        finally
        {
            if (free)
                _registrar.Unregister(id);
        }
        return free ? null : AnotherAppText;
    }

    public string? Change(long id, KeyShortcut keys)
    {
        if (_entries.FirstOrDefault(e => e.Key == id) is not { Edits: { } edits })
            return NotFoundText;
        if (Check(keys, id) is { } refused)
            return refused;
        string? failed;
        try { failed = edits.Change(keys); }
        catch (Exception ex)
        {
            Diagnostics.Log.Warn("Shortcuts: changing a shortcut failed", ex);
            failed = "Change failed";
        }
        Changed?.Invoke(); // after the feature saved, so the list reads its new keys
        // Taken between the check and the feature's registering: the same words the check would have said.
        const string usedBy = "Used by ";
        if (failed == InUse)
            return AnotherAppText;
        return failed?.StartsWith(usedBy, StringComparison.Ordinal) == true ? $"{failed[usedBy.Length..]} already uses this" : failed;
    }

    public void Remove(long id)
    {
        if (_entries.FirstOrDefault(e => e.Key == id) is not { Edits: { } edits })
            return;
        try { edits.Remove(); }
        catch (Exception ex) { Diagnostics.Log.Warn("Shortcuts: removing a shortcut failed", ex); }
        Changed?.Invoke();
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
        try { taken = (shortcut.IsValid || shortcut.IsBindable) && _registrar.Register(id, shortcut); }
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
        if (entry.Disposed)
            return;
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
        public long Key { get; init; }
        public ShortcutEdits? Edits { get; init; }
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
