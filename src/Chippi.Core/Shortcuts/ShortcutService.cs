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

    /// <summary>A Print Screen shortcut's error while Windows' own Print Screen setting is back on (capture-layer.md).</summary>
    public const string UsedByWindowsText = "Used by Windows";

    private readonly IHotkeyRegistrar _registrar;
    private readonly IPrintScreenSetting? _printScreen;
    private readonly List<Entry> _entries = [];
    private int _nextId;
    private long _nextKey;
    private bool _changing;
    private bool _closing;

    /// <param name="printScreen">
    /// Windows' Print Screen setting, which the service turns off while one of Chippi's shortcuts is Print Screen and puts
    /// back after; null leaves it alone.
    /// </param>
    public ShortcutService(IHotkeyRegistrar registrar, IPrintScreenSetting? printScreen = null)
    {
        _registrar = registrar;
        _registrar.Pressed += id => _entries.FirstOrDefault(e => e.Id == id && id != 0)?.Pressed?.Invoke();
        _printScreen = printScreen;
        if (_printScreen is not null)
            _printScreen.Changed += OnPrintScreenChanged;
    }

    /// <summary>
    /// The shell is closing: the features' shortcuts go as they stop, but Windows' Print Screen setting stays as it is,
    /// so the key is Chippi's again at the next start. Removing a shortcut or a feature otherwise puts it back.
    /// </summary>
    public void Closing() => _closing = true;

    /// <summary>
    /// After the features started: a hold on Print Screen that no shortcut uses any more (the feature went while Chippi
    /// wasn't running) puts Windows' setting back.
    /// </summary>
    public void Started() => ReleasePrintScreen();

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
        if (_entries.FirstOrDefault(e => !ReferenceEquals(e, own) && Holds(e) && e.Shortcut == keys) is { } holder)
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
        _changing = true;
        try { failed = edits.Change(keys); }
        catch (Exception ex)
        {
            Diagnostics.Log.Warn("Shortcuts: changing a shortcut failed", ex);
            failed = "Change failed";
        }
        finally { _changing = false; }
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
            ClaimPrintScreen(shortcut, out string? windows);
            if (windows is not null)
            {
                Yield(entry);
                Changed?.Invoke();
            }
            return true;
        }
        if (_entries.FirstOrDefault(e => !ReferenceEquals(e, entry) && Holds(e) && e.Shortcut == shortcut) is { } holder)
            return Refuse(entry, $"Used by {holder.Name}");
        // Windows' own Print Screen goes off before the key is registered: a Print Screen hotkey registered while it's on
        // fires only with Chippi's own window in front, and while it's on, Windows keeps the key only if Chippi lets go
        // (the capture layer's Sandbox check, 2026-11-06).
        ClaimPrintScreen(shortcut, out string? usedByWindows);
        if (usedByWindows is not null)
        {
            if (entry.Id != 0)
                _registrar.Unregister(entry.Id);
            entry.Id = 0;
            entry.Shortcut = shortcut;
            entry.Pressed = pressed;
            entry.Yielded = true;
            entry.Error = usedByWindows;
            Changed?.Invoke();
            return true;
        }
        int id = ++_nextId;
        bool taken;
        try { taken = (shortcut.IsValid || shortcut.IsBindable) && _registrar.Register(id, shortcut); }
        catch (Exception ex)
        {
            Diagnostics.Log.Warn("Shortcuts: registering failed", ex);
            taken = false;
        }
        if (!taken)
        {
            ReleasePrintScreen();
            return Refuse(entry, InUse);
        }
        // The new keys are held before the old ones go, so a refused change keeps the shortcut working.
        if (entry.Id != 0)
            _registrar.Unregister(entry.Id);
        entry.Id = id;
        entry.Shortcut = shortcut;
        entry.Pressed = pressed;
        entry.Yielded = false;
        entry.Error = null;
        ReleasePrintScreen(); // the old keys may have been Print Screen
        Changed?.Invoke();
        return true;
    }

    /// <summary>Set to keys Chippi holds, or to Print Screen while Windows' own has it.</summary>
    private static bool Holds(Entry e) => e.Id != 0 || e.Yielded;

    /// <summary>Windows' own Print Screen is on: the entry lets go of the key and keeps it set, to take it back once it's off.</summary>
    private void Yield(Entry entry)
    {
        if (entry.Id != 0)
            _registrar.Unregister(entry.Id);
        entry.Id = 0;
        entry.Yielded = true;
        entry.Error = UsedByWindowsText;
    }

    /// <summary>Windows' own Print Screen is off again: the entry registers the key again, or reads in use when another app took it.</summary>
    private void TakeBack(Entry entry)
    {
        int id = ++_nextId;
        bool taken;
        try { taken = _registrar.Register(id, entry.Shortcut!); }
        catch (Exception ex)
        {
            Diagnostics.Log.Warn("Shortcuts: registering failed", ex);
            taken = false;
        }
        entry.Yielded = false;
        entry.Id = taken ? id : 0;
        entry.Error = taken ? null : InUse;
    }

    private static bool IsPrintScreen(KeyShortcut? keys) => keys?.Key == KeyShortcut.PrintScreen;

    /// <summary>Windows' value says its own Print Screen is on: unset counts as on.</summary>
    private static bool WindowsHasIt(int? value) => value != 0;

    /// <summary>
    /// An entry is about to hold Print Screen. The first hold saves what Windows' setting was and turns it off. While held,
    /// the setting found on again (Travis turned it back on, maybe while Chippi wasn't running) is left on, and the entry
    /// lets go of the key and reads "Used by Windows", until he records Print Screen again, which turns it off again.
    /// </summary>
    /// <param name="error">"Used by Windows" when Windows keeps the key.</param>
    private void ClaimPrintScreen(KeyShortcut keys, out string? error)
    {
        error = null;
        if (_printScreen is null || !IsPrintScreen(keys))
            return;
        try
        {
            int? value = _printScreen.Read();
            if (_printScreen.Claim is null)
            {
                _printScreen.Claim = new PrintScreenClaim(value);
                if (value != 0)
                    _printScreen.Write(0);
            }
            else if (WindowsHasIt(value))
            {
                if (_changing)
                    _printScreen.Write(0);
                else
                    error = UsedByWindowsText;
            }
        }
        catch (Exception ex) { Diagnostics.Log.Warn("Shortcuts: turning off Windows' Print Screen setting failed", ex); }
    }

    /// <summary>No entry holds Print Screen any more: Windows' setting goes back to what Chippi found.</summary>
    private void ReleasePrintScreen()
    {
        if (_printScreen?.Claim is not { } claim || _closing || _entries.Any(e => Holds(e) && IsPrintScreen(e.Shortcut)))
            return;
        try
        {
            _printScreen.Write(claim.Found);
            _printScreen.Claim = null;
        }
        catch (Exception ex) { Diagnostics.Log.Warn("Shortcuts: putting back Windows' Print Screen setting failed", ex); }
    }

    /// <summary>Travis flipped Windows' switch: the Print Screen entries say so, or stop saying so.</summary>
    private void OnPrintScreenChanged()
    {
        if (_printScreen?.Claim is null)
            return;
        int? value;
        try { value = _printScreen.Read(); }
        catch (Exception ex)
        {
            Diagnostics.Log.Warn("Shortcuts: reading Windows' Print Screen setting failed", ex);
            return;
        }
        bool changed = false;
        foreach (var e in _entries.Where(e => Holds(e) && IsPrintScreen(e.Shortcut)))
        {
            if (WindowsHasIt(value) && !e.Yielded)
                Yield(e);
            else if (!WindowsHasIt(value) && e.Yielded)
                TakeBack(e);
            else
                continue;
            changed = true;
        }
        if (changed)
            Changed?.Invoke();
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
        entry.Yielded = false;
        entry.Error = null;
        ReleasePrintScreen();
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
        entry.Yielded = false;
        entry.Disposed = true;
        _entries.Remove(entry);
        ReleasePrintScreen();
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
        public bool Yielded; // Print Screen, set but let go while Windows' own has it

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
