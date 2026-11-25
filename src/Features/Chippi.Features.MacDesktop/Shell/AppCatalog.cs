// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.IO;
using System.Runtime.InteropServices;
using Chippi.Core.Diagnostics;

namespace Chippi.Features.MacDesktop.Shell;

/// <summary>An app as the Start menu lists it: the name people know it by, its AppUserModelID and the program it starts.</summary>
public sealed record CatalogApp(string Name, string AppId, string Target, string Arguments = "");

/// <summary>
/// The names Windows itself shows for apps (the Start menu's list, <c>shell:AppsFolder</c>), so the menu bar and the dock
/// call an app what the user does: "Steam", not the "Steam Client WebHelper" its window's program describes itself as.
/// Finding a name is pure and tested; reading the list is Windows'.
/// </summary>
public sealed class AppCatalog
{
    private readonly Dictionary<string, string> _byId = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<CatalogApp>> _byTarget = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<CatalogApp>> _byFolder = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _shared;

    /// <param name="apps">The Start menu's apps.</param>
    /// <param name="sharedFolders">
    /// Folders many unrelated programs live in (Windows, System32, Program Files itself): a program there is never named
    /// after a neighbour.
    /// </param>
    public AppCatalog(IEnumerable<CatalogApp> apps, IEnumerable<string>? sharedFolders = null)
    {
        _shared = new HashSet<string>((sharedFolders ?? []).Select(Trim), StringComparer.OrdinalIgnoreCase);
        foreach (var app in apps)
        {
            if (app.Name.Length == 0)
                continue;
            if (app.AppId.Length > 0)
                _byId.TryAdd(app.AppId, app.Name);
            // An uninstaller's shortcut names nothing that has a window.
            if (app.Target.Length == 0 || !Path.IsPathFullyQualified(app.Target) || Path.GetFileName(app.Target).StartsWith("unins", StringComparison.OrdinalIgnoreCase))
                continue;
            Add(_byTarget, app.Target, app);
            if (Path.GetDirectoryName(app.Target) is { Length: > 0 } folder)
                Add(_byFolder, Trim(folder), app);
        }
    }

    public static AppCatalog Empty { get; } = new([]);

    public int Count => _byId.Count + _byTarget.Count;

    private static void Add(Dictionary<string, List<CatalogApp>> map, string key, CatalogApp app)
    {
        if (!map.TryGetValue(key, out var list))
            map[key] = list = [];
        list.Add(app);
    }

    private static string Trim(string folder) => folder.TrimEnd('\\', '/');

    /// <summary>
    /// The name for a window of <paramref name="program"/> whose taskbar identity is <paramref name="appId"/>; null when
    /// the Start menu doesn't know it. By its identity first (a Store app, a web app); then by its program; then, for a
    /// helper that draws the app's window (Steam's, a Squirrel app's versioned program), by the one app installed in a
    /// folder above it.
    /// </summary>
    public string? NameOf(string? program, string? appId)
    {
        if (!string.IsNullOrEmpty(appId) && _byId.TryGetValue(appId, out var known))
            return known;
        if (string.IsNullOrEmpty(program) || !Path.IsPathFullyQualified(program))
            return null;
        // By its program, when a shortcut starts just that. A shortcut with arguments starts something else through the
        // program (a folder through explorer.exe, a script through python.exe, a web app through the browser's proxy).
        if (_byTarget.TryGetValue(program, out var exact) && Pick(exact) is { } plain)
            return plain;
        for (string? folder = Path.GetDirectoryName(program); !string.IsNullOrEmpty(folder); folder = Path.GetDirectoryName(folder))
        {
            folder = Trim(folder);
            if (_shared.Contains(folder) || Path.GetPathRoot(folder) is { } root && Trim(root).Equals(folder, StringComparison.OrdinalIgnoreCase))
                return null;
            if (!_byFolder.ContainsKey(folder))
                continue;
            // One app installed under the folder: the window is its. Several (Git Bash, and Git GUI a folder down): it
            // could be any of them.
            string under = folder + Path.DirectorySeparatorChar;
            var names = _byTarget.Where(t => t.Key.StartsWith(under, StringComparison.OrdinalIgnoreCase)).SelectMany(t => t.Value)
                .Select(a => a.Name).Distinct(StringComparer.OrdinalIgnoreCase).Take(2).ToArray();
            return names.Length == 1 ? names[0] : null;
        }
        return null;
    }

    /// <summary>Of several shortcuts to one program, the plain one: without arguments, the shortest name. Null when none is plain.</summary>
    private static string? Pick(List<CatalogApp> apps) =>
        apps.Where(a => a.Arguments.Length == 0).OrderBy(a => a.Name.Length).ThenBy(a => a.Name, StringComparer.OrdinalIgnoreCase).FirstOrDefault()?.Name;

    /// <summary>The folders no program is named after a neighbour in.</summary>
    public static IReadOnlyList<string> SharedFolders()
    {
        var folders = new List<string>();
        foreach (var special in new[]
        {
            Environment.SpecialFolder.Windows, Environment.SpecialFolder.System, Environment.SpecialFolder.SystemX86,
            Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86, Environment.SpecialFolder.CommonProgramFiles,
            Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolder.ApplicationData, Environment.SpecialFolder.CommonApplicationData,
            Environment.SpecialFolder.UserProfile, Environment.SpecialFolder.Desktop, Environment.SpecialFolder.MyDocuments,
        })
            if (Environment.GetFolderPath(special) is { Length: > 0 } path)
                folders.Add(path);
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (local.Length > 0)
        {
            folders.Add(Path.Combine(local, "Programs"));
            folders.Add(Path.Combine(local, "Microsoft", "WindowsApps"));
        }
        return folders;
    }

    /// <summary>
    /// Reads the Start menu's apps. Slow (a second or so) and COM: call it on a thread of its own, never the UI thread.
    /// Names only stay in memory; nothing is logged but the count.
    /// </summary>
    public static AppCatalog Read()
    {
        object? shell = null, folder = null, items = null;
        var apps = new List<CatalogApp>();
        try
        {
            shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application", throwOnError: true)!);
            folder = ((dynamic)shell!).NameSpace("shell:AppsFolder");
            if (folder is null)
                return Empty;
            items = ((dynamic)folder).Items();
            int count = ((dynamic)items).Count;
            for (int i = 0; i < count; i++)
            {
                object item = ((dynamic)items).Item(i);
                try
                {
                    string Property(string name) => ((dynamic)item).ExtendedProperty(name) as string ?? "";
                    string name = ((dynamic)item).Name as string ?? "";
                    apps.Add(new CatalogApp(name.Trim(), Property("System.AppUserModel.ID"), Property("System.Link.TargetParsingPath"), Property("System.Link.Arguments")));
                }
                catch (Exception ex) when (ex is COMException or InvalidCastException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException) { }
                finally { Marshal.ReleaseComObject(item); }
            }
            return new AppCatalog(apps, SharedFolders());
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException or ArgumentException or TypeLoadException)
        {
            Log.Warn("Mac-style desktop: reading the Start menu's apps failed", ex);
            return Empty;
        }
        finally
        {
            if (items is not null) Marshal.ReleaseComObject(items);
            if (folder is not null) Marshal.ReleaseComObject(folder);
            if (shell is not null) Marshal.ReleaseComObject(shell);
        }
    }
}
