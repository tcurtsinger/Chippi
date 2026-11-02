// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Chippi.Features.AgentUsage.Readers;

/// <summary>One run of an agent's own program for a check: JSON lines in and out.</summary>
internal interface IAgentConversation : IAsyncDisposable
{
    Task SendAsync(string line, CancellationToken ct);

    /// <summary>The next line it writes, or null once its output has closed (it exited, or the check timed out).</summary>
    Task<string?> ReadLineAsync(CancellationToken ct);
}

/// <summary>The agents' own programs: Claude Code's <c>claude</c> and Codex's <c>codex</c>. Tests pass a scripted fake.</summary>
internal interface IAgentPrograms
{
    /// <summary>Whether the agent's program is on this PC.</summary>
    bool IsInstalled(AgentKind agent);

    /// <summary>Starts it for one check, with <paramref name="arguments"/>; null when it isn't on this PC.</summary>
    IAgentConversation? Start(AgentKind agent, IReadOnlyList<string> arguments, string workingDirectory);

    /// <summary>Whether <paramref name="processId"/> is one of these checks, still running: its session isn't one of yours.</summary>
    bool IsOwn(int processId);
}

/// <summary>What one check cost: wall time, the program's CPU time and its peak working set.</summary>
internal sealed record ProgramCost(TimeSpan Wall, TimeSpan Cpu, long PeakBytes)
{
    public override string ToString() =>
        $"{Wall.TotalSeconds:0.0} s, CPU {Cpu.TotalMilliseconds:0} ms, peak {PeakBytes / (1024.0 * 1024):0} MB";
}

/// <summary>
/// Finds and starts the agents' programs. Each runs with no window and no console, its standard input UTF-8 without a
/// byte-order mark (Codex's server can't parse one), and none of a Claude Code session's own variables, so a Chippi
/// started from inside a session asks as one started from Explorer does. A program that outlives its check is ended.
/// </summary>
internal sealed class WindowsAgentPrograms(Action<AgentKind, ProgramCost>? costed = null) : IAgentPrograms
{
    private static readonly TimeSpan ExitWait = TimeSpan.FromSeconds(2);
    private readonly ConcurrentDictionary<int, byte> _running = new();

    public bool IsInstalled(AgentKind agent) => Locate(agent) is not null;

    public bool IsOwn(int processId) => _running.ContainsKey(processId);

