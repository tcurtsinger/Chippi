// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using Chippi.Core.Actions;
using Chippi.Core.Diagnostics;
using Chippi.Features.MacDesktop.MenuBar;
using Chippi.Features.MacDesktop.Shell;
using Chippi.UI.Actions;
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
    private DockWindow? _window;
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
        _proc = (_, _, window, objectId, _, _, _) =>
        {
            if (objectId == 0 && window != 0 && !_settle.IsEnabled)
                _settle.Start();
        };
    }

    public void Start()
    {
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
    }

    public void SettingsChanged()
    {
        ReadWindows();
        _window?.SettingsChanged();
    }

    public void RefreshDisplays() => _window?.Place();

    private void ReadWindows()
    {
        if (_disposed)
            return;
        var windows = _list.Read();
        var pins = (Settings.Pins ?? []).Select(p => (p, DockPins.Program(p), DockPins.Name(p))).ToArray();
        Apps = DockApps.Compose(pins, windows);
        _window?.SetApps(Apps);
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

    /// <summary>Not running, it opens; running, it comes to the front; already in front, it minimizes.</summary>
    public void Click(DockApp app)
    {
        if (!app.Running)
        {
            Launch(app.Launch);
            return;
        }
        nint front = Native.GetForegroundWindow();
        if (app.Windows.Any(w => w.Handle == front))
        {
            foreach (var window in app.Windows)
                _shell.Window(window.Handle, WindowAction.Minimize);
            return;
        }
        Activate(app.Windows[0].Handle);
    }

    /// <summary>Brings the window to the front, restored if it was minimized. The click on the dock earns the foreground.</summary>
    public void Activate(nint window)
    {
        if (Native.IsIconic(window))
            _shell.Window(window, WindowAction.Restore);
        Native.SetForegroundWindow(window);
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
            ? new BarMenuItem("New Window") { Run = () => Launch(app.Launch) }
            : new BarMenuItem("Open") { Run = () => Launch(app.Launch) });
        items.Add(app.Pinned
            ? new BarMenuItem("Remove from Dock") { Run = () => Unpin(app.Launch) }
            : new BarMenuItem("Keep in Dock") { Run = () => Pin(app.Launch, null) });
        if (File.Exists(app.Path))
            items.Add(new BarMenuItem("Show in Explorer") { Run = () => ShowInExplorer(app.Path) });
        if (app.Running)
        {
            items.Add(BarMenuItem.Separator);
            items.Add(new BarMenuItem(app.Windows.Count == 1 ? "Close Window" : "Close Windows") { Run = () => CloseAll(app) });
            items.Add(new BarMenuItem("Quit") { Run = () => Quit(app) });
        }
        return items;
    }

    public IReadOnlyList<BarMenuItem> DownloadsMenu()
    {
        var folder = Downloads;
        var items = DockApps.Newest(folder, 12).Select(file => new BarMenuItem(file.Name) { Run = () => Open(file.FullName) }).ToList<BarMenuItem>();
        if (items.Count == 0)
            items.Add(new BarMenuItem("Empty") { Enabled = false });
        items.Add(BarMenuItem.Separator);
        items.Add(new BarMenuItem("Open Downloads") { Run = () => Open(folder) });
        return items;
    }

    public IReadOnlyList<BarMenuItem> RecycleBinMenu() =>
    [
        new BarMenuItem("Open") { Run = () => Open("shell:RecycleBinFolder") },
        new BarMenuItem("Empty Recycle Bin") { Run = EmptyRecycleBin },
    ];

    public IReadOnlyList<BarMenuItem> StartMenu() =>
    [
        new BarMenuItem("Start") { Run = OpenStart },
        new BarMenuItem("Settings") { Run = () => Open("ms-settings:") },
        new BarMenuItem("Chippi Settings…") { Run = () => _shell.Context.OpenSettings($"module:{MacDesktopModule.Id}") },
    ];

    public void Open(string target)
    {
        try { _shell.Pc?.Open(target); }
        catch (ActionFailure ex) { Log.Warn("Mac-style desktop: opening failed", ex); }
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

    /// <summary>Closes every window the program has, on every desktop.</summary>
    private void Quit(DockApp app)
    {
        foreach (var window in _list.Read(includeOtherDesktops: true).Where(w => string.Equals(w.Executable, app.Path, StringComparison.OrdinalIgnoreCase)))
            _shell.Window(window.Handle, WindowAction.Close);
    }

    private static void EmptyRecycleBin()
    {
        if (MessageBox.Show("Empty Recycle Bin?", "Chippi", MessageBoxButton.OKCancel, MessageBoxImage.None, MessageBoxResult.Cancel) != MessageBoxResult.OK)
            return;
        int result = SHEmptyRecycleBin(0, null, 0x1 | 0x2 | 0x4); // no confirm, no progress, no sound
        if (result < 0)
            Log.Warn($"Mac-style desktop: emptying the Recycle Bin failed (0x{result:X8})");
    }

    // ----- Pins -----

    public void Pin(string launch, int? at)
    {
        var pins = Settings.Pins ?? [];
        Save(Settings with { Pins = DockApps.Move(pins, launch, at ?? pins.Count) });
    }

    public void Unpin(string launch) =>
        Save(Settings with { Pins = (Settings.Pins ?? []).Where(p => !string.Equals(p, launch, StringComparison.OrdinalIgnoreCase)).ToArray() });

    // ----- Drops -----

    /// <summary>Files dropped on an app open with it.</summary>
    public void OpenWith(DockApp app, IReadOnlyList<string> files)
    {
        string program = app.Path;
        if (!program.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            return;
        Launch(program, string.Join(' ', files.Select(f => $"\"{f}\"")));
    }

    /// <summary>Files dropped on Downloads move there.</summary>
    public void MoveToDownloads(IReadOnlyList<string> paths)
    {
        string folder = Downloads;
        foreach (var path in paths)
        {
            try
            {
                string target = Path.Combine(folder, Path.GetFileName(path));
                if (string.Equals(Path.GetDirectoryName(path), folder, StringComparison.OrdinalIgnoreCase) || File.Exists(target) || Directory.Exists(target))
                    continue;
                if (Directory.Exists(path))
                    Directory.Move(path, target);
                else
                    File.Move(path, target);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                Log.Warn("Mac-style desktop: moving to Downloads failed", ex);
            }
        }
    }

    /// <summary>Files dropped on the Recycle Bin go to it, as Explorer's Delete does.</summary>
    public static void Recycle(IReadOnlyList<string> paths)
    {
        if (paths.Count == 0)
            return;
        var operation = new FileOperation
        {
            Function = 0x3, // FO_DELETE
            From = string.Join('\0', paths) + "\0\0",
            Flags = 0x40 | 0x400 | 0x10, // FOF_ALLOWUNDO | FOF_NOERRORUI | FOF_NOCONFIRMATION
        };
        int result = SHFileOperation(ref operation);
        if (result != 0)
            Log.Warn($"Mac-style desktop: recycling failed ({result})");
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
        if (_disposed)
            return;
        _disposed = true;
        _settle.Stop();
        foreach (var hook in _hooks)
            Native.UnhookWinEvent(hook);
        _hooks.Clear();
        _window?.Close();
        _window = null;
    }

    /// <summary>The main display, where the dock sits.</summary>
    public static nint MainDisplay => DisplayCatalog.Main;
}
