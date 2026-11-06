// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.IO;
using System.Runtime.InteropServices;
using System.Xml;
using System.Xml.Linq;
using Chippi.Core.Actions;
using Microsoft.Win32;

namespace Chippi.UI.Actions;

/// <summary>Checks elevation metadata without executing the image, even in a session where UAC is disabled.</summary>
public static class ExecutableLaunchPolicy
{
    public static bool ManifestNeedsAdmin(Stream stream)
    {
        using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
        return XDocument.Load(reader).Descendants().Any(e => e.Name.LocalName == "requestedExecutionLevel"
            && ((string?)e.Attribute("level") is { } level && !level.Equals("asInvoker", StringComparison.OrdinalIgnoreCase)
                || string.Equals((string?)e.Attribute("uiAccess"), "true", StringComparison.OrdinalIgnoreCase)));
    }

    public static bool NeedsAdmin(string path)
    {
        const string layers = @"Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers";
        foreach (var root in new[] { Registry.CurrentUser, Registry.LocalMachine })
        {
            using var key = root.OpenSubKey(layers);
            if (key?.GetValue(path) is string flags && flags.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Contains("RUNASADMIN", StringComparer.OrdinalIgnoreCase)) return true;
        }
        // DATAFILE | IMAGE_RESOURCE: no executable code, imports or DllMain are run.
        nint image = LoadLibraryEx(path, 0, 0x2 | 0x20);
        if (image == 0) throw new ActionFailure("App target unavailable");
        try
        {
            nint resource = FindResource(image, 1, 24); // CREATEPROCESS_MANIFEST_RESOURCE_ID, RT_MANIFEST
            if (resource == 0)
            {
                if (!File.Exists(path + ".manifest")) return false;
                using var external = File.OpenRead(path + ".manifest");
                return ManifestNeedsAdmin(external);
            }
            uint size = SizeofResource(image, resource);
            nint data = LockResource(LoadResource(image, resource));
            if (size == 0 || size > 1024 * 1024 || data == 0) throw new ActionFailure("App target unavailable");
            byte[] bytes = new byte[size];
            Marshal.Copy(data, bytes, 0, bytes.Length);
            using var stream = new MemoryStream(bytes, writable: false);
            return ManifestNeedsAdmin(stream);
        }
        finally { FreeLibrary(image); }
    }

    [DllImport("kernel32.dll", EntryPoint = "LoadLibraryExW", CharSet = CharSet.Unicode)]
    private static extern nint LoadLibraryEx(string path, nint file, uint flags);
    [DllImport("kernel32.dll", EntryPoint = "FindResourceW")]
    private static extern nint FindResource(nint image, nint name, nint type);
    [DllImport("kernel32.dll")] private static extern uint SizeofResource(nint image, nint resource);
    [DllImport("kernel32.dll")] private static extern nint LoadResource(nint image, nint resource);
    [DllImport("kernel32.dll")] private static extern nint LockResource(nint data);
    [DllImport("kernel32.dll")] private static extern bool FreeLibrary(nint image);
}
