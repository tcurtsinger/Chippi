// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using Chippi.Core.Actions;
using Chippi.Core.Diagnostics;
using Chippi.Core.Input;
using Chippi.Features.MacDesktop.MenuBar;
using Chippi.Features.MacDesktop.Shell;
using Chippi.UI.Actions;
using Chippi.UI.Controls;
using Chippi.UI.Displays;

namespace Chippi.Features.MacDesktop.Dock;

/// <summary>
/// The dock on the main display (docs/mac-style/brief.md, Dock): Start, the pins, the running apps that aren't pinned, a
/// divider, Downloads and the Recycle Bin. Follows the windows with WinEvent hooks, read again at most every 150 ms. Every
/// launch, window change and key goes through the shared action PC.
/// </summary>
internal sealed class DockController : IDisposable
{
    private readonly MacShell _shell;
    private readonly MacDesktopModule _module;
    private readonly Dispatcher _dispatcher;
    private readonly WindowsWindowList _list = new();
    private readonly DispatcherTimer _settle;
    private readonly Native.WinEventProc _proc;
    private readonly List<nint> _hooks = [];
    private readonly Dictionary<nint, string> _appIds = [];
    private readonly Dictionary<nint, int> _appIdMisses = [];
    private readonly HashSet<nint> _known = [];
    private readonly List<string> _order = [];
    private DockWindow? _window;
    private TaskbarBadgeReader? _badges;
    private IReadOnlyList<TaskbarButton> _buttons = [];
    private IInputRegistration? _keys;
    private bool _disposed;

    public IReadOnlyList<DockApp> Apps { get; private set; } = [];

    public MacDesktopSettings Settings => _module.Settings;

    public MacShell Shell => _shell;

    public static string Downloads => KnownFolder(new Guid("374DE290-123F-4565-9164-39C4925E467B"))
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");

    public const string RecycleBin = "::{645FF040-5081-101B-9F08-00AA002F954E}";

    public DockController(MacShell shell, MacDesktopModule module)
    {
        _shell = shell;
        _module = module;
        _dispatcher = Dispatcher.CurrentDispatcher;
        _settle = new DispatcherTimer(TimeSpan.FromMilliseconds(150), DispatcherPriority.Background, (_, _) => { _settle!.Stop(); ReadWindows(); }, _dispatcher);
        _settle.Stop();
        _proc = (_, type, window, objectId, _, _, _) =>
        {
            if (objectId == 0 && window != 0 && !_settle.IsEnabled && Matters(type, window))
                _settle.Start();
        };
    }

    /// <summary>
    /// Whether an event can change the dock. The hooks hear every window of every app: tooltips, menus, child windows and
    /// each change of a title. Only an app's own window appearing, leaving or coming forward moves anything here.
    /// </summary>
    private bool Matters(uint type, nint window)
    {
        if (type is Native.EventSystemForeground or Native.EventSystemMinimizeStart or Native.EventSystemMinimizeEnd)
            return true;
        if (_known.Contains(window))
            return type != Native.EventObjectNameChange; // a title is read when a menu or preview opens, not kept
        if (type is Native.EventObjectDestroy or Native.EventObjectHide)
            return false; // not one the dock shows
        // Shown or named: a window the dock doesn't have yet (one with no title isn't listed until it gets one).
        return Native.GetAncestor(window, 2 /* GA_ROOT */) == window
            && (Native.GetWindowLongPtr(window, Native.GwlExStyle) & (nint)Native.WsExToolWindow) == 0;
    }

    public void Start()
    {
        AppNames.CatalogRead += OnCatalogRead;
        AppNames.ReadCatalog();
        if (Settings.Pins is null)
        {
            // The first start brings the taskbar's pins over.
            var imported = DockPins.FromTaskbar();
            Save(Settings with { Pins = imported });
            Log.Info($"Mac-style desktop: brought {imported.Count} pins over from the taskbar");
        }
        foreach (var (min, max) in new[] { (Native.EventSystemForeground, Native.EventSystemForeground), (Native.EventSystemMinimizeStart, Native.EventSystemMinimizeEnd),
            (Native.EventObjectDestroy, Native.EventObjectHide), (Native.EventObjectNameChange, Native.EventObjectNameChange) })
        {
            nint hook = Native.SetWinEventHook(min, max, 0, _proc, 0, 0, Native.WinEventOutOfContext | Native.WinEventSkipOwnProcess);
            if (hook != 0)
                _hooks.Add(hook);
        }
        _window = new DockWindow(this);
        _window.Show();
        ReadWindows();
        _badges = new TaskbarBadgeReader(buttons => _dispatcher.BeginInvoke(() =>
        {
            if (_disposed)
                return;
            _buttons = buttons;
            _window?.RefreshBadges();
        }));
        _badges.Start();
        if (_shell.Context.Input is { } input)
        {
            var keys = new DockKeys(index => _dispatcher.BeginInvoke(() => OpenNumber(index)));
            try { _keys = keys.Registration = input.Add(keys); }
            catch (Exception ex)
            {
                // Windows refused the hook: the dock works, only its keys don't.
                Log.Warn("Mac-style desktop: Win+number for the dock failed", ex);
            }
        }
    }

