// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

namespace Chippi.Core.Actions;

/// <summary>
/// The PC's side of an action: the windows, keys, media and programs it acts on. Nothing here runs in tests or dev runs
/// (docs/phase-5.5-handoff.md §3): tests pass a fake. Each action throws <see cref="ActionFailure"/> when Windows refuses.
/// </summary>
public interface IActionPc
{
    /// <summary>Whether the Start menu holds the app (its parsing name); null until the Start menu has been read.</summary>
    bool? HasApp(string parsingName);

    /// <summary>Whether the file or folder is there.</summary>
    bool Exists(string path);

    /// <summary>The top-level window under the pointer, or 0 for the desktop, the taskbar or one of Chippi's.</summary>
    nint WindowUnderPointer();

    /// <summary>The window in front, or 0 for the desktop, the taskbar or one of Chippi's.</summary>
    nint WindowInFront();

    /// <summary>Opens a file, folder, app, Settings page or web address as Explorer would.</summary>
    void Open(string target);

    void Window(nint window, WindowAction action);

    ActionWindow? CaptureWindow(nint window) => null;
    bool CanRestore(ActionWindow window) => false;
    bool CanSnap(ActionWindow window) => !window.Elevated;
    void BeginWindowDrag(ActionWindow window) { }
    void EndWindowDrag(ActionWindow window) { }
    void Window(ActionWindow window, WindowAction action) => Window(window.Handle, action);
    void WindowZone(ActionWindow window, string layoutId, string zoneId, nint monitor = 0) => throw new ActionFailure("Unavailable");

    void Media(MediaAction action);

    /// <summary>Presses the keys in the app in front, once the keys still held are let go.</summary>
    Task PressAsync(KeyCombo keys);

    /// <summary>Types the text at the cursor of the app in front, as keystrokes, without the clipboard.</summary>
    Task TypeAsync(string text);

    /// <summary>Runs the line hidden, without waiting for it.</summary>
    void Run(CommandShell shell, string line);

    /// <summary>
    /// The text selected in a terminal in front, read through UI Automation, since Ctrl+C there would stop what runs when
    /// nothing is selected. Null for any other window (the clipboard hub copies it), or when nothing is selected.
    /// </summary>
    Task<(bool Terminal, string? Text)> TerminalSelectionAsync();
}

/// <summary>Records a Key shortcut action's keys. The real one takes the keyboard through the input hub; tests use a fake.</summary>
public interface IKeyRecorder
{
    /// <summary>
    /// Takes the keyboard until a key is pressed with whatever modifiers are down, then calls <paramref name="recorded"/>
    /// with them on the calling (UI) thread, or with null for Esc alone, and lets go once those keys are up. Dispose the
    /// handle to stop early.
    /// </summary>
    IDisposable Record(Action<KeyCombo?> recorded);
}
