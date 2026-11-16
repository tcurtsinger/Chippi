// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Chippi.Elevated.Protocol;
using Microsoft.Win32.SafeHandles;

namespace Chippi.Elevated;

public sealed class PackageUpdater(SafeAccessTokenHandle standardToken) : IPackageUpdater
{
    public static IReadOnlyList<string> Arguments(string id)
    {
        if (!Messages.PackageIdAllowed(id)) throw new ArgumentException("Invalid package id.", nameof(id));
        return ["upgrade", "--id", id, "--exact", "--source", "winget", "--scope", "machine", "--silent",
            "--accept-package-agreements", "--accept-source-agreements", "--disable-interactivity"];
    }

    public async Task<AdminReply> UpdateAsync(AdminRequest request)
    {
        if (!Messages.RequestAllowed(request) || request.Verb != "UpdatePackage")
            return new(request.Id, "Refused", request.PackageId, Reason: "Invalid package id");
        try
        {
            string executable = FindWinget();
            var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Path.GetDirectoryName(executable)! };
            foreach (var arg in Arguments(request.PackageId!)) start.ArgumentList.Add(arg);
            foreach (string key in start.Environment.Keys.Where(k => k.StartsWith("DOTNET_", StringComparison.OrdinalIgnoreCase)
                || k.StartsWith("COR_", StringComparison.OrdinalIgnoreCase) || k.StartsWith("COMPLUS_", StringComparison.OrdinalIgnoreCase)
                || k.Equals("__COMPAT_LAYER", StringComparison.OrdinalIgnoreCase)).ToArray()) start.Environment.Remove(key);
            start.Environment["PATH"] = Environment.GetFolderPath(Environment.SpecialFolder.System);
            Process process;
            using (TrustedExecutable.Open(executable, standardToken))
                process = Process.Start(start) ?? throw new InvalidOperationException("winget did not start.");
            using (process)
            {
                await process.WaitForExitAsync().ConfigureAwait(false);
                int code = process.ExitCode;
                return new(request.Id, code == 0 ? "Done" : "Failed", request.PackageId, code, code == 0 ? null : "Package update failed");
            }
        }
        catch (UnauthorizedAccessException) { return new(request.Id, "Refused", request.PackageId, Reason: "Untrusted winget executable"); }
        catch (Exception ex) when (ex is IOException or Win32Exception or InvalidOperationException)
        { return new(request.Id, "Failed", request.PackageId, Reason: "Package update failed"); }
    }

    internal static string FindWinget()
    {
        uint count = 0, length = 0;
        const string family = "Microsoft.DesktopAppInstaller_8wekyb3d8bbwe";
        int result = GetPackagesByPackageFamily(family, ref count, 0, ref length, 0);
        if (result != 122 || count is 0 or > 100 || length > 65536) throw new InvalidOperationException("App Installer unavailable.");
        var pointers = Marshal.AllocHGlobal(checked((int)count * nint.Size));
        var buffer = Marshal.AllocHGlobal(checked((int)length * 2));
        try
        {
            result = GetPackagesByPackageFamily(family, ref count, pointers, ref length, buffer);
            if (result != 0) throw new Win32Exception(result);
            var candidates = new List<string>();
            for (int i = 0; i < count; i++)
            {
                string name = Marshal.PtrToStringUni(Marshal.ReadIntPtr(pointers, i * nint.Size))!;
                uint size = 0;
                if (GetPackagePathByFullName(name, ref size, null) != 122 || size > 32768) continue;
                var path = new StringBuilder((int)size);
                if (GetPackagePathByFullName(name, ref size, path) != 0) continue;
                string exe = Path.Combine(path.ToString(), "winget.exe");
                string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WindowsApps") + Path.DirectorySeparatorChar;
                if (exe.StartsWith(root, StringComparison.OrdinalIgnoreCase) && File.Exists(exe)) candidates.Add(exe);
            }
            return candidates.OrderDescending(StringComparer.OrdinalIgnoreCase).FirstOrDefault()
                ?? throw new InvalidOperationException("Protected App Installer unavailable.");
        }
        finally { Marshal.FreeHGlobal(pointers); Marshal.FreeHGlobal(buffer); }
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern int GetPackagesByPackageFamily(string family, ref uint count, nint names, ref uint length, nint buffer);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern int GetPackagePathByFullName(string name, ref uint length, StringBuilder? path);
}
