// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Text;

namespace Chippi.Features.ScreenText;

/// <summary>Line breaks (docs/phase-7/text-and-qr.md, Recognition): keep the lines as recognized, or join them into paragraphs.</summary>
public static class TextJoin
{
    /// <summary>
    /// The text, and where each source line starts in it. Joining merges a line into the previous one unless the previous
    /// ends a sentence or the next starts with a bullet, a number or an indent, and never across a blank gap.
    /// </summary>
    public static (string Text, int[] Starts) Apply(IReadOnlyList<string> lines, bool join)
    {
        var text = new StringBuilder();
        var starts = new int[lines.Count];
        string? previous = null;
        for (int i = 0; i < lines.Count; i++)
        {
            string line = lines[i];
            bool merge = join && previous is not null && !IsBlank(previous) && !IsBlank(line) && !EndsSentence(previous) && !StartsItem(line);
            if (i > 0)
                text.Append(merge ? " " : "\n");
            starts[i] = text.Length;
            text.Append(merge ? line.TrimStart() : line);
            previous = line;
        }
        return (text.ToString(), starts);
    }

    /// <summary>Whitespace-separated tokens.</summary>
    public static int WordCount(string text) => text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;

    private static bool IsBlank(string line) => string.IsNullOrWhiteSpace(line);

    private static bool EndsSentence(string line) => line.TrimEnd() is { Length: > 0 } t && t[^1] is '.' or '!' or '?' or ':' or '…' or '。' or '？' or '！';

    private static bool StartsItem(string line)
    {
        if (line.Length == 0)
            return false;
        if (char.IsWhiteSpace(line[0]))
            return true; // an indent
        string t = line.TrimStart();
        if (t[0] is '•' or '·' or '-' or '*' or '–' or '—' or '▪' or '○' or '●' && (t.Length == 1 || char.IsWhiteSpace(t[1])))
            return true;
        int digits = 0;
        while (digits < t.Length && char.IsDigit(t[digits]))
            digits++;
        return digits is > 0 and <= 3 && digits < t.Length && t[digits] is '.' or ')' && digits + 1 < t.Length && char.IsWhiteSpace(t[digits + 1]);
    }
}
