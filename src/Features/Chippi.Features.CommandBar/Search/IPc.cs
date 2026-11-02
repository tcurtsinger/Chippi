// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows.Media;

namespace Chippi.Features.CommandBar.Search;

/// <summary>An app of the Start menu: its name, and the parsing name that opens it from <c>shell:AppsFolder</c>.</summary>
public sealed record StartApp(string Name, string ParsingName);

/// <summary>A window Alt+Tab lists: its title and the app it belongs to ("Chrome").</summary>
public sealed record OpenWindow(nint Handle, string Title, string App);

/// <summary>A file or folder Windows Search found: its full path, its name, and the folder it's in ("Downloads").</summary>
public sealed record FoundFile(string Path, string Name, string Folder, bool IsFolder);

/// <summary>
/// The PC's side of the Command bar: what it finds there and how it opens it. Nothing here runs in tests or dev runs
/// (docs/phase-5.5-handoff.md §3): tests pass a fake.
/// </summary>
public interface IPc : IDisposable
{
    /// <summary>The Start menu's apps as last read, by name; empty until the first read is done.</summary>
    IReadOnlyList<StartApp> Apps { get; }

    /// <summary>Reads the Start menu again off the UI thread, and raises <see cref="AppsChanged"/> on any thread when done.</summary>
    void RefreshApps();

    event Action? AppsChanged;

    /// <summary>Every window Alt+Tab lists, front first, leaving out Chippi's own.</summary>
    IReadOnlyList<OpenWindow> Windows();

    /// <summary>
    /// Windows Search's files and folders whose names have words starting with <paramref name="text"/>'s, best first, at
    /// most <paramref name="count"/>; empty when Windows Search can't answer (its service off, nothing indexed).
    /// </summary>
    Task<IReadOnlyList<FoundFile>> FindFilesAsync(string text, int count, CancellationToken ct);

    /// <summary>Opens a file, folder, app, Settings page or web address as Explorer would. Throws when Windows refuses.</summary>
    void Open(string target);

    /// <summary>Brings <paramref name="window"/> to the front, restored if it was minimized. Throws when it's gone.</summary>
    void Switch(nint window);

    /// <summary>An app's icon at <paramref name="pixels"/> square, frozen, or null; read off the UI thread and kept.</summary>
    Task<ImageSource?> AppIconAsync(StartApp app, int pixels);
}
