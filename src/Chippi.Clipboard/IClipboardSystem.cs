// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.Core.Clipboard;

namespace Chippi.Clipboard;

/// <summary>Clipboard format names. Standard formats use their CF_ names; the rest are registered names.</summary>
public static class ClipFormat
{
    public const string UnicodeText = "CF_UNICODETEXT";
    public const string Dib = "CF_DIB";
    public const string DibV5 = "CF_DIBV5";
    public const string Files = "CF_HDROP";
    public const string Html = "HTML Format";
    public const string Rtf = "Rich Text Format";
    public const string DropEffect = "Preferred DropEffect";

    /// <summary>Don't monitor this copy at all. Presence alone counts.</summary>
    public const string Exclude = "ExcludeClipboardContentFromMonitorProcessing";

    /// <summary>The legacy viewer opt-out (KeePass and others). Presence alone counts.</summary>
    public const string ViewerIgnore = "Clipboard Viewer Ignore";

    /// <summary>DWORD: 0 keeps the copy out of clipboard history.</summary>
    public const string History = "CanIncludeInClipboardHistory";

    /// <summary>DWORD: cloud sync only. Ignored for local history; kept on restore.</summary>
    public const string Cloud = "CanUploadToCloudClipboard";
}

/// <summary>
/// The raw clipboard, so <see cref="ClipboardHub"/>'s rules run the same against the real one
/// (<see cref="Windows.WindowsClipboardSystem"/>) and a fake. Members outside a session never open the clipboard.
/// </summary>
public interface IClipboardSystem : IDisposable
{
    /// <summary>The window station's clipboard sequence number; it changes with every new copy.</summary>
    uint Sequence { get; }

    /// <summary>Whether a format is on the clipboard, without opening it or reading any content.</summary>
    bool IsAvailable(string format);

    /// <summary>The executable path of the window that owns the clipboard, or null.</summary>
    string? OwnerPath();

    /// <summary>Raised for every clipboard change while <see cref="Listening"/> is on. On the clipboard's own thread: hand off at once.</summary>
    event Action? Changed;

    bool Listening { get; set; }

    /// <summary>
    /// Opens the clipboard (retrying briefly), runs <paramref name="work"/>, closes it. Throws
    /// <see cref="ClipboardBusyException"/> when another app held it through every retry.
    /// </summary>
    Task<T> TransactAsync<T>(Func<IClipboardSession, T> work, CancellationToken cancel = default);

    /// <summary>
    /// <see cref="TransactAsync"/> for a write, also returning the sequence number of the copy it left, read
    /// once the clipboard is closed (Windows moves it again on close). 0 when nothing was written or another
    /// app's copy already replaced it, so a later copy is never taken for Chippi's.
    /// </summary>
    Task<(T Result, uint Sequence)> WriteAsync<T>(Func<IClipboardSession, T> work, CancellationToken cancel = default);
}

/// <summary>The clipboard while Chippi holds it open. Valid only inside its transaction.</summary>
public interface IClipboardSession
{
    uint Sequence { get; }

    string? OwnerPath();

    bool IsAvailable(string format);

    /// <summary>The formats on the clipboard that hold plain memory (what a restore can copy back).</summary>
    IReadOnlyList<string> Formats();

    /// <summary>The format's allocation size in bytes, or -1 when absent. May make the source app render it.</summary>
    long SizeOf(string format);

    /// <summary>The format's bytes, or null when absent.</summary>
    byte[]? Get(string format);

    /// <summary>Clears the clipboard and makes Chippi its owner.</summary>
    void Empty();

    void Set(string format, byte[] data);

    /// <summary>
    /// Offers a format without its data. When an app asks for it, <paramref name="render"/> supplies the
    /// bytes and <paramref name="requested"/> is told (the first data request, not proof of a paste).
    /// </summary>
    void SetDelayed(string format, Func<byte[]> render, Action? requested = null);
}

public sealed class ClipboardBusyException() : Exception("Clipboard busy");

/// <summary>The window in front and the keyboard, for pasting (architecture §5).</summary>
public interface IForeground
{
    /// <summary>The foreground window, unless it's Chippi's own or there is none.</summary>
    PasteTarget? Current();

    bool IsAlive(PasteTarget target);

    /// <summary>
    /// Whether keystrokes can reach the target: its integrity level is readable and not above Chippi's.
    /// UIPI drops input to an elevated window without an error, so this is checked before sending.
    /// </summary>
    bool CanSendInput(PasteTarget target);

    /// <summary>Makes the target the foreground window and waits until it is, up to <paramref name="timeout"/>.</summary>
    Task<bool> BringToFrontAsync(PasteTarget target, TimeSpan timeout, CancellationToken cancel = default);

    /// <summary>Waits until Ctrl, Shift, Alt and Win are all up (a shortcut's keys), up to <paramref name="timeout"/>.</summary>
    Task WaitForModifiersReleasedAsync(TimeSpan timeout, CancellationToken cancel = default);

    /// <summary>Ctrl down, V down, V up, Ctrl up, in one SendInput call.</summary>
    void SendPaste();

    /// <summary>The Windows default beep.</summary>
    void Beep();
}
