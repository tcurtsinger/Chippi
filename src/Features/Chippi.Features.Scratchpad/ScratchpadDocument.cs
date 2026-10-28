// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.IO;

namespace Chippi.Features.Scratchpad;

public sealed record ScratchpadNote(long Id, string Name, string Text);

/// <summary>The complete saved document; names and bodies are sealed together, never settings.</summary>
public sealed record ScratchpadDocument(long SelectedId, IReadOnlyList<ScratchpadNote> Notes)
{
    public const int MaximumNotes = 12;
    public const int MaximumNameLength = 40;

    public static ScratchpadDocument Empty() => new(1, Array.AsReadOnly(new[] { new ScratchpadNote(1, "Note 1", "") }));

    [System.Text.Json.Serialization.JsonIgnore]
    public ScratchpadNote Selected => Notes.Single(n => n.Id == SelectedId);

    public void Validate()
    {
        if (Notes is null || Notes.Count is < 1 or > MaximumNotes ||
            Notes.Any(n => n is null || n.Id <= 0 || string.IsNullOrWhiteSpace(n.Name) ||
                n.Name.Length > MaximumNameLength || n.Text is null) ||
            Notes.Select(n => n.Id).Distinct().Count() != Notes.Count || !Notes.Any(n => n.Id == SelectedId))
            throw new InvalidDataException("Invalid notes document");
    }

    public ScratchpadDocument Edit(string text) => Replace(Selected with { Text = text });

    public ScratchpadDocument Rename(string name)
    {
        var trimmed = name.Trim();
        if (trimmed.Length == 0)
            return this;
        if (trimmed.Length > MaximumNameLength)
            throw new ArgumentOutOfRangeException(nameof(name));
        return Replace(Selected with { Name = trimmed });
    }

    public ScratchpadDocument Select(long id) => Notes.Any(n => n.Id == id)
        ? this with { SelectedId = id } : throw new ArgumentOutOfRangeException(nameof(id));

    public ScratchpadDocument Add()
    {
        if (Notes.Count >= MaximumNotes)
            return this;
        var id = checked(Notes.Max(n => n.Id) + 1);
        return new(id, Array.AsReadOnly(Notes.Append(new ScratchpadNote(id, $"Note {id}", "")).ToArray()));
    }

    public ScratchpadDocument DeleteSelected()
    {
        if (Notes.Count == 1)
            return Empty();
        var index = Notes.ToList().FindIndex(n => n.Id == SelectedId);
        var remaining = Notes.Where(n => n.Id != SelectedId).ToArray();
        return new(remaining[Math.Min(index, remaining.Length - 1)].Id, Array.AsReadOnly(remaining));
    }

    private ScratchpadDocument Replace(ScratchpadNote note) =>
        this with { Notes = Array.AsReadOnly(Notes.Select(n => n.Id == note.Id ? note : n).ToArray()) };
}