    /// <summary>Win+1 to Win+9: the dock's apps in order, as a click opens them.</summary>
    private void OpenNumber(int index)
    {
        if (_disposed || index < 0 || index >= Apps.Count)
            return;
        Click(Apps[index]);
    }

    public void SettingsChanged()
    {
        ReadWindows();
        _window?.SettingsChanged();
    }

    public void RefreshDisplays() => _window?.Place();

    /// <summary>A running app that isn't pinned is called what the Start menu calls it, as on the menu bar.</summary>
    private static string? Named(ListedWindow window, string? appId) =>
        window.Executable.Length > 0 ? AppNames.Name(window.Executable, appId) : null;

    private void OnCatalogRead() => _dispatcher.BeginInvoke(ReadWindows);

    private void ReadWindows()
    {
        if (_disposed)
            return;
        var windows = _list.Read();
        var pins = (Settings.Pins ?? []).Select(DockPins.Read).ToArray();
        _known.Clear();
        foreach (var window in windows)
            _known.Add(window.Handle);
        foreach (var gone in _appIds.Keys.Where(h => !_known.Contains(h)).ToArray())
            _appIds.Remove(gone);
        foreach (var gone in _appIdMisses.Keys.Where(h => !_known.Contains(h)).ToArray())
            _appIdMisses.Remove(gone);
        Apps = DockApps.Arrange(DockApps.Compose(pins, windows, AppId, Named), _order);
        _window?.SetApps(Apps);
    }

    /// <summary>
    /// A window's AppUserModelID, kept once read. One that has none yet is read again the next few times (apps set it
    /// late), then taken to have none, so a desktop app's windows aren't asked on every read.
    /// </summary>
    private string? AppId(ListedWindow window)
    {
        if (_appIds.TryGetValue(window.Handle, out var known))
            return known;
        int misses = _appIdMisses.GetValueOrDefault(window.Handle);
        if (misses >= 20)
            return null;
        var id = ShellProperties.AppId(window);
        if (id is not null)
            _appIds[window.Handle] = id;
        else
            _appIdMisses[window.Handle] = misses + 1;
        return id;
    }

    /// <summary>
    /// The red badge on a running app's icon, as its hidden taskbar button has it: null for none, "" for a dot, else the
    /// count (<see cref="DockBadges.Read"/>).
    /// </summary>
    public string? BadgeOf(DockApp app) =>
        DockBadges.Find(app, _buttons, w => _appIds.GetValueOrDefault(w.Handle), FolderOf) is { } button
            ? DockBadges.Read(button.Help, app.Windows.Select(w => w.Title))
            : null;

    private readonly Dictionary<Guid, string?> _folders = [];

    private string? FolderOf(Guid id)
    {
        if (!_folders.TryGetValue(id, out var path))
            _folders[id] = path = KnownFolder(id);
        return path;
    }

    private void Save(MacDesktopSettings settings)
    {
        try { _ = _module.ApplyAsync(settings); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            Log.Warn("Mac-style desktop: saving the dock failed", ex);
        }
    }

    // ----- Clicks -----

    /// <summary>Not running, it opens; running, it comes to the front; its window in front minimizes, as on the taskbar.</summary>
    public void Click(DockApp app)
    {
        var (action, window) = DockApps.OnClick(app, Native.GetForegroundWindow());
        switch (action)
        {
            case DockClick.Minimize:
                _shell.Window(window, WindowAction.Minimize);
                break;
            case DockClick.Show:
                Activate(window);
                break;
            // Its windows may all be on another desktop: Windows goes there rather than opening a second copy.
            case DockClick.Launch when Everywhere(app) is { Running: true } away:
                Activate(away.Windows[0].Handle);
                break;
            default:
                Launch(app.Launch);
                break;
        }
    }

