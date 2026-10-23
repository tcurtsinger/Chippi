// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.IO;
using Chippi.App.Notifications;
using Microsoft.Win32;

public sealed class RegistrationTests : IDisposable
{
    private readonly string _root = @"Software\Chippi.Toast.Tests-" + Guid.NewGuid().ToString("N");
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "chippi-toast-tests-" + Guid.NewGuid().ToString("N"));
    public void Dispose()
    {
        Registry.CurrentUser.DeleteSubKeyTree(_root, false);
        if (Directory.Exists(_folder)) Directory.Delete(_folder, true);
    }

    [Fact]
    public void Apply_reapply_and_remove_only_owned_dev_registration()
    {
        var a = ToastIdentity.Create("test-a", @"C:\Tools\Chippi.exe", _folder);
        var b = ToastIdentity.Create("test-b", @"C:\Tools\Chippi.exe", _folder);
        var registration = new ToastRegistration(a, _root);
        var neighbor = new ToastRegistration(b, _root);
        registration.Apply(); neighbor.Apply();
        using(var key = Registry.CurrentUser.OpenSubKey(registration.ComKey + @"\LocalServer32")!)
            Assert.Equal($"\"{a.Executable}\" --toast-server --dev-profile \"test-a\"", key.GetValue(null));
        Assert.True(File.Exists(a.Shortcut));
        AssertShortcutIdentity(a);
        using(var key = Registry.CurrentUser.OpenSubKey(registration.ComKey + @"\LocalServer32", true)!) key.SetValue(null, "stale");
        registration.Apply();
        AssertShortcutIdentity(a);
        using(var key = Registry.CurrentUser.OpenSubKey(registration.ComKey + @"\LocalServer32")!) Assert.Contains("--toast-server", (string)key.GetValue(null)!);
        registration.Remove(); registration.Remove();
        Assert.Null(Registry.CurrentUser.OpenSubKey(registration.ComKey)); Assert.False(File.Exists(a.Shortcut));
        using(var key = Registry.CurrentUser.OpenSubKey(neighbor.ComKey)) Assert.NotNull(key);
        Assert.True(File.Exists(b.Shortcut));
    }

    [Fact]
    public void Production_cleanup_preserves_the_existing_velopack_owned_shortcut()
    {
        var identity = ToastIdentity.Create("fixture", @"C:\Tools\Chippi.exe", _folder);
        ShortcutProperties.Apply(identity); // create a test-owned stand-in, never the real Start Menu
        identity = identity with { Profile = null };
        var registration = new ToastRegistration(identity, _root);
        registration.Apply(); registration.Remove();
        Assert.True(File.Exists(identity.Shortcut));
        Assert.Null(Registry.CurrentUser.OpenSubKey(registration.ComKey));
    }

    private static void AssertShortcutIdentity(ToastIdentity identity)
    {
        dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application")!)!;
        dynamic folder = shell.NameSpace(Path.GetDirectoryName(identity.Shortcut)!);
        dynamic item = folder.ParseName(Path.GetFileName(identity.Shortcut));
        try
        {
            Assert.Equal(identity.AppId, (string)item.ExtendedProperty("System.AppUserModel.ID"));
            Assert.Equal(identity.Activator, Guid.Parse((string)item.ExtendedProperty("System.AppUserModel.ToastActivatorCLSID")));
        }
        finally
        {
            System.Runtime.InteropServices.Marshal.FinalReleaseComObject(item);
            System.Runtime.InteropServices.Marshal.FinalReleaseComObject(folder);
            System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell);
        }
    }
    [Fact]
    public void Missing_installed_shortcut_does_not_leave_a_com_key()
    {
        var identity = ToastIdentity.Create(null, @"C:\Tools\Chippi.exe", _folder);
        var registration = new ToastRegistration(identity, _root);
        Assert.Throws<FileNotFoundException>(registration.Apply);
        Assert.Null(Registry.CurrentUser.OpenSubKey(registration.ComKey));
        Assert.False(File.Exists(identity.Shortcut));
    }
}