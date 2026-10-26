// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.IO;
using System.Text.Json;
using Chippi.Audio;
using Chippi.Audio.Processing;
using Chippi.Core.Island;
using Chippi.Core.Modules;
using Chippi.Core.Notifications;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Chippi.Features.Sound.Processing;

/// <summary>
/// Mic processing (Hopper's, brief §2): cleans the real mic through RNNoise, a gate and auto gain into
/// VB-CABLE, so apps hear it as CABLE Output. The engine side is <see cref="MicPipelineHost"/>; this
/// module owns the settings, the recovery marker, the "Stopped" notice and the Install Driver row.
/// </summary>
public sealed class MicProcessingModule : IFeatureModule
{
    public const string Id = "mic-processing";
    internal const string MarkerFile = "processing-owner.json";
    private static readonly TimeSpan SaveDelay = TimeSpan.FromMilliseconds(500); // stage edits persist after a pause

    private readonly SemaphoreSlim _gate = new(1);
    private readonly Func<IReadOnlyList<PriorityEntry>> _pinned;
    private readonly string _dataDirectory;
    private readonly IDriverSetup _setup;
    private readonly MicSettings _live = new();
    private AudioService? _audio;
    private IAsyncDisposable? _lease;
    private ModuleContext? _context;
    private IIslandTile? _tile;
    private Timer? _saveTimer;
    private ProcessingState _lastState = ProcessingState.Off;
    private volatile bool _stopping;

    public MicProcessingModule() : this(null) { }

    /// <param name="pinned">Audio routing's microphone list, read at every reconcile for the "Highest priority" source.</param>
    /// <param name="dataDirectory">This module's data folder; the Install Driver row needs it before the module starts.</param>
    public MicProcessingModule(AudioService? audio, Func<IReadOnlyList<PriorityEntry>>? pinned = null, string? dataDirectory = null, IDriverSetup? setup = null)
    {
        _audio = audio;
        _pinned = pinned ?? (() => []);
        _dataDirectory = dataDirectory ?? Path.Combine(ModuleServices.DefaultDataRoot, Id);
        _setup = setup ?? new ElevatedDriverSetup();
        Descriptor = new(Id, "Mic processing", "Noise suppression, gate and auto gain for the mic", FeatureGroup.Sound)
        {
            Icon = "Mic",
            Requires = [Capability.VbCable],
            Presets = PresetMembership.Audio | PresetMembership.PowerUser,
            Requirement = () => new VbCableRequirement(Audio(NullLogger.Instance), _dataDirectory,
                () => VbCablePackage.Prepare(VbCablePackage.VendorFolder, Path.Combine(_dataDirectory, "vb-cable")),
                _setup, VbCableRequirement.OpenWindowsSoundSettings),
        };
    }

    public FeatureDescriptor Descriptor { get; }

    public ProcessingSettings Settings { get; private set; } = new();

    public ProcessingStatus Status => _audio?.Snapshot.Processing ?? ProcessingStatus.Off;

    public AudioSnapshot Snapshot => _audio?.Snapshot ?? AudioSnapshot.Empty;

    public string? Error { get; private set; }

    /// <summary>Raised after settings, status or snapshot changes, on the thread that changed them.</summary>
    public event Action? Changed;

