// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors
using Chippi.Elevated.Protocol;

namespace Chippi.Elevated.Tests;

/// <summary>Everything DeleteLeftovers touches, faked: no real registry, no real Recycle Bin, no process.</summary>
internal sealed class FakeApps : IInstalledApps
{
    public List<InstalledApp> All { get; } = [];
    public int Reads;
    public IReadOnlyList<InstalledApp> List() { Reads++; return [.. All]; }
    public FakeApps Hklm(string key, string? name, string? location, int view = 64, string? uninstall = null, string? icon = null)
    { All.Add(new InstalledApp("HKLM", view, key, name, location, uninstall, icon)); return this; }
    public FakeApps Hkcu(string key, string? name, string? location)
    { All.Add(new InstalledApp("HKCU", 64, key, name, location, null, null)); return this; }
}

internal sealed class FakeKeys : ILeftoverRegistry
{
    public Dictionary<(int View, string Path), KeyProbe> Keys { get; } = new();
    public List<(int View, string Path)> Deleted { get; } = [];
    public List<(int View, string Path)> Probed { get; } = [];
    public bool DeleteResult = true;
    public Action<(int View, string Path)>? OnDelete;
    public KeyProbe Probe(int view, string path) { Probed.Add((view, path)); return Keys.TryGetValue((view, path), out var probe) ? probe : new KeyProbe(false, false, false); }
    public bool DeleteTree(int view, string path) { OnDelete?.Invoke((view, path)); Deleted.Add((view, path)); return DeleteResult; }
    public FakeKeys Has(int view, string path, bool unsafeTree = false, bool standardCanDelete = false) { Keys[(view, path)] = new KeyProbe(true, unsafeTree, standardCanDelete); return this; }
}

internal sealed class FakeLeftoverSystem(FakeProbe probe) : ILeftoverSystem
{
    public Dictionary<string, string> Variables { get; } = new(StringComparer.OrdinalIgnoreCase) { ["ProgramFiles"] = @"C:\Program Files" };
    public string Expand(string text)
    {
        foreach (var (name, value) in Variables) text = text.Replace($"%{name}%", value, StringComparison.OrdinalIgnoreCase);
        return text;
    }
    public string SystemDirectory => @"C:\Windows\System32";
    public List<string> Roots { get; } =
    [
        @"C:\Program Files", @"C:\Program Files (x86)", @"C:\Program Files\Common Files", @"C:\Program Files (x86)\Common Files", @"C:\ProgramData",
        @"C:\Users", @"C:\Users\Test", @"C:\Users\Test\AppData", @"C:\Users\Test\AppData\Local", @"C:\Users\Test\AppData\Roaming",
    ];
    public List<string> Trees { get; } = [@"C:\Windows", @"C:\Users\Test\AppData\Local\Chippi", @"C:\Users\Test\AppData\Local\ChippiApp", @"C:\Users\Test\AppData\Roaming\Chippi"];
    public IReadOnlyList<string> ProtectedRoots => Roots;
    public IReadOnlyList<string> ProtectedTrees => Trees;
    public string BackupFolder => @"C:\Users\Test\AppData\Local\Chippi\leftovers-backup";

    public HashSet<string> Existing { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> Created { get; } = [];
    public List<string> Recycled { get; } = [];
    public RecycleOutcome Outcome = RecycleOutcome.Removed;
    public Action<string>? OnRecycle;
    public bool CanCreate = true;

    public bool Exists(string path) => Existing.Contains(path);
    public bool CreateFolder(string path)
    {
        if (!CanCreate) return false;
        Created.Add(path); Existing.Add(path); probe.Nodes[path] = new();
        return true;
    }
    public RecycleOutcome Recycle(string path)
    {
        Recycled.Add(path);
        OnRecycle?.Invoke(path);
        if (Outcome == RecycleOutcome.Removed) Existing.Remove(path);
        return Outcome;
    }
}

/// <summary>An item path decorated so the rig can seed the probe, the fake file system and the item together.</summary>
internal sealed class LeftoverRig
{
    public const string Firefox = @"C:\Program Files\Mozilla Firefox";
    public static readonly DateTimeOffset Start = new(2026, 11, 18, 12, 0, 0, TimeSpan.Zero);
    public readonly FakeRecords Records = new();
    public readonly FakeApps Apps = new();
    public readonly FakeProbe Probe = new();
    public readonly FakeKeys Keys = new();
    public readonly FakeLeftoverSystem System;
    public readonly FakeLauncher Launcher;
    public readonly StepClock Clock = new(Start);
    public readonly LeftoverActions Actions;
    public readonly UninstallRecord Record;

    public LeftoverRig(string? installLocation = Firefox, string? uninstallerFolder = @"C:\Program Files\Mozilla Firefox\uninstall", string? displayName = "Mozilla Firefox",
        string? publisher = "Mozilla", string keyName = "Mozilla Firefox 130.0 (x64 en-US)", int view = 64)
    {
        System = new FakeLeftoverSystem(Probe);
        Launcher = new FakeLauncher(Probe);
        Record = new UninstallRecord(Guid.NewGuid(), view, keyName, displayName, publisher, installLocation, null, uninstallerFolder, Start.UtcDateTime);
        Records.All.Add(Record);
        // reg.exe is run from System32 after the same execution check as an uninstaller.
        Probe.Trusting(@"C:\Windows\System32\reg.exe");
        // Writing the .reg file is reg.exe's job; the fake does it the moment the process starts.
        Launcher.AtStart = () =>
        {
            var line = Launcher.Started[^1].CommandLine;
            int open = line.IndexOf(".reg\"", StringComparison.Ordinal);
            if (open < 0) return;
            int begin = line.LastIndexOf('"', open) + 1;
            System.Existing.Add(line[begin..(open + 4)]);
        };
        Actions = new LeftoverActions(Records, Apps, Probe, Keys, System, Launcher, Clock);
    }

    /// <summary>A file or folder that exists, with every folder above it, owned by Administrators and granting the standard token nothing.</summary>
    public string Exists(string path) { Probe.Trusting(path); System.Existing.Add(path); return path; }

    public static LeftoverItem File(string path, string cls = Messages.ClassSafe, bool ticked = true) => new(Messages.KindFile, path, null, cls, ticked);
    public static LeftoverItem Folder(string path, string cls = Messages.ClassSafe, bool ticked = true) => new(Messages.KindFolder, path, null, cls, ticked);
    public static LeftoverItem Key(string path, int view = 64, string cls = Messages.ClassSafe, bool ticked = true) => new(Messages.KindRegistryKey, path, view, cls, ticked);

    public Task<AdminReply> Run(params LeftoverItem[] items) => RunFor(Record.Id, items);
    public Task<AdminReply> RunFor(Guid recordId, params LeftoverItem[] items) =>
        Actions.RunAsync(new AdminRequest(Guid.NewGuid(), Messages.DeleteLeftovers, RecordId: recordId, Items: items), CancellationToken.None);
    public Task<AdminReply> Drop(Guid? recordId = null) =>
        Actions.RunAsync(new AdminRequest(Guid.NewGuid(), Messages.DropRecord, RecordId: recordId ?? Record.Id), CancellationToken.None);
}
