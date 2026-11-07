// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Media.Imaging;

namespace Chippi.Features.ScreenText;

/// <summary>One line the engine read: where it sits in <see cref="TextResult.Text"/> and where it was in the capture, in the capture's pixels.</summary>
public sealed record TextLine(int Start, int Length, Rect Box);

/// <summary>How a read ended.</summary>
public enum TextReadState
{
    /// <summary>Text was found.</summary>
    Read,

    /// <summary>The engine found nothing ("No text found").</summary>
    NoText,

    /// <summary>No OCR language is installed in Windows.</summary>
    NoLanguage,

    /// <summary>The engine failed ("Text recognition failed").</summary>
    Failed,
}

/// <summary>What Extract text read: the captured region, the text with its lines, and the language used.</summary>
/// <param name="Capture">The region that was read, at its own pixels. Released when the page closes.</param>
/// <param name="Language">The OCR language's name, such as "English".</param>
public sealed record TextResult(BitmapSource Capture, string Text, IReadOnlyList<TextLine> Lines, string Language)
{
    public TextReadState State { get; init; } = TextReadState.Read;
}

/// <summary>
/// What the island's Text page shows and does (text-and-qr.md, Extract text; Lumen's Capture results v3). The feature's
/// module implements it; the page never reads the screen or opens the clipboard itself.
/// </summary>
public interface ITextPageModel
{
    /// <summary>The last read, or null before the first.</summary>
    TextResult? Result { get; }

    /// <summary>A Reselect or Retry is reading: the page keeps the last text until the new one lands.</summary>
    bool Reading { get; }

    /// <summary>Raised on any thread when <see cref="Result"/> or <see cref="Reading"/> changes.</summary>
    event Action? Changed;

    /// <summary>Copies the text through the clipboard hub; false when the write was refused ("Copy failed").</summary>
    bool Copy(string text);

    /// <summary>The selector again; the text is replaced only after the new region is read.</summary>
    void Reselect();

    /// <summary>Reads the same region again, in the language set now.</summary>
    void Retry();
}

/// <summary>What a QR code holds, by its payload's form.</summary>
public enum QrKind
{
    Link,
    Text,
    WiFi,
    Contact,
    Email,
    Phone,
    Message,
    Location,
    Other,
}

/// <summary>A named part of a code's payload: a Wi-Fi network's Name, Security and Password, or a contact's lines.</summary>
/// <param name="Secret">Shown as dots until Show (a Wi-Fi password).</param>
public sealed record QrField(string Name, string Value, bool Secret = false);

/// <summary>One decoded code.</summary>
/// <param name="Payload">Everything the code says, as decoded. Copy writes it.</param>
/// <param name="Image">The code as it was in the capture, or null.</param>
public sealed record QrCodeResult(QrKind Kind, string Payload, BitmapSource? Image)
{
    /// <summary>An absolute http or https address, the only payload Open link opens; null for every other.</summary>
    public Uri? Link { get; init; }

    /// <summary>The link's host has non-ASCII characters or an <c>xn--</c> label: the page says "Check the address".</summary>
    public bool CheckAddress { get; init; }

    /// <summary>The payload's parts, shown in place of it: Wi-Fi and contacts. Empty shows the payload.</summary>
    public IReadOnlyList<QrField> Fields { get; init; } = [];
}

/// <summary>How a scan ended.</summary>
public enum QrScanState
{
    /// <summary>One or more codes were decoded.</summary>
    Read,

    /// <summary>The decoder saw no code ("No QR code found").</summary>
    NotFound,

    /// <summary>A code was seen but couldn't be decoded ("Unreadable QR code").</summary>
    Unreadable,
}

/// <summary>
/// What the island's QR code page shows and does (text-and-qr.md, Scan QR; Capture results v3). The feature's module
/// implements it. The page opens a link only from Open link, and never on its own.
/// </summary>
public interface IQrPageModel
{
    /// <summary>The codes found, top to bottom then left to right.</summary>
    IReadOnlyList<QrCodeResult> Codes { get; }

    QrScanState State { get; }

    /// <summary>Raised on any thread when the codes or the state change.</summary>
    event Action? Changed;

    /// <summary>Copies a payload through the clipboard hub; false when the write was refused ("Copy failed").</summary>
    bool Copy(string payload);

    /// <summary>Hands the exact address to the default browser, once.</summary>
    void Open(Uri link);

    /// <summary>The selector again, for another scan.</summary>
    void Reselect();
}
