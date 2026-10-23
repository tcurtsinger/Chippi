// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace Chippi.App.Notifications;

[ComVisible(true), Guid("53E31837-6600-4A81-9395-75CFFE746F94"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface INotificationActivationCallback
{
    void Activate([MarshalAs(UnmanagedType.LPWStr)] string appUserModelId,
        [MarshalAs(UnmanagedType.LPWStr)] string invokedArgs, IntPtr data, uint count);
}

[ComVisible(true), ClassInterface(ClassInterfaceType.None)]
public sealed class ActivationCallback(string appId, Action<string> activate) : INotificationActivationCallback
{
    public void Activate(string appUserModelId, string invokedArgs, IntPtr data, uint count)
    {
        // Never propagate managed exceptions across the COM boundary.
        try { if (string.Equals(appUserModelId, appId, StringComparison.Ordinal)) activate(invokedArgs); }
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
public sealed class ClassFactory(string appId, Action<string> activate) : IClassFactory
{
    public int CreateInstance(IntPtr outer, ref Guid iid, out IntPtr instance)
    {
        instance = IntPtr.Zero;
        if (outer != IntPtr.Zero) return unchecked((int)0x80040110);
        var unknown = Marshal.GetIUnknownForObject(new ActivationCallback(appId, activate));
        try { return Marshal.QueryInterface(unknown, in iid, out instance); }
        finally { Marshal.Release(unknown); }
    }
    public int LockServer(bool locked) => 0;
}

internal static class ToastNative
{
    [DllImport("ole32.dll")] internal static extern int CoRegisterClassObject(ref Guid clsid,
        [MarshalAs(UnmanagedType.Interface)] IClassFactory factory, uint context, uint flags, out uint cookie);
    [DllImport("ole32.dll")] internal static extern int CoRevokeClassObject(uint cookie);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] internal static extern int SetCurrentProcessExplicitAppUserModelID(string appId);
}

