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
    private bool _installing;
    private bool _disposed;

    /// <param name="prepare">Checks and extracts the package, returning the setup's path (<see cref="VbCablePackage.Prepare"/>).</param>
    public VbCableRequirement(AudioService audio, string dataDirectory, Func<string> prepare, IDriverSetup setup, Action openSoundSettings)
    {
        _audio = audio;
        _dataDirectory = dataDirectory;
        _prepare = prepare;
        _setup = setup;
        _openSoundSettings = openSoundSettings;
        _lease = audio.Acquire();
        try { audio.RefreshAsync().GetAwaiter().GetResult(); } // State is current as soon as it exists
        catch (InvalidOperationException) { } // audio didn't start: reads as Missing
        audio.SnapshotChanged += OnSnapshot;
        State = Compute();
    }

    public RequirementState State { get; private set; }

    public event EventHandler? StateChanged;

    /// <summary>The device system's boot moment, to tell "restart pending" from "restarted and still missing".</summary>
    internal static Func<DateTimeOffset> BootTime { get; set; } = () => DateTimeOffset.UtcNow - TimeSpan.FromMilliseconds(Environment.TickCount64);

    public async Task InstallAsync(CancellationToken ct)
    {
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
        if (File.Exists(pending)
            && DateTimeOffset.TryParse(File.ReadAllText(pending), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var bootAtSetup)
            && (BootTime() - bootAtSetup).Duration() < TimeSpan.FromMinutes(1))
            return RequirementState.RestartNeeded;
        TryDelete(pending); // restarted and still no cable: back to Install Driver
        return RequirementState.Missing;
    }

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
