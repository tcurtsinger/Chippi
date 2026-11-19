// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors
using Chippi.Elevated.Protocol;

namespace Chippi.Elevated.Tests;

/// <summary>Everything Uninstall touches, faked: nothing here reads or writes the real registry, the real file system's security or any process.</summary>
internal sealed class FakeRegistry : IUninstallRegistry
{
    public Dictionary<(int View, string Key), UninstallKey> Keys { get; } = [];
    public UninstallKey? Read(int view, string keyName) => Keys.GetValueOrDefault((view, keyName));
    public bool Exists(int view, string keyName) => Keys.ContainsKey((view, keyName));
}

internal sealed class FakeRecords : IRecordStore
{
    public bool Usable = true;
    public List<UninstallRecord> All { get; } = [];
    public bool Prepare() => Usable;
    public IReadOnlyList<UninstallRecord> List() => [.. All];
    public UninstallRecord? Get(Guid id) => All.FirstOrDefault(r => r.Id == id);
    public void Write(UninstallRecord record) => All.Add(record);
    public void Delete(Guid id) => All.RemoveAll(r => r.Id == id);
}

internal sealed class FakeHost : IUninstallHost
{
    public Dictionary<string, string> Variables { get; } = new(StringComparer.OrdinalIgnoreCase) { ["ProgramFiles"] = @"C:\Program Files" };
    public HashSet<string> Files { get; } = new(StringComparer.OrdinalIgnoreCase);
    public string Expand(string text)
    {
        foreach (var (name, value) in Variables) text = text.Replace($"%{name}%", value, StringComparison.OrdinalIgnoreCase);
        return text;
    }
    public bool FileExists(string path) => Files.Contains(path);
    public string SystemDirectory => @"C:\Windows\System32";
}

/// <summary>A file system whose every file and folder says what Windows would: owner, link, and the rights the standard token is granted.</summary>
internal sealed class FakeProbe : IFsProbe
{
    public const string Admins = "S-1-5-32-544", System = "S-1-5-18", User = "S-1-5-21-1-2-3-1001";
    public sealed record Spec(string Owner = Admins, uint Granted = 0, bool Reparse = false, bool Matches = true);
    public Dictionary<string, Spec> Nodes { get; } = new(StringComparer.OrdinalIgnoreCase);
    public int Live;
    public List<string> Opened { get; } = [];

    /// <summary>The file and each folder above it, all owned by Administrators and granting the standard token nothing.</summary>
    public FakeProbe Trusting(string file)
    {
        Nodes[file] = new();
        for (var folder = Path.GetDirectoryName(file); !string.IsNullOrEmpty(folder); folder = Path.GetDirectoryName(folder)) Nodes[folder] = new();
        return this;
    }

    public IHeldNode? Open(string path, bool file)
    {
        Opened.Add(path);
        if (!Nodes.TryGetValue(path, out var spec)) return null;
        Interlocked.Increment(ref Live);
        return new Node(this, spec);
    }

    private sealed class Node(FakeProbe owner, Spec spec) : IHeldNode
    {
        private int _disposed;
        public bool IsReparsePoint => spec.Reparse;
        public bool PathMatches => spec.Matches;
        public string OwnerSid => spec.Owner;
        public uint StandardGranted => spec.Granted;
        public void Dispose() { if (Interlocked.Exchange(ref _disposed, 1) == 0) Interlocked.Decrement(ref owner.Live); }
    }
}

internal sealed class FakeLauncher(FakeProbe probe, FakeRegistry? registry = null) : IProcessLauncher
{
    public int ExitCode;
    public Exception? StartError;
    public (int View, string Key)? RemoveKeyWhenDone;
    public List<(string Application, string CommandLine, string Folder)> Started { get; } = [];
    public int HeldAtStart = -1;
    public int HeldWhileRunning = -1;
    /// <summary>Runs at the moment the process is started, with the file and folders still held.</summary>
    public Action? AtStart;

    public ILaunchedJob Start(string application, string commandLine, string workingDirectory)
    {
        if (StartError is not null) throw StartError;
        HeldAtStart = probe.Live;
        Started.Add((application, commandLine, workingDirectory));
        AtStart?.Invoke();
        return new Job(this);
    }

    private sealed class Job(FakeLauncher launcher) : ILaunchedJob
    {
        public Task<int> WaitAsync(CancellationToken ct)
        {
            launcher.HeldWhileRunning = launcher.HeldAtStart < 0 ? -1 : launcher.Probe.Live;
            if (launcher.RemoveKeyWhenDone is { } key) launcher.Registry?.Keys.Remove(key);
            return Task.FromResult(launcher.ExitCode);
        }
        public void Dispose() { }
    }
    private FakeProbe Probe => probe;
    private FakeRegistry? Registry => registry;
}

internal sealed class StepClock(DateTimeOffset start) : TimeProvider
{
    public DateTimeOffset Now = start;
    public override DateTimeOffset GetUtcNow() => Now;
}
