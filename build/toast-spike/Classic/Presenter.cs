// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using Microsoft.Win32;
using Chippi.Core.Notifications;
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;

namespace Chippi.ToastSpike;

internal sealed class Presenter(Action<string> activate) : IToastPresenter, IDisposable
{
    internal const string Aumid = "velopack.ChippiToastClassicSpike";
    internal const string ActivatorId = "8F276553-88D0-4A4A-A88D-4357F687C87E";
    private static string Shortcut => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Chippi Toast Classic Spike.lnk");
    private ClassFactory? _factory;
    private uint _cookie;

    public void Register()
    {
        // Explicit opt-in only. In production run from ChippiApp/current/Chippi.exe,
        // use the existing Velopack shortcut and velopack.ChippiApp AUMID.
        var executable = Environment.ProcessPath ?? throw new InvalidOperationException("No executable path.");
        using (var key = Registry.CurrentUser.CreateSubKey($@"Software\Classes\CLSID\{{{ActivatorId}}}\LocalServer32"))
            key.SetValue(null, $"\"{executable}\" --toast-server");
        ShortcutRegistration.Write(Shortcut, executable, Aumid, Guid.Parse(ActivatorId));
        Marshal.ThrowExceptionForHR(Native.SetCurrentProcessExplicitAppUserModelID(Aumid));
        _factory = new ClassFactory(activate);
        var id = Guid.Parse(ActivatorId);
        Marshal.ThrowExceptionForHR(Native.CoRegisterClassObject(ref id, _factory, 4, 1, out _cookie));
    }

    public void Show(string moduleId, Notice notice)
    {
        var xml = new XmlDocument();
        xml.LoadXml(Payload.Xml(notice));
        ToastNotificationManager.CreateToastNotifier(Aumid).Show(new ToastNotification(xml));
    }

    public static void Uninstall()
    {
        try { ToastNotificationManager.History.Clear(Aumid); }
        catch (Exception ex) { Console.Error.WriteLine(ex); }
        try { Registry.CurrentUser.DeleteSubKeyTree($@"Software\Classes\CLSID\{{{ActivatorId}}}", false); }
        catch (Exception ex) { Console.Error.WriteLine(ex); }
        File.Delete(Shortcut);
    }

    public void Dispose()
    {
        if (_cookie != 0)
        {
            Marshal.ThrowExceptionForHR(Native.CoRevokeClassObject(_cookie));
            _cookie = 0;
        }
        GC.KeepAlive(_factory);
    }
}

[ComVisible(true), Guid("53E31837-6600-4A81-9395-75CFFE746F94"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface INotificationActivationCallback
{
    void Activate([MarshalAs(UnmanagedType.LPWStr)] string appUserModelId,
        [MarshalAs(UnmanagedType.LPWStr)] string invokedArgs, IntPtr data, uint count);
}

[ComVisible(true), ClassInterface(ClassInterfaceType.None)]
public sealed class ActivationCallback(Action<string> activate) : INotificationActivationCallback
{
    public void Activate(string appUserModelId, string invokedArgs, IntPtr data, uint count)
    {
        // Never propagate managed exceptions across the COM boundary.
        try { activate(invokedArgs); }
        catch (Exception ex) { Console.Error.WriteLine(ex); }
    }
}

[ComVisible(true), Guid("00000001-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IClassFactory
{
    [PreserveSig] int CreateInstance(IntPtr outer, ref Guid iid, out IntPtr instance);
    [PreserveSig] int LockServer([MarshalAs(UnmanagedType.Bool)] bool locked);
}

[ComVisible(true), ClassInterface(ClassInterfaceType.None)]
public sealed class ClassFactory(Action<string> activate) : IClassFactory
{
    public int CreateInstance(IntPtr outer, ref Guid iid, out IntPtr instance)
    {
        instance = IntPtr.Zero;
        if (outer != IntPtr.Zero) return unchecked((int)0x80040110);
        var unknown = Marshal.GetIUnknownForObject(new ActivationCallback(activate));
        try { return Marshal.QueryInterface(unknown, in iid, out instance); }
        finally { Marshal.Release(unknown); }
    }
    public int LockServer(bool locked) => 0;
}

internal static class Native
{
    [DllImport("ole32.dll")] internal static extern int CoRegisterClassObject(ref Guid clsid,
        [MarshalAs(UnmanagedType.Interface)] IClassFactory factory, uint context, uint flags, out uint cookie);
    [DllImport("ole32.dll")] internal static extern int CoRevokeClassObject(uint cookie);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] internal static extern int SetCurrentProcessExplicitAppUserModelID(string appId);
}

internal static class ShortcutRegistration
{
    public static void Write(string path, string executable, string aumid, Guid activator)
    {
        var link = (IShellLinkW)new ShellLink();
        try
        {
            link.SetPath(executable);
            link.SetDescription("Chippi toast spike");
            var store = (IPropertyStore)link;
            var fmt = Guid.Parse("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3");
            var nameKey = new PropertyKey { Format = fmt, Id = 5 };
            var clsidKey = new PropertyKey { Format = fmt, Id = 26 };
            var name = new PropVariant { Type = 31, Pointer = Marshal.StringToCoTaskMemUni(aumid) };
            var clsid = new PropVariant { Type = 72, Pointer = Marshal.AllocCoTaskMem(16) };
            try
            {
                Marshal.StructureToPtr(activator, clsid.Pointer, false);
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