    public async Task StartAsync(ModuleContext context, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            _context = context;
            _stopping = false;
            Settings = context.Settings.Get<ProcessingSettings>().Normalized();
            Settings.ApplyTo(_live);
            var audio = Audio(context.Logger);
            _lease = audio.Acquire();
            await RecoverAsync(audio, context);
            context.Notifier.Declare([new("stopped", "Stopped", NoticePlaces.Windows | NoticePlaces.Island)]);
            _tile = context.Ui.AddIslandTile(SoundTile.Section(30, SoundTile.Microphone, () => new ProcessingQuickView(this)) with { StatusPriority = 10 });
            context.Ui.SetSettingsPage(_ => new ProcessingPage(this));
            audio.SnapshotChanged += OnSnapshot;
            _lastState = ProcessingState.Off;
            await ApplyAsync();
        }
        catch
        {
            await ReleaseAsync();
            _context = null;
            throw;
        }
        finally { _gate.Release(); }
        Changed?.Invoke();
    }

    public async Task StopAsync(CancellationToken ct)
    {
        _stopping = true;
        if (_audio is not null)
            _audio.SnapshotChanged -= OnSnapshot;
        await _gate.WaitAsync(CancellationToken.None);
        try
        {
            FlushSave();
            await ReleaseAsync();
            _context = null;
            _tile = null;
        }
        finally { _gate.Release(); }
        Changed?.Invoke();
    }

    /// <summary>Turns processing on or off. Saved at once; status follows from the engine.</summary>
    public Task SetEnabledAsync(bool on) => EditAsync(s => s with { Enabled = on }, apply: true, saveNow: true);

    public Task SetDefaultMicAsync(bool on) => EditAsync(s => s with { SetDefault = on }, apply: true, saveNow: true);

    public Task SetSourceAsync(string source) => EditAsync(s => s with { Source = source }, apply: true, saveNow: true);

    /// <summary>A stage switch or level: heard at once, saved after a 500 ms pause (Hopper's).</summary>
    public Task SetStagesAsync(Func<ProcessingSettings, ProcessingSettings> edit) => EditAsync(edit, apply: false, saveNow: false);

    /// <summary>Real mics the user can pick as the source, VB-CABLE left out.</summary>
    public IReadOnlyList<AudioEndpoint> Sources => Snapshot.Active(AudioFlow.Capture).Where(e => !VbCable.IsCable(e)).ToList();

    public string SourceName => Snapshot.Find(Status.SourceId ?? "")?.Name ?? "No microphone";

    /// <summary>One line for the status: "Live", "Off", "Stopped · No microphone".</summary>
    public string StatusText => Status.State switch
    {
        ProcessingState.Live => "Live",
        ProcessingState.Off => "Off",
        _ => "Stopped · " + Status.Reason switch
        {
            StopReason.NoDriver => "No driver",
            StopReason.DriverDisabled => "Driver disabled",
            StopReason.NoMicrophone => "No microphone",
            _ => Status.Error ?? "Start failed",
        },
    };

    public IDisposable WatchMeters() => Audio(NullLogger.Instance).WatchMeters([], []);

    public event EventHandler<MeterReadings>? Meters
    {
        add => Audio(NullLogger.Instance).MetersChanged += value;
        remove => Audio(NullLogger.Instance).MetersChanged -= value;
    }

    public void OpenSettings() => _context?.OpenSettings(null);

    private AudioService Audio(ILogger logger) => _audio ??= SoundAudio.Get(logger);

    private async Task EditAsync(Func<ProcessingSettings, ProcessingSettings> edit, bool apply, bool saveNow)
    {
        await _gate.WaitAsync();
        try
        {
            if (_context is null || _stopping)
                throw new AudioFailure("Mic processing uninstalled");
            Settings = edit(Settings).Normalized();
            Settings.ApplyTo(_live);
            if (saveNow)
                FlushSave();
            else
                ScheduleSave();
            if (apply)
                await ApplyAsync();
            Error = null;
        }
        catch (Exception ex)
        {
            Error = AudioErrors.Reason(ex);
            throw;
        }
        finally
        {
            _gate.Release();
            Changed?.Invoke();
        }
    }

    // Caller holds _gate.
    private async Task ApplyAsync()
    {
        var status = await Audio(NullLogger.Instance).SetProcessingAsync(Settings.Enabled ? Request() : null);
        UpdateTile(status);
    }

    private ProcessingRequest Request()
    {
        var source = Settings.Source;
        RoleTarget pick = source == ProcessingSettings.PinnedSource
            ? active => PriorityPolicy.Resolve(_pinned(), active) ?? active.FirstOrDefault()?.Id
            : active => active.FirstOrDefault(e => string.Equals(e.Id, source, StringComparison.OrdinalIgnoreCase))?.Id;
        return new ProcessingRequest(pick, _live, Settings.SetDefault, SaveMarker);
    }

    // Engine thread. Written before the capture roles change, deleted when they're given back.
    private void SaveMarker(ProcessingMarker? marker)
    {
        var path = Path.Combine(_dataDirectory, MarkerFile);
        try
        {
            if (marker is null)
            {
                File.Delete(path);
                return;
            }
            Directory.CreateDirectory(_dataDirectory);
            var temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(marker));
            File.Move(temp, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _context?.Logger.LogWarning(ex, "Writing the mic processing recovery marker failed");
        }
    }

    /// <summary>A marker left by a crash: put back the defaults it recorded, then drop it (architecture §3).</summary>
    private async Task RecoverAsync(AudioService audio, ModuleContext context)
    {
        var path = Path.Combine(_dataDirectory, MarkerFile);
        if (ReadMarker(path) is not { } marker)
            return;
        try
        {
            // No real mic yet: the engine keeps the marker, retries as devices change, and deletes the
            // file through SaveMarker once a real mic is back.
            if (!await audio.RecoverProcessingAsync(marker, SaveMarker))
            {
                context.Logger.LogWarning("The cable is still the default mic and no real mic is active to put back");
                return;
            }
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            context.Logger.LogWarning(ex, "Mic processing recovery failed");
        }
    }

    /// <summary>
    /// App uninstall (Velopack hook): if a crash left the cable as the default mic and Chippi never ran
    /// again, put back what processing replaced. Bounded, and never throws into the uninstall.
    /// </summary>
    public static void RestoreOnUninstall(string dataDirectory)
    {
        var path = Path.Combine(dataDirectory, MarkerFile);
        if (ReadMarker(path) is not { } marker)
            return;
        var audio = new AudioService(() => new CoreAudioDeviceSystem(NullLogger.Instance), null);
        var lease = audio.Acquire();
        try
        {
            var recover = audio.RecoverProcessingAsync(marker);
            if (recover.Wait(TimeSpan.FromSeconds(10)) && recover.Result)
                File.Delete(path);
        }
        catch (Exception ex) when (ex is AggregateException or IOException or UnauthorizedAccessException) { }
        finally
        {
            lease.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(5));
        }
    }

    internal static ProcessingMarker? ReadMarker(string path)
    {
        try { return File.Exists(path) ? JsonSerializer.Deserialize<ProcessingMarker>(File.ReadAllText(path)) : null; }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { return null; }
    }

    private void OnSnapshot(object? sender, AudioSnapshot snapshot)
    {
        if (_stopping || _context is null)
            return;
        var status = snapshot.Processing;
        if (_lastState == ProcessingState.Live && status.State == ProcessingState.Stopped && Settings.Enabled)
            _context.Notifier.Show(new("stopped", "Mic processing stopped") { Body = StatusText["Stopped · ".Length..], IslandKind = IslandActivityKind.Status });
        UpdateTile(status);
        Changed?.Invoke();
    }

    private void UpdateTile(ProcessingStatus status)
    {
        _lastState = status.State;
        // The Sound tile shows the output; clean-up speaks up only when it stopped.
        _tile?.SetStatus(status.State == ProcessingState.Stopped ? "Clean-up stopped" : "");
    }

    private void ScheduleSave()
    {
        _saveTimer ??= new Timer(_ => FlushSave());
        _saveTimer.Change(SaveDelay, Timeout.InfiniteTimeSpan);
    }

    private void FlushSave()
    {
        _saveTimer?.Change(Timeout.Infinite, Timeout.Infinite);
        _context?.Settings.Save(Settings);
    }

    private async Task ReleaseAsync()
    {
        var lease = _lease;
        _lease = null;
        if (lease is null)
            return;
        try { await _audio!.SetProcessingAsync(null); } // stops the pipeline and restores the mic
        catch (InvalidOperationException) { }
        finally { await lease.DisposeAsync(); }
    }
}
