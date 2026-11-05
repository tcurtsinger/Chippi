// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Globalization;
using System.Text.RegularExpressions;

namespace Chippi.Features.TextSnippets;

/// <summary>
/// Fills a snippet's variables (docs/phase-6/text-snippets.md, Variables): <c>{{date}}</c>, <c>{{time}}</c>,
/// <c>{{datetime}}</c> with a .NET format after a colon and an IANA time zone (<c>{{date-tz(America/New_York):yyyy-MM-dd}}</c>),
/// and <c>{{clipboard}}</c>. A tag it doesn't know stays as typed, so a typo shows.
/// </summary>
internal static partial class SnippetRenderer
{
    public static readonly TimeSpan ClipboardTimeout = TimeSpan.FromSeconds(1);

    [GeneratedRegex(@"\{\{(?:(?<kind>date|time|datetime)(?:-tz\((?<zone>[^)]+)\))?(?::(?<format>[^}]*))?|(?<clip>clipboard))\}\}")]
    private static partial Regex Tag();

    /// <summary>Whether the text asks for the clipboard, so a snippet without it never touches it.</summary>
    public static bool UsesClipboard(string text) => text.Contains("{{clipboard}}", StringComparison.Ordinal);

    /// <param name="clipboard">Reads the copy's text; asked only when the text uses it, and given <paramref name="clipboardTimeout"/>.</param>
    public static async Task<string> RenderAsync(string text, DateTimeOffset now, Func<Task<string?>>? clipboard = null, TimeSpan? clipboardTimeout = null)
    {
        string? copy = null;
        if (UsesClipboard(text) && clipboard is not null)
        {
            var read = Task.Run(clipboard);
            if (await Task.WhenAny(read, Task.Delay(clipboardTimeout ?? ClipboardTimeout)) == read)
            {
                try { copy = await read; }
                catch (Exception ex) when (ex is not OutOfMemoryException) { }
            }
        }
        return Tag().Replace(text, m =>
        {
            if (m.Groups["clip"].Success)
                return copy ?? "";
            var moment = now;
            if (m.Groups["zone"].Success)
            {
                try { moment = TimeZoneInfo.ConvertTime(now, TimeZoneInfo.FindSystemTimeZoneById(m.Groups["zone"].Value)); }
                catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException) { return m.Value; }
            }
            string format = m.Groups["format"].Success ? m.Groups["format"].Value : m.Groups["kind"].Value switch
            {
                "date" => "d",
                "time" => "t",
                _ => "g",
            };
            try { return moment.ToString(format, CultureInfo.CurrentCulture); }
            catch (FormatException) { return m.Value; }
        });
    }
}
