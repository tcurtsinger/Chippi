// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

namespace Chippi.Features.TextSnippets;

/// <summary>The key that followed a trigger, typed again after the text.</summary>
public enum Delimiter { Space, Tab, Enter }

/// <summary>One key event: a virtual key, or a UTF-16 unit typed as itself (<c>KEYEVENTF_UNICODE</c>).</summary>
internal readonly record struct Stroke(ushort Vk, char Unit, bool Up)
{
    public bool IsUnicode => Vk == 0;
}

/// <summary>Sends an expansion: deletes what was typed, then types the text. Every event is tagged as Chippi's, so the hub never sees it.</summary>
internal interface ISnippetSender
{
    Task SendAsync(int backspaces, string text, Delimiter? after, CancellationToken ct = default);
}

/// <summary>The events of an expansion, in batches (docs/phase-6/text-snippets.md, Inserting).</summary>
internal static class Keystrokes
{
    public const ushort Back = 0x08, TabKey = 0x09, Enter = 0x0D, Shift = 0x10;

    /// <summary>A batch holds at most this many characters: one big batch drops characters in Remote Desktop and some terminals.</summary>
    public const int ChunkChars = 48;

    public static List<Stroke[]> Plan(int backspaces, string text, Delimiter? after)
    {
        var batches = new List<Stroke[]>();
        if (backspaces > 0)
        {
            var back = new List<Stroke>();
            for (int i = 0; i < backspaces; i++)
            {
                back.Add(new Stroke(Back, '\0', false));
                back.Add(new Stroke(Back, '\0', true));
            }
            batches.Add([.. back]);
        }
        text = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        var chunk = new List<Stroke>();
        int chars = 0;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '\n')
            {
                // Shift+Enter: a bare Enter sends a chat message.
                chunk.AddRange([new(Shift, '\0', false), new(Enter, '\0', false), new(Enter, '\0', true), new(Shift, '\0', true)]);
            }
            else if (c == '\t')
                chunk.AddRange([new(TabKey, '\0', false), new(TabKey, '\0', true)]);
            else
                chunk.AddRange([new(0, c, false), new(0, c, true)]);
            chars++;
            bool splitsPair = char.IsHighSurrogate(c) && i + 1 < text.Length;
            if (chars >= ChunkChars && !splitsPair)
            {
                batches.Add([.. chunk]);
                chunk.Clear();
                chars = 0;
            }
        }
        if (after is { } key)
        {
            Stroke[] strokes = key switch
            {
                Delimiter.Tab => [new(TabKey, '\0', false), new(TabKey, '\0', true)],
                Delimiter.Enter => [new(Enter, '\0', false), new(Enter, '\0', true)],
                _ => [new(0, ' ', false), new(0, ' ', true)],
            };
            chunk.AddRange(strokes);
        }
        if (chunk.Count > 0)
            batches.Add([.. chunk]);
        return batches;
    }
}
