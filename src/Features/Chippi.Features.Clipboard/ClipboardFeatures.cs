// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

namespace Chippi.Features.Clipboard;

/// <summary>
/// What Clipboard history and Clean URL share: the Clipboard tile and its page (design notes #1, #23).
/// Each module sets itself here while it runs, so the page knows which views it has. The app makes one
/// and passes it to both modules.
/// </summary>
public sealed class ClipboardFeatures
{
    /// <summary>The Clipboard tile's key, shared by both modules.</summary>
    public const string TileKey = "clipboard";

    /// <summary>Clipboard and Scratchpad are 440 wide, like Now Playing (notes #6).</summary>
    public const double PageWidth = 440;

    private ClipboardHistoryModule? _history;
    private CleanUrlModule? _cleanUrl;

    /// <summary>Clipboard history, while it runs.</summary>
    public ClipboardHistoryModule? History
    {
        get => _history;
        internal set
        {
            _history = value;
            Changed?.Invoke();
        }
    }

    /// <summary>Clean URL, while it runs.</summary>
    public CleanUrlModule? CleanUrl
    {
        get => _cleanUrl;
        internal set
        {
            _cleanUrl = value;
            Changed?.Invoke();
        }
    }

    /// <summary>
    /// The app that was in front before the island took the keyboard, for History's Paste (notes #62);
    /// null when the shell has none to give, and Paste copies instead.
    /// </summary>
    public Func<Core.Clipboard.PasteTarget?>? PasteTarget { get; set; }

    /// <summary>Closes the island after a paste was sent (notes #62).</summary>
    public Action? CloseIsland { get; set; }

    /// <summary>Raised when a module starts or stops, on the thread that did it.</summary>
    public event Action? Changed;

    /// <summary>The link automatic cleaning puts in place of <paramref name="text"/>, or null (notes #28).</summary>
    internal string? AutoCleaned(string text) => _cleanUrl?.Cleaner?.Cleaned(text);
}
