// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.IO;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using Chippi.Audio;
using Chippi.Core.Modules;

namespace Chippi.Features.Sound.Processing;

/// <summary>
/// Mic processing's Add features row (brief §2, Install Driver): Install Driver while VB-CABLE is
/// missing, busy while its setup runs, Restart to finish until the devices appear, Driver disabled
/// when they're there but off, and the normal switch once a verified pair is ready. The state comes
/// from the devices, never from setup's exit code, and driver presence never turns processing on.
/// </summary>
public sealed class VbCableRequirement : IFeatureRequirement
{
    private const string PendingFile = "restart-pending.txt";

    private readonly AudioService _audio;
    private readonly string _dataDirectory;
    private readonly Func<string> _prepare;
    private readonly IDriverSetup _setup;
    private readonly Action _openSoundSettings;
    private readonly IAsyncDisposable _lease;
    private readonly Func<string, string> _readMarker;
    private bool _installing;
    private bool _disposed;

    /// <param name="prepare">Checks and extracts the package, returning the setup's path (<see cref="VbCablePackage.Prepare"/>).</param>
    public VbCableRequirement(AudioService audio, string dataDirectory, Func<string> prepare, IDriverSetup setup, Action openSoundSettings)
        : this(audio, dataDirectory, prepare, setup, openSoundSettings, ReadMarker)
    {
    }

    /// <param name="readMarker">Reads the restart marker at a path; a test's stands in for another scan clearing it first.</param>
    internal VbCableRequirement(AudioService audio, string dataDirectory, Func<string> prepare, IDriverSetup setup, Action openSoundSettings,
        Func<string, string> readMarker)
    {
        _audio = audio;
        _dataDirectory = dataDirectory;
        _prepare = prepare;
        _setup = setup;
        _openSoundSettings = openSoundSettings;
        _readMarker = readMarker;
        _lease = audio.Acquire();
        try
        {
            audio.SnapshotChanged += OnSnapshot;
            State = Compute();
        }
        catch
        {
            audio.SnapshotChanged -= OnSnapshot;
            _ = _lease.DisposeAsync().AsTask();
            throw;
        }
        _ = RefreshFirstAsync(); // the first full scan can take a while; State follows when it lands
    }

    private async Task RefreshFirstAsync()
    {
        try { await _audio.RefreshAsync(); }
        catch (InvalidOperationException) { return; } // audio didn't start (an AudioFailure too): reads as Missing
        if (!_disposed)
            Update();
    }

    public RequirementState State { get; private set; }

    public event EventHandler? StateChanged;

    /// <summary>The device system's boot moment, to tell "restart pending" from "restarted and still missing".</summary>
    internal static Func<DateTimeOffset> BootTime { get; set; } = () => DateTimeOffset.UtcNow - TimeSpan.FromMilliseconds(Environment.TickCount64);

    public async Task InstallAsync(CancellationToken ct)
    {
        if (State != RequirementState.Missing || _installing)
            return;
        // Missing may only mean the first scan hasn't landed; never start setup on a stale read.
        try { await _audio.RefreshAsync(); }
        catch (InvalidOperationException) { } // audio didn't start: still Missing
        Update();
        if (State != RequirementState.Missing || _installing)
            return;
        _installing = true;
        Update();
        try
        {
            var setup = await Task.Run(_prepare, ct);
            int? exit;
            try { exit = await _setup.RunAsync(setup, ct); }
            catch (Win32Exception ex) { throw new AudioFailure("Driver setup failed", ex); }
            finally { VbCablePackage.Release(setup); }
            if (exit is not null)
            {
                // The exit code is logged by the caller, not trusted: the devices decide.
                Directory.CreateDirectory(_dataDirectory);
                File.WriteAllText(Path.Combine(_dataDirectory, PendingFile), BootTime().ToString("O", CultureInfo.InvariantCulture));
            }
        }
        finally
        {
            _installing = false;
            if (!_disposed)
            {
                try { await _audio.RefreshAsync(); }
                catch (InvalidOperationException) { }
            }
            Update();
        }
    }

    public void OpenSettings() => _openSoundSettings();

    /// <summary>Windows' list of sound devices, where a disabled one is turned back on.</summary>
    public static void OpenWindowsSoundSettings() =>
        Process.Start(new ProcessStartInfo("ms-settings:sound-devices") { UseShellExecute = true })?.Dispose();

    private void OnSnapshot(object? sender, AudioSnapshot snapshot) => Update();

    private void Update()
    {
        var state = Compute();
        if (state == State)
            return;
        State = state;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private RequirementState Compute()
    {
        if (_installing)
            return RequirementState.Installing;
        var cable = VbCable.Find(_audio.Snapshot.Endpoints).State;
        var pending = Path.Combine(_dataDirectory, PendingFile);
        if (cable != CableState.Missing)
        {
            TryDelete(pending);
            return cable == CableState.Disabled ? RequirementState.Disabled : RequirementState.Ready;
        }
        if (File.Exists(pending))
        {
            string marker;
            try { marker = _readMarker(pending); }
            // Compute runs on several threads (the constructor, the audio thread's snapshots, the scans'
            // continuations) and in every requirement over this folder, so another one can clear a stale
            // marker between the check above and this read. Gone means no restart is pending.
            catch (FileNotFoundException) { return RequirementState.Missing; }
            catch (DirectoryNotFoundException) { return RequirementState.Missing; }
            // Windows' answer for a file whose delete is still under way: going is gone.
            catch (UnauthorizedAccessException) { return RequirementState.Missing; }
            // There but unreadable. ReadMarker shares reads and deletes, so in Chippi only InstallAsync writing
            // a fresh marker blocks it, and then a restart is pending. Anything else holding it doesn't say the
            // PC restarted either: the marker stays and the next change reads it again.
            catch (IOException) { return RequirementState.RestartNeeded; }
            if (DateTimeOffset.TryParse(marker, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var bootAtSetup)
                && (BootTime() - bootAtSetup).Duration() < TimeSpan.FromMinutes(1))
                return RequirementState.RestartNeeded;
        }
        TryDelete(pending); // restarted and still no cable: back to Install Driver
        return RequirementState.Missing;
    }

    internal static string ReadMarker(string path)
    {
        using var reader = new StreamReader(OpenMarker(path));
        return reader.ReadToEnd();
    }

    /// <summary>
    /// The marker, open to read. It shares Delete as well as Read, so a read and another scan's
    /// <see cref="TryDelete"/> never refuse each other, and not Write, so a marker half written by setup is never read.
    /// </summary>
    internal static FileStream OpenMarker(string path) =>
        new(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _audio.SnapshotChanged -= OnSnapshot;
        _ = _lease.DisposeAsync().AsTask();
    }
}
