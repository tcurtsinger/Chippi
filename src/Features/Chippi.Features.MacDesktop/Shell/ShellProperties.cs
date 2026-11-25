// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Runtime.InteropServices;
using Chippi.Core.Actions;
using Chippi.UI.Actions;

namespace Chippi.Features.MacDesktop.Shell;

/// <summary>
/// AppUserModelIDs, the identity Windows groups taskbar buttons by: a shortcut's, a window's, or a Store app's process.
/// Read on the UI thread (COM, STA).
/// </summary>
internal static class ShellProperties
{
    private static readonly PropertyKey AppIdKey = new() { Format = new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), Id = 5 };

    /// <summary>The ID a property store holds; null for none.</summary>
    public static string? AppId(IPropertyStore store)
    {
        var value = default(PropVariant);
        try
        {
            var key = AppIdKey;
            if (store.GetValue(ref key, out value) != 0 || value.Type != 31 /* VT_LPWSTR */ || value.Pointer == 0)
                return null;
            string? id = Marshal.PtrToStringUni(value.Pointer);
            return string.IsNullOrWhiteSpace(id) ? null : id;
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException)
        {
            return null;
        }
        finally
        {
            PropVariantClear(ref value);
        }
    }

    /// <summary>
    /// The ID a window's taskbar button would group by: the one the window sets, else its Store app's; null for a desktop
    /// app that sets none (Windows then groups it by its program).
    /// </summary>
    public static string? AppId(ListedWindow window)
    {
        if (window.Handle != 0)
        {
            var iid = typeof(IPropertyStore).GUID;
            object? store = null;
            try
            {
                if (SHGetPropertyStoreForWindow(window.Handle, ref iid, out store) == 0 && store is IPropertyStore properties && AppId(properties) is { } own)
                    return own;
            }
            catch (Exception ex) when (ex is COMException or InvalidCastException) { }
            finally
            {
                if (store is not null && Marshal.IsComObject(store))
                    Marshal.ReleaseComObject(store);
            }
        }
        uint pid = window.AppProcessId != 0 ? window.AppProcessId : window.ProcessId;
        return pid != 0 ? RegisteredAppTargets.ProcessIdentity(pid) : null;
    }

    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IPropertyStore
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int GetAt(uint index, out PropertyKey key);
        [PreserveSig] int GetValue(ref PropertyKey key, out PropVariant value);
        [PreserveSig] int SetValue(ref PropertyKey key, ref PropVariant value);
        [PreserveSig] int Commit();
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    internal struct PropertyKey
    {
        public Guid Format;
        public uint Id;
    }

    [StructLayout(LayoutKind.Explicit, Size = 24)]
    internal struct PropVariant
    {
        [FieldOffset(0)] public ushort Type;
        [FieldOffset(8)] public nint Pointer;
    }

    [DllImport("ole32.dll")] private static extern int PropVariantClear(ref PropVariant value);

    [DllImport("shell32.dll")]
    private static extern int SHGetPropertyStoreForWindow(nint window, ref Guid iid, [MarshalAs(UnmanagedType.IUnknown)] out object store);
}

/// <summary>The names Explorer shows for shell items (a Store app's <c>shell:AppsFolder\ID</c>).</summary>
internal static class ShellNames
{
    public static string? DisplayName(string parsing)
    {
        var iid = typeof(IShellItem).GUID;
        IShellItem? item = null;
        nint name = 0;
        try
        {
            if (SHCreateItemFromParsingName(parsing, 0, ref iid, out item) != 0 || item is null || item.GetDisplayName(0 /* SIGDN_NORMALDISPLAY */, out name) != 0)
                return null;
            string? text = Marshal.PtrToStringUni(name);
            return string.IsNullOrWhiteSpace(text) ? null : text;
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or ArgumentException)
        {
            return null;
        }
        finally
        {
            if (name != 0)
                Marshal.FreeCoTaskMem(name);
            if (item is not null)
                Marshal.ReleaseComObject(item);
        }
    }

    [ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem
    {
        [PreserveSig] int BindToHandler(nint context, ref Guid handler, ref Guid iid, out nint result);
        [PreserveSig] int GetParent(out IShellItem parent);
        [PreserveSig] int GetDisplayName(uint form, out nint name);
        [PreserveSig] int GetAttributes(uint mask, out uint attributes);
        [PreserveSig] int Compare(IShellItem other, uint hint, out int order);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHCreateItemFromParsingName(string path, nint context, ref Guid iid, out IShellItem item);
}
