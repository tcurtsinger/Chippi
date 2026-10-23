// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace Chippi.App.Notifications;

internal static class ShortcutProperties
{
    public static void Apply(ToastIdentity identity)
    {
        string path = identity.Shortcut;
        var link = (IShellLinkW)new ShellLink();
        try
        {
            if (File.Exists(path)) ((IPersistFile)link).Load(path, 2);
            else
            {
                if (identity.Profile is null) throw new FileNotFoundException("Installed Chippi shortcut not found", path);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                link.SetPath(identity.Executable);
                link.SetArguments($"--dev-profile \"{identity.Profile}\"");
                link.SetDescription("Chippi");
            }
            var store = (IPropertyStore)link;
            var fmt = Guid.Parse("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3");
            var nameKey = new PropertyKey { Format = fmt, Id = 5 };
            var clsidKey = new PropertyKey { Format = fmt, Id = 26 };
            var name = new PropVariant { Type = 31, Pointer = Marshal.StringToCoTaskMemUni(identity.AppId) };
            var clsid = new PropVariant { Type = 72, Pointer = Marshal.AllocCoTaskMem(16) };
            try
            {
                Marshal.StructureToPtr(identity.Activator, clsid.Pointer, false);
                store.SetValue(ref nameKey, ref name);
                store.SetValue(ref clsidKey, ref clsid);
                store.Commit();
                ((IPersistFile)link).Save(path, true);
            }
            finally
            {
                Marshal.FreeCoTaskMem(name.Pointer);
                Marshal.FreeCoTaskMem(clsid.Pointer);
            }
        }
        finally { Marshal.FinalReleaseComObject(link); }
    }

    [ComImport, Guid("00021401-0000-0000-C000-000000000046")] private class ShellLink { }
    [StructLayout(LayoutKind.Sequential)] private struct PropertyKey { public Guid Format; public uint Id; }
    [StructLayout(LayoutKind.Explicit, Size = 24)] private struct PropVariant
    {
        [FieldOffset(0)] public ushort Type;
        [FieldOffset(8)] public IntPtr Pointer;
    }
    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        void GetCount(out uint count);
        void GetAt(uint index, out PropertyKey key);
        void GetValue(ref PropertyKey key, out PropVariant value);
        void SetValue(ref PropertyKey key, ref PropVariant value);
        void Commit();
    }
    [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLinkW
    {
        void GetPath(IntPtr path, int count, IntPtr data, uint flags);
        void GetIDList(out IntPtr list);
        void SetIDList(IntPtr list);
        void GetDescription(IntPtr description, int count);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string description);
        void GetWorkingDirectory(IntPtr directory, int count);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string directory);
        void GetArguments(IntPtr arguments, int count);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string arguments);
        void GetHotkey(out ushort hotkey);
        void SetHotkey(ushort hotkey);
        void GetShowCmd(out int command);
        void SetShowCmd(int command);
        void GetIconLocation(IntPtr path, int count, out int index);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string path, int index);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string path, uint reserved);
        void Resolve(IntPtr window, uint flags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string path);
    }
}