    public IAgentConversation? Start(AgentKind agent, IReadOnlyList<string> arguments, string workingDirectory)
    {
        if (Locate(agent) is not { } exe)
            return null;
        var start = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false),
            StandardOutputEncoding = new UTF8Encoding(false),
            StandardErrorEncoding = new UTF8Encoding(false),
            WorkingDirectory = workingDirectory,
        };
        foreach (string argument in arguments)
            start.ArgumentList.Add(argument);
        foreach (string name in start.Environment.Keys.ToList())
            if (IsSessionVariable(name))
                start.Environment.Remove(name);
        var process = Process.Start(start) ?? throw new InvalidOperationException($"{exe} didn't start");
        _running[process.Id] = 0;
        return new Conversation(process, () => _running.TryRemove(process.Id, out _), cost => costed?.Invoke(agent, cost));
    }

    /// <summary>A Claude Code session's own variables (<c>CLAUDECODE</c>, <c>CLAUDE_CODE_*</c>…), which make a child expect its host's sign-in.</summary>
    internal static bool IsSessionVariable(string name) =>
        name.Equals("CLAUDECODE", StringComparison.OrdinalIgnoreCase)
        || name.Equals("CLAUDE_PID", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("CLAUDE_CODE_", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("CLAUDE_AGENT_SDK_", StringComparison.OrdinalIgnoreCase);

    /// <summary>The agent's program: on the PATH or where its installer puts it. Codex's npm wrapper is passed by for its own codex.exe.</summary>
    internal static string? Locate(AgentKind agent)
    {
        try { return Find(agent); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; } // a folder it may not read
    }

    private static string? Find(AgentKind agent)
    {
        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (agent == AgentKind.Claude)
            return OnPath("claude.exe") ?? Existing(Path.Combine(profile, ".local", "bin", "claude.exe"));
        if (OnPath("codex.exe") is { } native)
            return native;
        // npm: codex.cmd beside node_modules\@openai\codex, whose platform package holds the real program.
        foreach (string? shim in new[] { OnPath("codex.cmd"), OnPath("codex.ps1"), Existing(Path.Combine(profile, ".local", "codex.cmd")) })
        {
            if (shim is null || Path.GetDirectoryName(shim) is not { } dir)
                continue;
            string arch = RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "arm64" : "x64";
            string vendor = Path.Combine(dir, "node_modules", "@openai", "codex", "node_modules", "@openai", $"codex-win32-{arch}", "vendor");
            if (Directory.Exists(vendor))
                foreach (string triple in Directory.EnumerateDirectories(vendor))
                    if (Existing(Path.Combine(triple, "bin", "codex.exe")) is { } found)
                        return found;
        }
        // The Codex app keeps its own copy, one folder per version.
        string app = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenAI", "Codex", "bin");
        return Directory.Exists(app)
            ? Directory.EnumerateFiles(app, "codex.exe", SearchOption.AllDirectories).OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault()
            : null;
    }

    private static string? OnPath(string file)
    {
        foreach (string dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                if (Existing(Path.Combine(dir.Trim().Trim('"'), file)) is { } found)
                    return found;
            }
            catch (ArgumentException) { } // a PATH entry with characters a path can't have
        }
        return null;
    }

    private static string? Existing(string path) => File.Exists(path) ? path : null;

    private sealed class Conversation : IAgentConversation
    {
        private readonly Process _process;
        private readonly Action _ended;
        private readonly Action<ProgramCost> _costed;
        private readonly Stopwatch _wall = Stopwatch.StartNew();
        private long _peak;
        private int _disposed;

        public Conversation(Process process, Action ended, Action<ProgramCost> costed)
        {
            _process = process;
            _ended = ended;
            _costed = costed;
            // Its errors are drained so a full pipe never blocks it; nothing in them is kept.
            process.ErrorDataReceived += (_, _) => { };
            process.BeginErrorReadLine();
        }

        public async Task SendAsync(string line, CancellationToken ct)
        {
            await _process.StandardInput.WriteLineAsync(line.AsMemory(), ct).ConfigureAwait(false);
            await _process.StandardInput.FlushAsync(ct).ConfigureAwait(false);
        }

        public async Task<string?> ReadLineAsync(CancellationToken ct)
        {
            // A timed-out check ends the program, which closes its output and ends the read.
            using var end = ct.Register(Kill);
            try { return await _process.StandardOutput.ReadLineAsync(CancellationToken.None).ConfigureAwait(false); }
            catch (Exception) when (ct.IsCancellationRequested) { return null; }
            finally { Sample(); }
        }

        private void Sample()
        {
            try
            {
                _process.Refresh();
                _peak = Math.Max(_peak, _process.PeakWorkingSet64);
            }
            catch (InvalidOperationException) { } // it has exited
        }

        private void Kill()
        {
            try { _process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { } // it has exited
            catch (System.ComponentModel.Win32Exception) { }
        }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;
            Sample();
            try { _process.StandardInput.Close(); } // asks it to finish
            catch (IOException) { }
            using (var wait = new CancellationTokenSource(ExitWait))
            {
                try { await _process.WaitForExitAsync(wait.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) { Kill(); }
            }
            TimeSpan cpu = TimeSpan.Zero;
            try
            {
                _process.WaitForExit(500);
                cpu = _process.TotalProcessorTime;
            }
            catch (InvalidOperationException) { }
            _ended();
            _costed(new ProgramCost(_wall.Elapsed, cpu, _peak));
            _process.Dispose();
        }
    }
}
