// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

namespace Chippi.Features.TextSnippets;

/// <summary>A trigger the typed text just completed. <see cref="Typed"/> characters are in the app and must be deleted.</summary>
internal readonly record struct SnippetMatch(Snippet Snippet, int Typed);

/// <summary>
/// What was typed since the buffer last reset, up to 64 characters (docs/phase-6/text-snippets.md, Watching and Matching).
/// Memory only: never logged, cleared on a match. Called from the hub's thread, one key at a time.
/// </summary>
internal sealed class SnippetMatcher
{
    public const int Capacity = 64;

    private readonly char[] _buffer = new char[Capacity];
    private int _length;
    private volatile Snippet[] _snippets = [];

    /// <summary>The enabled snippets, longest trigger first so the first hit is the longest. Swapped whole, from any thread.</summary>
    public void SetSnippets(IEnumerable<Snippet> snippets) =>
        _snippets = [.. snippets.Where(s => s.Enabled && s.Trigger.Length >= SnippetRules.MinTrigger).OrderByDescending(s => s.Trigger.Length)];

    public int Length => _length;

    public void Reset() => _length = 0;

    public void Backspace()
    {
        if (_length > 0)
            _length--;
    }

    /// <summary>A character was typed. A right-away snippet it completes matches now; the buffer clears on a match.</summary>
    public SnippetMatch? Type(char c)
    {
        if (c == ' ' || c == '\t')
        {
            _length = 0; // a trigger holds no space, so nothing before one can match
            return null;
        }
        if (_length == Capacity)
        {
            Array.Copy(_buffer, 1, _buffer, 0, Capacity - 1);
            _length--;
        }
        _buffer[_length++] = c;
        return Find(ExpandMode.RightAway);
    }

    /// <summary>Space, Tab or Enter was pressed. A delimiter snippet the buffer ends with matches; with none, the buffer resets.</summary>
    public SnippetMatch? Delimiter()
    {
        var match = Find(ExpandMode.AfterDelimiter);
        _length = 0;
        return match;
    }

    private SnippetMatch? Find(ExpandMode mode)
    {
        var typed = new ReadOnlySpan<char>(_buffer, 0, _length);
        foreach (var snippet in _snippets)
        {
            if (snippet.Expand != mode || snippet.Trigger.Length > typed.Length)
                continue;
            var tail = typed[^snippet.Trigger.Length..];
            if (tail.Equals(snippet.Trigger, snippet.IgnoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            {
                _length = 0;
                return new SnippetMatch(snippet, snippet.Trigger.Length);
            }
        }
        return null;
    }
}
