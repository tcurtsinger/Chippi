// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.IO;

namespace Chippi.Features.Scratchpad;

public sealed record ScratchpadNote(long Id, string Name, string Text);

public enum ScratchpadSurface { Widget, Settings }

/// <summary>The complete saved document; names and bodies are sealed together, never settings.</summary>
public sealed record ScratchpadDocument(long SelectedId, IReadOnlyList<ScratchpadNote> Notes)
{
    public const int MaximumNotes = 12;
    public const int MaximumNameLength = 40;

    // Old documents have one selection. Both views start there until one of them moves.
    public long SettingsSelectedId { get; init; } = SelectedId;
    public long LastIssuedId { get; init; }

    public static ScratchpadDocument Empty() => new(1, Array.AsReadOnly(new[] { new ScratchpadNote(1, "Note 1", "") }));

    [System.Text.Json.Serialization.JsonIgnore]
    public ScratchpadNote Selected => Current(ScratchpadSurface.Widget);

    public ScratchpadNote Current(ScratchpadSurface surface) =>
        Notes.Single(n => n.Id == CurrentId(surface));

    private long CurrentId(ScratchpadSurface surface) => surface switch
    {
        ScratchpadSurface.Widget => SelectedId,
        ScratchpadSurface.Settings => SettingsSelectedId,
        _ => throw new ArgumentOutOfRangeException(nameof(surface))
    };

    public void Validate()
    {
        if (Notes is null || Notes.Count is < 1 or > MaximumNotes ||
            Notes.Any(n => n is null || n.Id <= 0 || string.IsNullOrWhiteSpace(n.Name) ||
                n.Name.Length > MaximumNameLength || n.Text is null) ||
            Notes.Select(n => n.Id).Distinct().Count() != Notes.Count || !Notes.Any(n => n.Id == SelectedId) ||
            !Notes.Any(n => n.Id == SettingsSelectedId) || LastIssuedId < 0)
            throw new InvalidDataException("Invalid notes document");
    }

    public ScratchpadDocument Edit(string text) => Edit(SelectedId, text);

    public ScratchpadDocument Edit(long id, string text)
    {
        var note = Notes.FirstOrDefault(n => n.Id == id);
        return note is null || note.Text == text ? this : Replace(note with { Text = text });
    }

    public ScratchpadDocument Rename(string name) => Rename(SelectedId, name);

    public ScratchpadDocument Rename(long id, string name)
    {
        var note = Notes.FirstOrDefault(n => n.Id == id);
        var trimmed = name.Trim();
        if (note is null || trimmed.Length == 0 || note.Name == trimmed)
            return this;
        if (trimmed.Length > MaximumNameLength)
            throw new ArgumentOutOfRangeException(nameof(name));
        return Replace(note with { Name = trimmed });
    }

    public ScratchpadDocument Select(long id) => Notes.Any(n => n.Id == id)
        ? Select(ScratchpadSurface.Widget, id) : throw new ArgumentOutOfRangeException(nameof(id));

    public ScratchpadDocument Select(ScratchpadSurface surface, long id)
    {
        if (CurrentId(surface) == id || !Notes.Any(n => n.Id == id))
            return this;
        return surface == ScratchpadSurface.Widget ? this with { SelectedId = id } : this with { SettingsSelectedId = id };
    }

    public ScratchpadDocument Add() => Add(ScratchpadSurface.Widget);

    public ScratchpadDocument Add(ScratchpadSurface surface)
    {
        if (Notes.Count >= MaximumNotes)
            return this;
        var id = checked(HighestId() + 1);
        var number = Notes.Count + 1;
        while (Notes.Any(n => n.Name == $"Note {number}"))
            number++;
        var added = this with
        {
            LastIssuedId = id,
            Notes = Array.AsReadOnly(Notes.Append(new ScratchpadNote(id, $"Note {number}", "")).ToArray())
        };
        return added.Select(surface, id);
    }

    public ScratchpadDocument DeleteSelected() => Delete(SelectedId);

    public ScratchpadDocument Delete(long id)
    {
        var index = Notes.ToList().FindIndex(n => n.Id == id);
        if (index < 0)
            return this;
        var highestId = HighestId();
        if (Notes.Count == 1)
        {
            var replacement = checked(highestId + 1);
            return new(replacement, Array.AsReadOnly(new[] { new ScratchpadNote(replacement, "Note 1", "") }))
            {
                LastIssuedId = replacement
            };
        }
        var remaining = Notes.Where(n => n.Id != id).ToArray();
        var neighbor = remaining[Math.Min(index, remaining.Length - 1)].Id;
        return this with
        {
            SelectedId = SelectedId == id ? neighbor : SelectedId,
            SettingsSelectedId = SettingsSelectedId == id ? neighbor : SettingsSelectedId,
            LastIssuedId = highestId,
            Notes = Array.AsReadOnly(remaining)
        };
    }

    private long HighestId() => Math.Max(LastIssuedId, Notes.Max(n => n.Id));

    private ScratchpadDocument Replace(ScratchpadNote note) =>
        this with { Notes = Array.AsReadOnly(Notes.Select(n => n.Id == note.Id ? note : n).ToArray()) };
}
