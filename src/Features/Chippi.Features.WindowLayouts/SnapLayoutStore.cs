// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.Core.Modules;

namespace Chippi.Features.WindowLayouts;

public sealed record WindowLayoutsSettings
{
    public List<SavedArrangement> Arrangements { get; init; } = [];
    public bool ShiftDragEnabled { get; init; }
    public int WindowGap { get; init; } = 8;
    public int ScreenEdgeGap { get; init; }
    public List<SnapLayout> Layouts { get; init; } = [.. SnapLayouts.Defaults()];
    public Dictionary<string, string> DisplayLayouts { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> ExcludedApps { get; init; } = [];
}

/// <summary>Called on the UI thread, like the module settings store. Save succeeds before publishing a change.</summary>
public sealed class SnapLayoutStore(IModuleSettings settings)
{
    public WindowLayoutsSettings Settings { get; private set; } = Normalize(settings.Get<WindowLayoutsSettings>());
    public event Action? Changed;

    public bool NameTaken(string name, string? exceptId = null) =>
        Settings.Layouts.Any(l => l.Id != exceptId && string.Equals(l.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));

    public bool ArrangementNameTaken(string name, string? exceptId = null) => Settings.Arrangements.Any(a =>
        a.Id != exceptId && string.Equals(a.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));

    public void SaveArrangement(SavedArrangement arrangement)
    {
        if (!arrangement.IsValid || ArrangementNameTaken(arrangement.Name, arrangement.Id))
            throw new ArgumentException("Invalid arrangement", nameof(arrangement));
        var arrangements = Settings.Arrangements.ToList();
        int index = arrangements.FindIndex(a => a.Id == arrangement.Id);
        var saved = arrangement with { Name = arrangement.Name.Trim(), Rows = arrangement.Rows.ToList() };
        if (index < 0) arrangements.Add(saved);
        else arrangements[index] = saved;
        Commit(Settings with { Arrangements = arrangements });
    }

    public void DeleteArrangement(string id) =>
        Commit(Settings with { Arrangements = Settings.Arrangements.Where(a => a.Id != id).ToList() });

    public void Save(SnapLayout layout)
    {
        if (string.IsNullOrWhiteSpace(layout.Id) || string.IsNullOrWhiteSpace(layout.Name) || layout.Name.Trim().Length > 80
            || layout.Root is null || !layout.Root.IsValid() || NameTaken(layout.Name, layout.Id))
            throw new ArgumentException("Invalid layout", nameof(layout));
        int index = Settings.Layouts.FindIndex(l => l.Id == layout.Id);
        if (index >= 0 && Settings.Layouts[index].BuiltIn)
            throw new InvalidOperationException("Built-in layouts cannot be edited");
        var layouts = new List<SnapLayout>(Settings.Layouts);
        layout = layout with { Name = layout.Name.Trim(), BuiltIn = false };
        if (index >= 0) layouts[index] = layout;
        else layouts.Add(layout);
        Commit(Settings with { Layouts = layouts });
    }

    public SnapLayout Duplicate(string id)
    {
        var source = Find(id);
        int suffix = 2;
        string name = CopyName(source.Name, suffix);
        while (NameTaken(name))
            name = CopyName(source.Name, ++suffix);
        var copy = source with { Id = Guid.NewGuid().ToString("N"), Name = name, BuiltIn = false };
        Save(copy);
        return copy;
    }

    private static string CopyName(string source, int suffix)
    {
        string tail = " " + suffix;
        return source[..Math.Min(source.Length, 80 - tail.Length)].TrimEnd() + tail;
    }

    public void Delete(string id)
    {
        if (Find(id).BuiltIn)
            throw new InvalidOperationException("Built-in layouts cannot be deleted");
        var displays = new Dictionary<string, string>(Settings.DisplayLayouts, StringComparer.OrdinalIgnoreCase);
        foreach (string display in displays.Where(p => p.Value == id).Select(p => p.Key).ToArray())
            displays.Remove(display);
        Commit(Settings with { Layouts = Settings.Layouts.Where(l => l.Id != id).ToList(), DisplayLayouts = displays });
    }

