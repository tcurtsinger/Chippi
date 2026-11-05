// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

namespace Chippi.Features.TextSnippets;

/// <summary>When a trigger expands: as its last character is typed, or when space, Tab or Enter follows it.</summary>
public enum ExpandMode { RightAway, AfterDelimiter }

/// <summary>One snippet (docs/phase-6/text-snippets.md). Unknown fields in settings read as these defaults.</summary>
public sealed record Snippet
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");

    public string Name { get; init; } = "";

    public string Trigger { get; init; } = "";

    public string Text { get; init; } = "";

    public ExpandMode Expand { get; init; } = ExpandMode.AfterDelimiter;

    public bool Enabled { get; init; } = true;

    public bool IgnoreCase { get; init; }

    /// <summary>Plain text; a folder exists only because a snippet names it.</summary>
    public string Folder { get; init; } = "";
}

/// <summary>The module's settings. Expansion while typing starts off, even once installed (Travis, task 226).</summary>
public sealed record TextSnippetsSettings
{
    public bool ExpandWhileTyping { get; init; }

    public Snippet[] Snippets { get; init; } = [];

    /// <summary>Apps nothing expands in, by exe path (matched by file name).</summary>
    public string[] IgnoredApps { get; init; } = [];

    public bool SoundOnExpand { get; init; }

    /// <summary>A Windows system sound's name ("Asterisk"); null for the default.</summary>
    public string? Sound { get; init; }
}

/// <summary>The trigger rules the editor shows as errors and warnings.</summary>
public static class SnippetRules
{
    public const int MinTrigger = 2, MaxTrigger = 40;
    public const string TooShort = "The trigger needs at least 2 characters.";
    public const string Duplicate = "Another snippet already uses this trigger.";
    public const string StartsAnother = "This trigger starts another one.";
    public const string NoSpaces = "The trigger can't hold a space or tab.";
    public const string TooLong = "The trigger can hold at most 40 characters.";

    /// <summary>The error for <paramref name="trigger"/> on the snippet <paramref name="selfId"/>, or null when it can be saved.</summary>
    public static string? Check(string trigger, IEnumerable<Snippet> all, string? selfId, bool ignoreCase = false)
    {
        if (trigger.Length < MinTrigger)
            return TooShort;
        if (trigger.Length > MaxTrigger)
            return TooLong;
        if (trigger.Any(c => char.IsWhiteSpace(c) || char.IsControl(c)))
            return NoSpaces;
        return all.Any(s => s.Id != selfId && (s.Trigger == trigger
            || ((ignoreCase || s.IgnoreCase) && string.Equals(s.Trigger, trigger, StringComparison.OrdinalIgnoreCase))))
            ? Duplicate : null;
    }

    /// <summary>A warning when one trigger starts the other, so the shorter can fire first: ";ad" and ";add".</summary>
    public static string? Warn(string trigger, IEnumerable<Snippet> all, string? selfId) =>
        trigger.Length >= MinTrigger && all.Any(s => s.Id != selfId && s.Trigger.Length > 0 && s.Trigger != trigger
            && (s.Trigger.StartsWith(trigger, StringComparison.OrdinalIgnoreCase) || trigger.StartsWith(s.Trigger, StringComparison.OrdinalIgnoreCase)))
            ? StartsAnother : null;
}