    /// <summary>
    /// Brings the window to the front, un-minimized if it was. The dock never takes the foreground, so Windows may refuse it
    /// the right to give it away; ForegroundRight earns it.
    /// </summary>
    public void Activate(nint window)
    {
        if (!ForegroundRight.Show(window))
            Log.Warn("Mac-style desktop: a window didn't come to the front");
    }

    /// <summary>The app with its windows on every desktop, matched as the dock matches them; null when it has none.</summary>
    private DockApp? Everywhere(DockApp app)
    {
        var pins = (Settings.Pins ?? []).Select(DockPins.Read).ToArray();
        return DockApps.Compose(pins, _list.Read(includeOtherDesktops: true), AppId, Named).FirstOrDefault(a => a.Key == app.Key);
    }

    public void Launch(string target, string arguments = "")
    {
        if (_shell.Pc is not { } pc)
            return;
        try
        {
            if (target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && Path.IsPathFullyQualified(target))
                pc.LaunchExecutable(new ExecutableLaunch(target, arguments));
            else if (target.StartsWith(@"shell:AppsFolder\", StringComparison.OrdinalIgnoreCase))
                pc.LaunchRegisteredApp(target[@"shell:AppsFolder\".Length..]);
            else
                pc.Open(target);
        }
        catch (ActionFailure ex)
        {
            Log.Warn($"Mac-style desktop: opening {Path.GetFileName(target)} failed", ex);
            _window?.ShowStatus("Open failed");
        }
    }

    public void OpenStart() => _ = _shell.TapAsync(0x5B);

    // ----- Menus -----

    public IReadOnlyList<BarMenuItem> Menu(DockApp app)
    {
        var items = new List<BarMenuItem>();
        if (app.Windows.Count > 1)
        {
            foreach (var window in app.Windows.Take(12))
                items.Add(new BarMenuItem(window.Title.Length > 0 ? window.Title : app.Name) { Run = () => Activate(window.Handle) });
            items.Add(BarMenuItem.Separator);
        }
        items.Add(app.Running
            ? new BarMenuItem("New window") { Run = () => Launch(app.Launch) }
            : new BarMenuItem("Open") { Run = () => Launch(app.Launch) });
        items.Add(app.Pinned
            ? new BarMenuItem("Remove from Dock") { Run = () => Unpin(app.Launch, offerUndo: true) }
            : new BarMenuItem("Keep in Dock") { Run = () => Pin(app.Launch, null) });
        if (File.Exists(app.Path))
            items.Add(new BarMenuItem("Show in Explorer") { Run = () => ShowInExplorer(app.Path) });
        if (app.Running)
        {
            items.Add(BarMenuItem.Separator);
            items.Add(new BarMenuItem(app.Windows.Count == 1 ? "Close window" : "Close windows") { Run = () => CloseAll(app) });
            items.Add(new BarMenuItem("Quit") { Run = () => Quit(app) });
        }
        return items;
    }

    /// <summary>
    /// The stack of newest downloads, each with its file's icon (read ahead while the pointer is on the tile) and its age
    /// ("Now", "5m", "3h", "2d").
    /// </summary>
    public IReadOnlyList<BarMenuItem> DownloadsMenu()
    {
        var folder = Downloads;
        var now = DateTime.UtcNow;
        var items = DockApps.Newest(folder, 12).Select(file => new BarMenuItem(file.Name)
        {
            Run = () => Open(file.FullName),
            Hint = DockApps.Age(now - file.LastWriteTimeUtc),
            Icon = AppIcon.LoadAsync(file.FullName, 32) is { IsCompletedSuccessfully: true } icon ? icon.Result : null,
        }).ToList<BarMenuItem>();
        if (items.Count == 0)
            items.Add(new BarMenuItem("Empty") { Enabled = false });
        items.Add(BarMenuItem.Separator);
        items.Add(new BarMenuItem("Open Downloads") { Run = () => Open(folder) });
        return items;
    }

    /// <summary>Starts reading the newest downloads' icons, so the stack opens with them.</summary>
    public static void PrefetchDownloadIcons()
    {
        foreach (var file in DockApps.Newest(Downloads, 12))
            _ = AppIcon.LoadAsync(file.FullName, 32);
    }

    /// <summary>Whether the Recycle Bin holds anything; null when Windows can't say.</summary>
    public static bool? RecycleBinFull()
    {
        var info = new RecycleBinInfo { Size = Marshal.SizeOf<RecycleBinInfo>() };
        return SHQueryRecycleBin(null, ref info) == 0 ? info.Items > 0 : null;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RecycleBinInfo
    {
        public int Size;
        public long Bytes;
        public long Items;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "SHQueryRecycleBinW")] private static extern int SHQueryRecycleBin(string? root, ref RecycleBinInfo info);

    public IReadOnlyList<BarMenuItem> RecycleBinMenu() =>
    [
        new BarMenuItem("Open") { Run = () => Open("shell:RecycleBinFolder") },
        new BarMenuItem("Empty Recycle Bin") { Run = () => _window?.ConfirmEmptyRecycleBin() },
    ];

    public IReadOnlyList<BarMenuItem> StartMenu() =>
    [
        new BarMenuItem("Start") { Run = OpenStart },
        new BarMenuItem("Settings") { Run = () => Open("ms-settings:") },
        new BarMenuItem("Chippi settings") { Run = () => _shell.Context.OpenSettings($"module:{MacDesktopModule.Id}") },
    ];

    public void Open(string target)
    {
        try { _shell.Pc?.Open(target); }
        catch (ActionFailure ex)
        {
            Log.Warn("Mac-style desktop: opening failed", ex);
            _window?.ShowStatus("Open failed");
        }
    }

    private void ShowInExplorer(string path)
    {
        try { _shell.Pc?.LaunchExecutable(new ExecutableLaunch(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"), $"/select,\"{path}\"")); }
        catch (ActionFailure ex) { Log.Warn("Mac-style desktop: Show in Explorer failed", ex); }
    }

    private void CloseAll(DockApp app)
    {
        foreach (var window in app.Windows)
            _shell.Window(window.Handle, WindowAction.Close);
    }

    /// <summary>
    /// Closes every window the app has, on every desktop: the windows the dock shows under its icon, matched by its ID or
    /// its program as they are there, not by one program's path (a pin's launcher isn't the program its windows run).
    /// </summary>
    private void Quit(DockApp app)
    {
        foreach (var window in (Everywhere(app) ?? app).Windows)
            _shell.Window(window.Handle, WindowAction.Close);
    }

    /// <summary>Empties the Recycle Bin once its confirm said Empty (DockWindow.ConfirmEmptyRecycleBin).</summary>
    public async void EmptyRecycleBin()
    {
        // An empty bin answers 0x8000FFFF (E_UNEXPECTED): there was nothing to do, not a failure.
        int result = await Task.Run(() => SHEmptyRecycleBin(0, null, 0x1 | 0x2 | 0x4)); // no confirm, no progress, no sound
        if (result < 0 && result != unchecked((int)0x8000FFFF))
        {
            Log.Warn($"Mac-style desktop: emptying the Recycle Bin failed (0x{result:X8})");
            _window?.ShowStatus("Empty failed");
        }
        _window?.CheckBin();
    }

    // ----- Pins -----

    public void Pin(string launch, int? at)
    {
        var pins = Settings.Pins ?? [];
        Save(Settings with { Pins = DockApps.Move(pins, launch, at ?? pins.Count) });
    }

    /// <summary>
    /// Puts a dragged app at <paramref name="index"/> in the row of apps, wherever that is. One that wasn't pinned is kept
    /// from then on.
    /// </summary>
    public void Place(DockApp app, int index)
    {
        var (order, pins) = DockApps.Place(Apps, Apps.FirstOrDefault(a => a.Key == app.Key) ?? app, index);
        _order.Clear();
        _order.AddRange(order);
        Save(Settings with { Pins = pins });
    }

    /// <summary>Takes the pin off; <paramref name="offerUndo"/> shows "Removed" with Undo, which puts it back where it was.</summary>
    public void Unpin(string launch, bool offerUndo = false)
    {
        var pins = Settings.Pins ?? [];
        int was = pins.ToList().FindIndex(p => string.Equals(p, launch, StringComparison.OrdinalIgnoreCase));
        if (was < 0)
            return;
        Save(Settings with { Pins = pins.Where((_, i) => i != was).ToArray() });
        if (offerUndo)
            _window?.ShowStatus("Removed", () => Pin(launch, was));
    }

    // ----- Drops -----

    /// <summary>Files dropped on an app open with it.</summary>
    public void OpenWith(DockApp app, IReadOnlyList<string> files)
    {
        string program = app.Path;
        if (!program.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            return;
        Launch(program, string.Join(' ', files.Select(f => $"\"{f}\"")));
    }

    /// <summary>Files dropped on Downloads move there; a name already taken gets " (2)", as Explorer does.</summary>
    public void MoveToDownloads(IReadOnlyList<string> paths)
    {
        string folder = Downloads;
        bool failed = false;
        foreach (var path in paths)
        {
            try
            {
                if (string.Equals(Path.GetDirectoryName(path.TrimEnd('\\')), folder.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                    continue;
                string target = Path.Combine(folder, DockApps.FreeName(Path.GetFileName(path.TrimEnd('\\')),
                    name => File.Exists(Path.Combine(folder, name)) || Directory.Exists(Path.Combine(folder, name))));
                if (Directory.Exists(path))
                    Directory.Move(path, target);
                else
                    File.Move(path, target);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                Log.Warn("Mac-style desktop: moving to Downloads failed", ex);
                failed = true;
            }
        }
        if (failed)
            _window?.ShowStatus("Move failed");
    }

    /// <summary>
    /// Files dropped on the Recycle Bin go to it, as Explorer's Delete does. One that can't be recycled (too big for the
    /// bin, or on a drive without one) is never deleted for good without Windows asking first. Off the UI thread: a large
    /// folder takes a while.
    /// </summary>
    public async void Recycle(IReadOnlyList<string> paths)
    {
        if (paths.Count == 0)
            return;
        string from = string.Join('\0', paths) + "\0\0";
        var (result, aborted) = await OnSta(() =>
        {
            var operation = new FileOperation
            {
                Function = 0x3, // FO_DELETE
                From = from,
                Flags = 0x40 | 0x400 | 0x10 | 0x4000, // FOF_ALLOWUNDO | FOF_NOERRORUI | FOF_NOCONFIRMATION | FOF_WANTNUKEWARNING
            };
            int code = SHFileOperation(ref operation);
            return (code, operation.Aborted);
        });
        if (_disposed)
            return;
        // Declining Windows' question (1223, or the operation's own 0x75) is an answer, not a failure.
        if (result != 0 && !aborted && result is not (1223 or 0x75))
        {
            Log.Warn($"Mac-style desktop: recycling failed ({result})");
            _window?.ShowStatus("Delete failed");
        }
        _window?.CheckBin();
    }

    /// <summary>Runs shell work on its own STA thread, which Windows' file operations need for their questions.</summary>
    private static Task<T> OnSta<T>(Func<T> work)
    {
        var done = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try { done.SetResult(work()); }
            catch (Exception ex) { done.SetException(ex); }
        })
        { IsBackground = true, Name = "Chippi dock file operation" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return done.Task;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct FileOperation
    {
        public nint Window;
        public uint Function;
        public string From;
        public string? To;
        public ushort Flags;
        [MarshalAs(UnmanagedType.Bool)] public bool Aborted;
        public nint NameMappings;
        public string? ProgressTitle;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "SHFileOperationW")] private static extern int SHFileOperation(ref FileOperation operation);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "SHEmptyRecycleBinW")] private static extern int SHEmptyRecycleBin(nint window, string? root, uint flags);

    [DllImport("shell32.dll")] private static extern int SHGetKnownFolderPath(in Guid id, uint flags, nint token, out nint path);

    private static string? KnownFolder(Guid id)
    {
        if (SHGetKnownFolderPath(id, 0, 0, out nint path) != 0)
            return null;
        try { return Marshal.PtrToStringUni(path); }
        finally { Marshal.FreeCoTaskMem(path); }
    }

    public void Dispose()
    {
        AppNames.CatalogRead -= OnCatalogRead;
        if (_disposed)
            return;
        _disposed = true;
        _settle.Stop();
        _badges?.Dispose();
        _badges = null;
        _keys?.Dispose();
        _keys = null;
        foreach (var hook in _hooks)
            Native.UnhookWinEvent(hook);
        _hooks.Clear();
        _window?.Close();
        _window = null;
    }

    /// <summary>The main display, where the dock sits.</summary>
    public static nint MainDisplay => DisplayCatalog.Main;
}