    public void Move(string id, int index)
    {
        var layout = Find(id);
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        if (index >= Settings.Layouts.Count)
            throw new ArgumentOutOfRangeException(nameof(index));
        var layouts = new List<SnapLayout>(Settings.Layouts);
        layouts.Remove(layout);
        layouts.Insert(index, layout);
        Commit(Settings with { Layouts = layouts });
    }

    public void Configure(bool enabled, int windowGap, int screenEdgeGap, IEnumerable<string> excludedApps)
    {
        if (windowGap is < 0 or > 24 || screenEdgeGap is < 0 or > 24)
            throw new ArgumentOutOfRangeException(nameof(windowGap));
        Commit(Settings with { ShiftDragEnabled = enabled, WindowGap = windowGap, ScreenEdgeGap = screenEdgeGap,
            ExcludedApps = excludedApps.Where(s => !string.IsNullOrWhiteSpace(s)).Distinct(StringComparer.OrdinalIgnoreCase).ToList() });
    }

    public void AssignDisplay(string displayId, string layoutId)
    {
        _ = Find(layoutId);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayId);
        var displays = new Dictionary<string, string>(Settings.DisplayLayouts, StringComparer.OrdinalIgnoreCase) { [displayId] = layoutId };
        Commit(Settings with { DisplayLayouts = displays });
    }

    public SnapLayout ForDisplay(string displayId) =>
        Settings.DisplayLayouts.TryGetValue(displayId, out string? id) ? Settings.Layouts.FirstOrDefault(l => l.Id == id) ?? Find("halves") : Find("halves");

    private SnapLayout Find(string id) => Settings.Layouts.FirstOrDefault(l => l.Id == id) ?? throw new ArgumentException("Layout not found", nameof(id));

    private void Commit(WindowLayoutsSettings next)
    {
        settings.Save(next);
        Settings = next;
        Changed?.Invoke();
    }

    private static WindowLayoutsSettings Normalize(WindowLayoutsSettings saved)
    {
        var defaults = SnapLayouts.Defaults();
        var layouts = new List<SnapLayout>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var names = new HashSet<string>(defaults.Select(l => l.Name), StringComparer.OrdinalIgnoreCase);
        foreach (var layout in saved.Layouts ?? [])
        {
            if (layout is null || string.IsNullOrWhiteSpace(layout.Id) || !ids.Add(layout.Id))
                continue;
            var builtIn = defaults.FirstOrDefault(l => l.Id == layout.Id);
            if (builtIn is not null) layouts.Add(builtIn);
            else if (!string.IsNullOrWhiteSpace(layout.Name) && layout.Name.Trim().Length <= 80
                && layout.Root?.IsValid() == true && names.Add(layout.Name.Trim()))
                layouts.Add(layout with { Name = layout.Name.Trim(), BuiltIn = false });
        }
        foreach (var layout in defaults)
            if (!layouts.Any(l => l.Id == layout.Id)) layouts.Add(layout);
        var arrangementIds = new HashSet<string>(StringComparer.Ordinal);
        var arrangementNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var arrangements = (saved.Arrangements ?? []).Where(a => a is not null && a.IsValid
            && arrangementIds.Add(a.Id) && arrangementNames.Add(a.Name.Trim()))
            .Select(a => a with { Name = a.Name.Trim(), Rows = a.Rows.ToList() }).ToList();
        return saved with { Arrangements = arrangements, Layouts = layouts, WindowGap = Math.Clamp(saved.WindowGap, 0, 24),
            ScreenEdgeGap = Math.Clamp(saved.ScreenEdgeGap, 0, 24),
            DisplayLayouts = new(saved.DisplayLayouts ?? [], StringComparer.OrdinalIgnoreCase),
            ExcludedApps = (saved.ExcludedApps ?? []).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct(StringComparer.OrdinalIgnoreCase).ToList() };
    }
}

