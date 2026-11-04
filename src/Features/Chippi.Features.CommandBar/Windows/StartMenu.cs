// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Windows.Media;
using Chippi.Core.Diagnostics;
using Chippi.Features.CommandBar.Search;
using Chippi.UI.Controls;

namespace Chippi.Features.CommandBar.Windows;

/// <summary>
/// The Start menu's apps, as <c>shell:AppsFolder</c> lists them (desktop and Store apps alike), and their icons. The shell
/// is asked on one STA thread of its own, so a slow read never holds up typing.
/// </summary>
internal sealed class StartMenu : IDisposable
{
    private static readonly Guid EnumItems = new("94f60519-2850-4924-aa5a-d15e84868039"); // BHID_EnumItems
    private const uint NormalDisplay = 0; // SIGDN_NORMALDISPLAY
    private const uint ParentRelativeParsing = 0x80018001; // SIGDN_PARENTRELATIVEPARSING

    private readonly BlockingCollection<Action> _work = [];
    private readonly Thread _thread;
    private readonly object _iconsGate = new();
    private readonly Dictionary<(string, int), Task<ImageSource?>> _icons = [];
    private readonly Dictionary<(nint, string?, int), Task<ImageSource?>> _windowIcons = [];
    private readonly Func<IReadOnlyList<StartApp>> _readApps;
    private readonly Func<string, int, ImageSource?> _readIcon;
    private readonly Func<nint, int, ImageSource?> _readWindowIcon;
    private int _stopped;

    public StartMenu() : this(Read, AppIcon.Read, AppIcon.ReadWindow) { }

