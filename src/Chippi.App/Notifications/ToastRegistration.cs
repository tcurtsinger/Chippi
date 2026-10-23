// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.IO;
using Microsoft.Win32;

namespace Chippi.App.Notifications;

/// <summary>Persistent state only; live class factories belong to the presenter.</summary>
internal sealed class ToastRegistration(ToastIdentity identity, string classesRoot = @"Software\Classes")
{
    internal string ComKey => $@"{classesRoot}\CLSID\{{{identity.Activator}}}";

    internal void Apply()
    {
        if (!identity.CanRegister)
            throw new InvalidOperationException("Windows notifications require the installed executable or a dev profile");
        // Load an existing installed shortcut, preserving Velopack's target and other properties.
        // Only dev profiles create a separate, owned shortcut.
        ShortcutProperties.Apply(identity);
        using var key = Registry.CurrentUser.CreateSubKey(ComKey + @"\LocalServer32");
        key.SetValue(null, $"\"{identity.Executable}\" {identity.ServerArguments}");
    }

    internal void Remove()
    {
        Registry.CurrentUser.DeleteSubKeyTree(ComKey, false);
        if (identity.Profile is not null && File.Exists(identity.Shortcut)) File.Delete(identity.Shortcut);
    }
}