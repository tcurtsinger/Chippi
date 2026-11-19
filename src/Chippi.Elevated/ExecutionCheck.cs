// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors
namespace Chippi.Elevated;

/// <summary>One file or folder the execution check opened, held open until disposed. What it says comes from Windows (see NativeFsProbe), never from the helper's guess.</summary>
public interface IHeldNode : IDisposable
{
    bool IsReparsePoint { get; }
    /// <summary>The final path Windows reports for the open handle is the requested one: no link, junction, mapped drive or rename on the way.</summary>
    bool PathMatches { get; }
    string OwnerSid { get; }
    /// <summary>The rights AccessCheck grants the standard (non-elevated) token on this object.</summary>
    uint StandardGranted { get; }
}

public interface IFsProbe
{
    /// <summary>Opens the file (shared for read only) or folder (without FILE_SHARE_DELETE) without following a link; null when it can't be opened.</summary>
    IHeldNode? Open(string path, bool file);
}

/// <summary>An execution check said no. The message is short and names no path.</summary>
public sealed class ExecutionRefusedException(string reason) : Exception(reason);

/// <summary>
/// The execution check (admin-helper.md, Two file checks), for any file the helper runs elevated. The file, its folder and every folder above it
/// are opened without following links and stay held until this is disposed, which the caller does once the process has started. Refused when
/// the standard token is granted a right that could rewrite, replace or plant beside the file (or rename a folder on the way), when the owner
/// isn't Administrators, SYSTEM or TrustedInstaller, or when anything on the way is a reparse point.
/// </summary>
public sealed class ExecutionCheck : IDisposable
{
    public const uint FileWriteData = 0x2, FileAppendData = 0x4, Delete = 0x10000, WriteDac = 0x40000, WriteOwner = 0x80000;
    private const string TrustedInstaller = "S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464";
    private readonly List<IHeldNode> _held = [];

    public static ExecutionCheck Open(string path, IFsProbe probe)
    {
        var check = new ExecutionCheck();
        try
        {
            if (PathRules.Refusal(path) is { } unsafePath) throw new ExecutionRefusedException(unsafePath);
            check.Look(probe, path, file: true, FileWriteData | FileAppendData | Delete | WriteDac | WriteOwner, "file");
            string? folder = Path.GetDirectoryName(path);
            bool immediate = true;
            while (!string.IsNullOrEmpty(folder))
            {
                check.Look(probe, folder, file: false, TrustedExecutable.DirectoryDeniedRights(folder, immediate), immediate ? "folder" : "parent folder");
                immediate = false;
                folder = Path.GetDirectoryName(folder);
            }
            return check;
        }
        catch { check.Dispose(); throw; }
    }

    private void Look(IFsProbe probe, string path, bool file, uint denied, string what)
    {
        var node = probe.Open(path, file) ?? throw new ExecutionRefusedException($"The {what} can't be opened");
        _held.Add(node);
        if (node.IsReparsePoint) throw new ExecutionRefusedException($"The {what} is a link");
        if (!node.PathMatches) throw new ExecutionRefusedException($"The {what} moved");
        if (node.OwnerSid is not ("S-1-5-18" or "S-1-5-32-544" or TrustedInstaller)) throw new ExecutionRefusedException($"The {what} has an untrusted owner");
        if ((node.StandardGranted & denied) != 0) throw new ExecutionRefusedException($"The {what} is writable by a standard user");
    }

    public void Dispose() { foreach (var node in _held) node.Dispose(); _held.Clear(); }
}