    internal StartMenu(Func<IReadOnlyList<StartApp>> readApps, Func<string, int, ImageSource?> readIcon, Func<nint, int, ImageSource?> readWindowIcon)
    {
        _readApps = readApps;
        _readIcon = readIcon;
        _readWindowIcon = readWindowIcon;
        _thread = new Thread(() =>
        {
            foreach (var work in _work.GetConsumingEnumerable())
            {
                try { work(); }
                catch (Exception ex) { Log.Warn("Command bar: reading the Start menu failed", ex); }
            }
        })
        { IsBackground = true, Name = "Command bar Start menu" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
    }

    /// <summary>Every app, by name, without duplicates of the same name and target.</summary>
    public Task<IReadOnlyList<StartApp>> ReadAsync() => Run(() =>
    {
        var apps = _readApps();
        // A fresh app list can recover an icon that was unavailable during installation or startup.
        lock (_iconsGate)
        {
            RemoveMissing(_icons);
            RemoveMissing(_windowIcons);
        }
        return apps;
    });

    private static void RemoveMissing<TKey>(Dictionary<TKey, Task<ImageSource?>> icons) where TKey : notnull
    {
        foreach (var key in icons.Where(pair => pair.Value.IsCompleted
            && (!pair.Value.IsCompletedSuccessfully || pair.Value.Result is null)).Select(pair => pair.Key).ToArray())
            icons.Remove(key);
    }

    private static IReadOnlyList<StartApp> Read()
    {
        var folder = Item("shell:AppsFolder");
        var apps = new List<StartApp>();
        try
        {
            folder.BindToHandler(IntPtr.Zero, EnumItems, typeof(IEnumShellItems).GUID, out object found);
            try
            {
                var items = (IEnumShellItems)found;
                var one = new IShellItem[1];
                while (items.Next(1, one, out uint fetched) == 0 && fetched == 1)
                {
                    var item = one[0];
                    try
                    {
                        string? name = Name(item, NormalDisplay), parsing = Name(item, ParentRelativeParsing);
                        if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(parsing))
                            apps.Add(new StartApp(name, parsing));
                    }
                    finally { Marshal.ReleaseComObject(item); }
                }
            }
            finally { Marshal.ReleaseComObject(found); }
        }
        finally { Marshal.ReleaseComObject(folder); }
        return [.. apps.DistinctBy(a => (a.Name, a.ParsingName)).OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase)];
    }

    /// <summary>The app's icon at sufficient source resolution for <paramref name="pixels"/> display pixels; kept once read.</summary>
    public Task<ImageSource?> IconAsync(StartApp app, int pixels) => IconOfAsync(@"shell:AppsFolder\" + app.ParsingName, pixels);

    /// <summary>A file's, a folder's or a program's icon (the Radial menu's slices), sized for <paramref name="pixels"/> display pixels; kept once read.</summary>
    public Task<ImageSource?> PathIconAsync(string path, int pixels) => IconOfAsync(path, pixels);

    /// <summary>The executable's shell icon, then the window's own icon. Window messages are bounded and never on the UI thread.</summary>
    public Task<ImageSource?> WindowIconAsync(OpenWindow window, int pixels)
    {
        lock (_iconsGate)
        {
            // The icon belongs to the app, not its changing document title. Keep pending reads shared too.
            var key = (window.Handle, window.IconPath, pixels);
            if (_windowIcons.TryGetValue(key, out var known))
                return known;
            if (_windowIcons.Count >= 128)
                foreach (var old in _windowIcons.Where(pair => pair.Value.IsCompleted).Select(pair => pair.Key).ToArray())
                    _windowIcons.Remove(old);
            return _windowIcons[key] = Run(() =>
            {
                // ApplicationFrameHost's own icon is not the hosted app's identity.
                if (window.IconPath is { Length: > 0 } path && !path.EndsWith(@"\ApplicationFrameHost.exe", StringComparison.OrdinalIgnoreCase)
                    && _readIcon(path, pixels) is { } icon)
                    return icon;
                return _readWindowIcon(window.Handle, pixels);
            });
        }
    }

    private Task<ImageSource?> IconOfAsync(string parsing, int pixels)
    {
        lock (_iconsGate)
        {
            var key = (parsing, pixels);
            if (_icons.TryGetValue(key, out var known))
                return known;
            return _icons[key] = Run(() => _readIcon(parsing, pixels));
        }
    }

    private static IShellItem Item(string path)
    {
        SHCreateItemFromParsingName(path, IntPtr.Zero, typeof(IShellItem).GUID, out object item);
        return (IShellItem)item;
    }

    private static string? Name(IShellItem item, uint form)
    {
        if (item.GetDisplayName(form, out IntPtr text) != 0 || text == IntPtr.Zero)
            return null;
        try { return Marshal.PtrToStringUni(text); }
        finally { Marshal.FreeCoTaskMem(text); }
    }

    private Task<T> Run<T>(Func<T> read)
    {
        var done = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (Volatile.Read(ref _stopped) != 0)
            return Task.FromException<T>(new ObjectDisposedException(nameof(StartMenu)));
        try
        {
            _work.Add(() =>
            {
                try
                {
                    ObjectDisposedException.ThrowIf(Volatile.Read(ref _stopped) != 0, this);
                    T result = read();
                    ObjectDisposedException.ThrowIf(Volatile.Read(ref _stopped) != 0, this);
                    done.SetResult(result);
                }
                catch (Exception ex) { done.SetException(ex); }
            });
        }
        catch (InvalidOperationException) { done.SetException(new ObjectDisposedException(nameof(StartMenu))); }
        return done.Task;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _stopped, 1) != 0) return;
        _work.CompleteAdding();
        // Settle queued callers now; the stop guard prevents their shell delegates from running.
        while (_work.TryTake(out var skipped)) skipped();
        lock (_iconsGate)
        {
            _icons.Clear();
            _windowIcons.Clear();
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SHCreateItemFromParsingName(string path, IntPtr bindContext, [MarshalAs(UnmanagedType.LPStruct)] Guid riid, [MarshalAs(UnmanagedType.Interface)] out object item);

    [ComImport, Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem
    {
        void BindToHandler(IntPtr bindContext, [MarshalAs(UnmanagedType.LPStruct)] Guid handler, [MarshalAs(UnmanagedType.LPStruct)] Guid riid, [MarshalAs(UnmanagedType.Interface)] out object found);

        void GetParent([MarshalAs(UnmanagedType.Interface)] out IShellItem parent);

        [PreserveSig]
        int GetDisplayName(uint form, out IntPtr name);

        void GetAttributes(uint mask, out uint attributes);

        void Compare([MarshalAs(UnmanagedType.Interface)] IShellItem other, uint hint, out int order);
    }

    [ComImport, Guid("70629033-e363-4a28-a567-0db78006e6d7"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IEnumShellItems
    {
        [PreserveSig]
        int Next(uint count, [Out, MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.Interface, SizeParamIndex = 0)] IShellItem[] items, out uint fetched);
    }
}
