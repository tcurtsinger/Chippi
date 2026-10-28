// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

namespace Chippi.Core.Clipboard;

/// <summary>
/// The one clipboard service the clipboard features share (Phase 5 brief §3). Owned by the shell; no
/// module opens the clipboard itself. Every read passes the privacy gate while Chippi holds the
/// clipboard, before any content is read. Nothing listens unless someone holds a <see cref="Listen"/>
/// lease: History while it records, Clean URL while automatic cleaning is on.
/// </summary>
public interface IClipboardHub
{
    /// <summary>
    /// Calls <paramref name="copied"/> (on a worker, never with the clipboard open) for each new copy
    /// that passes the gate: not private, not one of Chippi's own writes. A copy marked
    /// <see cref="ClipPrivacy.NoHistory"/> is still delivered, flagged, so History can drop it and
    /// automatic cleaning can leave it alone. A copy whose owner <paramref name="skip"/> matches (by
    /// executable path) isn't read for this lease. Dispose the lease to stop; the last lease stops the listener.
    /// </summary>
    IDisposable Listen(Action<ClipCopy> copied, Func<string, bool>? skip = null);

    /// <summary>Reads the current copy on demand (Clean URL's prefill, plain paste). Never starts the listener.</summary>
    Task<ClipRead> ReadAsync(CancellationToken cancel = default);

    /// <summary>
    /// Puts <paramref name="content"/> on the clipboard as a new copy (History's Copy, Shelf's Copy, Clean
    /// URL's Copy and its automatic rewrite). Listeners don't receive Chippi's own writes.
    /// </summary>
    Task<ClipStatus> WriteAsync(ClipContent content, CancellationToken cancel = default);

    /// <summary>
    /// The window in front right now, captured before the island takes the keyboard. Null when there is
    /// none or it's Chippi's own.
    /// </summary>
    PasteTarget? CaptureTarget();

    /// <summary>
    /// Pastes <paramref name="content"/> into <paramref name="target"/>: puts it on the clipboard (marked
    /// so no clipboard manager records it), brings the target back, waits for the shortcut's keys to be
    /// released, sends Ctrl+V, then puts the previous copy back unless something newer was copied.
    /// Best effort: a slow target can read after the restore. An elevated, unreadable or vanished
    /// target gets no keystrokes; the content stays as the copy (<see cref="PasteOutcome.Copied"/>).
    /// </summary>
    Task<PasteOutcome> PasteAsync(ClipContent content, PasteTarget target, CancellationToken cancel = default);

    /// <summary>
    /// Plain paste: the current copy's text, without formatting, pasted into the window in front, then
    /// the original copy restored. A private copy works too (notes #20) and is never recorded.
    /// </summary>
    Task<PasteOutcome> PastePlainTextAsync(CancellationToken cancel = default);

    /// <summary>Plays the Windows default beep (plain paste's failure sound, notes #19).</summary>
    void Beep();
}

/// <summary>Why a copy may not be used (architecture §3).</summary>
[Flags]
public enum ClipPrivacy
{
    None = 0,

    /// <summary><c>CanIncludeInClipboardHistory</c> was 0 (or malformed): keep it out of History, don't rewrite it.</summary>
    NoHistory = 1,
}

public enum ClipStatus
{
    Ok,

    /// <summary>Another app held the clipboard through every retry ("Clipboard busy").</summary>
    Busy,

    /// <summary>Marked <c>ExcludeClipboardContentFromMonitorProcessing</c> or <c>Clipboard Viewer Ignore</c>: nothing was read.</summary>
    Private,

    /// <summary>The clipboard is empty, or holds no format Chippi uses.</summary>
    Empty,

    /// <summary>Over a limit (<see cref="ClipLimits"/>); <see cref="ClipRead.Limit"/> says which.</summary>
    TooLarge,
}

public enum ClipLimit { None, Text, Image, Files }

public enum PasteOutcome
{
    /// <summary>Ctrl+V went to the target. Not proof the text landed: nothing can tell.</summary>
    Sent,

    /// <summary>No keystrokes: the target is elevated, unreadable or gone. The content is now the copy ("Plain text copied").</summary>
    Copied,

    /// <summary>No text to paste: pictures or files only ("No text copied").</summary>
    NoText,

    Busy,
}

/// <summary>Fixed caps (scope.md): text in actual UTF-8 bytes, a picture's encoded size and decoded pixels, paths per copy.</summary>
public static class ClipLimits
{
    public const int TextUtf8Bytes = 1 << 20;

    /// <summary>The raw CF_UNICODETEXT allocation read before decoding (UTF-16 plus padding).</summary>
    public const int TextRawBytes = 4 << 20;

    public const int ImageEncodedBytes = 16 << 20;

    /// <summary>3840 × 2160 × 2.</summary>
    public const long ImagePixels = 16_588_800;

    public const int Files = 100;

    /// <summary>History and Shelf each, pins included.</summary>
    public const long StoreBytes = 128L << 20;
}

/// <summary>A copied picture as a 32-bit top-down BGRA bitmap. The page encodes PNG and thumbnails on its worker.</summary>
public sealed record ClipImage(int Width, int Height, byte[] Bgra);

/// <summary>
/// One copy's usable content: files win over their fallback text, then a picture, then text
/// (behavior-spec §3). Rich formats are kept as their plain text.
/// </summary>
public sealed record ClipContent
{
    public string? Text { get; init; }

    public ClipImage? Image { get; init; }

    /// <summary>Full paths, at most <see cref="ClipLimits.Files"/>.</summary>
    public IReadOnlyList<string>? Files { get; init; }

    public static ClipContent FromText(string text) => new() { Text = text };

    public static ClipContent FromFiles(IReadOnlyList<string> files) => new() { Files = files };

    public static ClipContent FromImage(ClipImage image) => new() { Image = image };

    public bool IsEmpty => Files is not { Count: > 0 } && Image is null && string.IsNullOrEmpty(Text);
}

/// <summary>
/// A copy the listener delivered. Over a limit, <see cref="Content"/> is null and <see cref="Limit"/> says
/// which ("Picture not saved · over 16 MiB").
/// </summary>
/// <param name="Sequence">The clipboard sequence number it was read at.</param>
public sealed record ClipCopy(uint Sequence, DateTimeOffset At, ClipContent? Content, ClipPrivacy Privacy, ClipLimit Limit = ClipLimit.None);

/// <summary>An on-demand read: <see cref="Content"/> is set only when <see cref="Status"/> is Ok.</summary>
public sealed record ClipRead(ClipStatus Status, ClipContent? Content = null, ClipPrivacy Privacy = ClipPrivacy.None, ClipLimit Limit = ClipLimit.None);

/// <summary>A window to paste into, remembered before the island takes the keyboard.</summary>
public sealed record PasteTarget(nint Window, uint ProcessId);
