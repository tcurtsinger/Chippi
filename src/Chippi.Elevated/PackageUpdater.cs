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
    /// <summary>Winget exit code for "no package found", which with a pinned --version means the reviewed version is gone.</summary>
    internal const int NoPackageFound = unchecked((int)0x8A150014);

    public static IReadOnlyList<string> Arguments(string id, string version, bool includeUnknown)
    {
        if (!Messages.PackageIdAllowed(id)) throw new ArgumentException("Invalid package id.", nameof(id));
        if (!Messages.VersionAllowed(version)) throw new ArgumentException("Invalid package version.", nameof(version));
        var args = new List<string> { "upgrade", "--id", id, "--version", version, "--exact", "--source", "winget", "--scope", "machine", "--silent" };
        if (includeUnknown) args.Add("--include-unknown");
        args.AddRange(["--accept-package-agreements", "--accept-source-agreements", "--disable-interactivity"]);
        return args;
    }

    internal static AdminReply Result(AdminRequest request, int code) => code switch
    {
        0 => new(request.Id, "Done", request.PackageId, 0),
        NoPackageFound => new(request.Id, "Failed", request.PackageId, code, "Version changed"),
        _ => new(request.Id, "Failed", request.PackageId, code, "Package update failed"),
    };

    public async Task<AdminReply> UpdateAsync(AdminRequest request, CancellationToken ceiling)
    {
        if (!Messages.RequestAllowed(request) || request.Verb != "UpdatePackage")
            return new(request.Id, "Refused", request.PackageId, Reason: "Invalid package id");
        try
        {
            string executable = FindWinget();
            var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Path.GetDirectoryName(executable)! };
            foreach (var arg in Arguments(request.PackageId!, request.ExpectedVersion!, request.IncludeUnknown)) start.ArgumentList.Add(arg);
            foreach (string key in start.Environment.Keys.Where(k => k.StartsWith("DOTNET_", StringComparison.OrdinalIgnoreCase)
                || k.StartsWith("COR_", StringComparison.OrdinalIgnoreCase) || k.StartsWith("COMPLUS_", StringComparison.OrdinalIgnoreCase)
                || k.Equals("__COMPAT_LAYER", StringComparison.OrdinalIgnoreCase)).ToArray()) start.Environment.Remove(key);
            start.Environment["PATH"] = Environment.GetFolderPath(Environment.SpecialFolder.System);
            Process process;
            using (TrustedExecutable.Open(executable, standardToken))
                process = Process.Start(start) ?? throw new InvalidOperationException("winget did not start.");
            using (process)
            {
                try { await process.WaitForExitAsync(ceiling).ConfigureAwait(false); }
                catch (OperationCanceledException)
                {
                    // Only the helper's ceiling cancels this wait: a hung winget is stopped with its installer children.
                    try { process.Kill(entireProcessTree: true); } catch (Exception ex) when (ex is InvalidOperationException or Win32Exception) { }
                    return new(request.Id, "Failed", request.PackageId, Reason: "Package update timed out");
                }
                return Result(request, process.ExitCode);
            }
        }
        catch (UnauthorizedAccessException error) { return new(request.Id, "Refused", request.PackageId, Reason: "Untrusted winget executable: " + error.Message); }
        catch (Exception ex) when (ex is IOException or Win32Exception or InvalidOperationException)
        { return new(request.Id, "Failed", request.PackageId, Reason: "Package update failed"); }
    }

    /// <summary>Reads Microsoft.DesktopAppInstaller_1.24.2511.0_x64__8wekyb3d8bbwe; versions compare as numbers, never as text.</summary>
    public static (Version Version, string Architecture)? ParseFullName(string fullName)
    {
        var parts = fullName.Split('_');
        return parts.Length >= 5 && Version.TryParse(parts[1], out var version) ? (version, parts[2]) : null;
    }

    public static bool ArchitectureMatches(string architecture) => architecture.Equals("neutral", StringComparison.OrdinalIgnoreCase)
        || architecture.Equals(System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(), StringComparison.OrdinalIgnoreCase);

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
            var candidates = new List<(string Name, string Exe)>();
            for (int i = 0; i < count; i++)
            {
                string name = Marshal.PtrToStringUni(Marshal.ReadIntPtr(pointers, i * nint.Size))!;
                uint size = 0;
                if (GetPackagePathByFullName(name, ref size, null) != 122 || size > 32768) continue;
                var path = new StringBuilder((int)size);
                if (GetPackagePathByFullName(name, ref size, path) != 0) continue;
                string exe = Path.Combine(path.ToString(), "winget.exe");
                string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WindowsApps") + Path.DirectorySeparatorChar;
                if (exe.StartsWith(root, StringComparison.OrdinalIgnoreCase) && File.Exists(exe)) candidates.Add((name, exe));
            }
            return candidates.Select(c => (Path: c.Exe, Parsed: ParseFullName(c.Name))).Where(c => c.Parsed is not null && ArchitectureMatches(c.Parsed.Value.Architecture))
                .OrderByDescending(c => c.Parsed!.Value.Version).Select(c => c.Path).FirstOrDefault()
                ?? throw new InvalidOperationException("Protected App Installer unavailable.");
        }
        finally { Marshal.FreeHGlobal(pointers); Marshal.FreeHGlobal(buffer); }
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern int GetPackagesByPackageFamily(string family, ref uint count, nint names, ref uint length, nint buffer);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern int GetPackagePathByFullName(string name, ref uint length, StringBuilder? path);
}
