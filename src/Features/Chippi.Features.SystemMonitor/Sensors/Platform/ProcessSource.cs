// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace Chippi.Features.SystemMonitor.Sensors.Platform;

/// <summary>
/// Every process in one call (NtQuerySystemInformation's process list): CPU time and the active private
/// working set, the figures Task Manager's Processes tab shows. An app's name is its program file's
/// description, read once per program. Read only while a detail page lists top apps.
/// </summary>
internal sealed class ProcessSource : IProcessSource
{
    private const int SystemProcessInformation = 5;
    private const uint InfoLengthMismatch = 0xC0000004;
    private const uint QueryLimitedInformation = 0x1000;

    // The kernel's own entries, which aren't apps.
    private static readonly HashSet<string> Kernel = new(StringComparer.OrdinalIgnoreCase) { "Registry", "Memory Compression", "Secure System", "System", "Idle" };

    private readonly Dictionary<int, (long Created, long Time)> _last = [];
    private readonly Dictionary<string, (string Name, string? Path, long RetryAt)> _names = new(StringComparer.OrdinalIgnoreCase);
    private byte[] _buffer = new byte[512 * 1024];
    private long _lastRead;

    public unsafe IReadOnlyList<ProcessReading> Read()
    {
        if (!Environment.Is64BitProcess)
            throw new PlatformNotSupportedException("The process list layout is read for 64-bit Windows.");
        long now = Stopwatch.GetTimestamp();
        // CPU is a rate over the time since the last read; after a long pause it starts over.
        double elapsed = _lastRead == 0 ? 0 : Stopwatch.GetElapsedTime(_lastRead, now).TotalSeconds;
        bool rates = elapsed > 0 && elapsed <= RateClock.StaleAfter.TotalSeconds;
        _lastRead = now;
        double capacity = elapsed * 10_000_000 * Environment.ProcessorCount; // 100ns units across every processor

        var readings = new List<ProcessReading>(_last.Count + 16);
        var seen = new Dictionary<int, (long, long)>(_last.Count + 16);
        while (true)
        {
            fixed (byte* buffer = _buffer)
            {
                uint status = NtQuerySystemInformation(SystemProcessInformation, buffer, _buffer.Length, out int needed);
                if (status == InfoLengthMismatch)
                {
                    _buffer = new byte[Math.Max(needed, _buffer.Length) + 64 * 1024];
                    continue;
                }
                if (status != 0)
                    throw new InvalidOperationException($"NtQuerySystemInformation failed: 0x{status:X8}");
                long tick = Environment.TickCount64;
                for (int offset = 0; ;)
                {
                    // SYSTEM_PROCESS_INFORMATION on 64-bit Windows.
                    byte* entry = buffer + offset;
                    uint next = *(uint*)entry;
                    long privateBytes = *(long*)(entry + 8);
                    long created = *(long*)(entry + 32);
                    long time = *(long*)(entry + 40) + *(long*)(entry + 48);
                    ushort length = *(ushort*)(entry + 56);
                    char* image = *(char**)(entry + 64);
                    int id = (int)*(nint*)(entry + 80);
                    string file = length > 0 && image != null ? new string(image, 0, length / 2) : "";
                    if (id > 4 && file.Length > 0 && !Kernel.Contains(file))
                    {
                        double? cpu = null;
                        if (rates && _last.TryGetValue(id, out var before) && before.Created == created)
                            cpu = Math.Clamp((time - before.Time) / capacity * 100, 0, 100);
                        seen[id] = (created, time);
                        var (name, path) = Name(id, file, tick);
                        readings.Add(new ProcessReading(id, file.ToLowerInvariant(), name, path, cpu, (ulong)Math.Max(privateBytes, 0)));
                    }
                    if (next == 0)
                        break;
                    offset += (int)next;
                }
            }
            break;
        }
        _last.Clear();
        foreach (var (id, value) in seen)
            _last[id] = value;
        return readings;
    }

    public void Dispose() { }

    // Once per program; a program whose file can't be read (a protected process) uses its file name,
    // and tries again a minute later with whichever of its processes is running then.
    private (string Name, string? Path) Name(int id, string file, long tick)
    {
        if (_names.TryGetValue(file, out var known) && (known.Path is not null || tick < known.RetryAt))
            return (known.Name, known.Path);
        string? path = PathOf(id);
        string name = Path.GetFileNameWithoutExtension(file);
        if (path is not null)
        {
            try
            {
                var info = FileVersionInfo.GetVersionInfo(path);
                name = FirstOf(info.FileDescription, info.ProductName) ?? name;
            }
            catch (Exception) { /* unreadable version info: the file's name */ }
        }
        _names[file] = (name, path, tick + 60_000);
        return (name, path);
    }

    private static string? FirstOf(params string?[] names) =>
        names.Select(n => n?.Trim()).FirstOrDefault(n => !string.IsNullOrEmpty(n));

    private static unsafe string? PathOf(int id)
    {
        nint process = OpenProcess(QueryLimitedInformation, false, id);
        if (process == 0)
            return null;
        try
        {
            char* text = stackalloc char[1024];
            int size = 1024;
            return QueryFullProcessImageNameW(process, 0, text, ref size) ? new string(text, 0, size) : null;
        }
        finally
        {
            CloseHandle(process);
        }
    }

    [DllImport("ntdll.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern unsafe uint NtQuerySystemInformation(int infoClass, byte* buffer, int length, out int needed);

    [DllImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern nint OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, int id);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern unsafe bool QueryFullProcessImageNameW(nint process, int flags, char* name, ref int size);

    [DllImport("kernel32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(nint handle);
}
