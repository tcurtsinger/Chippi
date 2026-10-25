// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Microsoft.Extensions.Logging;

namespace Chippi.Audio.Processing;

public enum ProcessingState { Off, Live, Stopped }

public enum StopReason
{
    None,

    /// <summary>VB-CABLE isn't installed.</summary>
    NoDriver,

    /// <summary>VB-CABLE is installed but its endpoints are disabled.</summary>
    DriverDisabled,

    /// <summary>No real microphone to clean.</summary>
    NoMicrophone,

    /// <summary>The pipeline couldn't start or stopped by itself. It retries when its mic changes or returns.</summary>
    Failed,
}

/// <summary>Where Mic processing actually is. Views show this, never the switch alone (no false On).</summary>
public sealed record ProcessingStatus(ProcessingState State, StopReason Reason = StopReason.None, string? SourceId = null, string? Error = null)
{
    public static ProcessingStatus Off { get; } = new(ProcessingState.Off);
}

/// <summary>
/// The capture defaults Mic processing replaced when it took them (architecture §3, recovery): the
/// cable it set and what each role pointed at before. Written before the roles are taken.
/// </summary>
public sealed record ProcessingMarker(string CableCaptureId, IReadOnlyDictionary<AudioRole, string> Replaced);

/// <summary>What Mic processing asks the engine for.</summary>
/// <param name="Source">Picks the real mic from the active capture endpoints, VB-CABLE already left out.</param>
/// <param name="Settings">Live DSP settings, shared with the running pipeline.</param>
/// <param name="SetDefault">Make CABLE Output the default mic for all three roles while live.</param>
/// <param name="SaveMarker">Persists the marker (null deletes it). Called on the engine thread.</param>
public sealed record ProcessingRequest(RoleTarget Source, MicSettings Settings, bool SetDefault, Action<ProcessingMarker?> SaveMarker);

/// <summary>
/// Owns at most one <see cref="IMicPipeline"/> and the capture roles Mic processing takes. Runs on
/// the engine thread and is updated on every reconcile, before the arbiter enforces, so the
/// pipeline follows the winning real mic (Karl gap 1) and a missing driver or mic stops it at once.
/// After a failure it waits for its mic to change or come back; there is no retry loop.
/// </summary>
internal sealed class MicPipelineHost(ILogger log, MicPipelineFactory factory, Action<Action> enqueue, Action reconcile)
{
    private static readonly AudioRole[] CaptureRoles = [AudioRole.Console, AudioRole.Multimedia, AudioRole.Communications];

    private ProcessingRequest? _request;
    private Action<ProcessingMarker?>? _saveMarker;
    private IMicPipeline? _pipeline;
    private string? _source;
    private string? _sink;
    private string? _failedSource;
    private string? _failedError;
    private ProcessingMarker? _marker;
    private bool _claimed;

    public ProcessingStatus Status { get; private set; } = ProcessingStatus.Off;

    public float InputLevel => _pipeline?.InputLevel ?? 0f;

    public float OutputLevel => _pipeline?.OutputLevel ?? 0f;

    /// <summary>A new request (or null for off). A new request also clears a past failure, so switching on again retries.</summary>
    public void Set(ProcessingRequest? request)
    {
        _request = request;
        if (request is not null)
            _saveMarker = request.SaveMarker;
        _failedSource = _failedError = null;
    }

    public void Update(RoleArbiter arbiter, DeviceCatalog catalog, IDeviceSystem system)
    {
        if (_request is null)
        {
            Halt(ProcessingStatus.Off, arbiter, catalog, system);
            return;
        }

        var (cable, pair) = VbCable.Find([.. catalog.Endpoints(AudioFlow.Render), .. catalog.Endpoints(AudioFlow.Capture)]);
        if (cable != CableState.Ready)
        {
            var reason = cable == CableState.Disabled ? StopReason.DriverDisabled : StopReason.NoDriver;
            Halt(new(ProcessingState.Stopped, reason, Error: cable == CableState.Ambiguous ? "More than one VB-CABLE" : null), arbiter, catalog, system);
            return;
        }

        var source = _request.Source(catalog.Active(AudioFlow.Capture).Where(e => !VbCable.IsCable(e)).ToList());
        if (source is null)
        {
            _failedSource = _failedError = null; // the mic left: its return is a fresh try
            Halt(new(ProcessingState.Stopped, StopReason.NoMicrophone), arbiter, catalog, system);
            return;
        }

        if (_pipeline is null && PriorityPolicy.Eq(_failedSource, source))
        {
            Halt(new(ProcessingState.Stopped, StopReason.Failed, source, _failedError), arbiter, catalog, system);
            return;
        }

        if (_pipeline is null || !PriorityPolicy.Eq(source, _source) || !PriorityPolicy.Eq(pair!.RenderId, _sink))
        {
            DisposePipeline();
            if (!StartPipeline(source, pair!.RenderId))
            {
                Halt(new(ProcessingState.Stopped, StopReason.Failed, source, _failedError), arbiter, catalog, system);
                return;
            }
        }

        Status = new(ProcessingState.Live, SourceId: source);
        if (_request.SetDefault)
            TakeRoles(pair!.CaptureId, arbiter, catalog);
        else
            ReleaseRoles(arbiter, catalog, system);
    }

    /// <summary>Engine stop: the pipeline goes and the replaced defaults come back.</summary>
    public void Shutdown(RoleArbiter arbiter, DeviceCatalog catalog, IDeviceSystem system)
    {
        _request = null;
        Halt(ProcessingStatus.Off, arbiter, catalog, system);
    }

    private bool StartPipeline(string source, string sink)
    {
        IMicPipeline? pipeline = null;
        try
        {
            pipeline = factory(source, sink, _request!.Settings);
            var started = pipeline;
            pipeline.Failed += error => enqueue(() => OnFailed(started, error));
            pipeline.Start();
            _pipeline = pipeline;
            _source = source;
            _sink = sink;
            _failedSource = _failedError = null;
            log.LogInformation("Mic processing live: {Source} -> {Sink}", source, sink);
            return true;
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Mic processing couldn't start on {Source}", source);
            pipeline?.Dispose();
            _failedSource = source;
            _failedError = ex.Message;
            return false;
        }
    }

    // On the engine thread, after the audio thread reported that capture or render stopped.
    private void OnFailed(IMicPipeline pipeline, Exception? error)
    {
        if (!ReferenceEquals(pipeline, _pipeline))
            return;
        log.LogWarning(error, "Mic processing stopped on {Source}", _source);
        _failedSource = _source;
        _failedError = error?.Message;
        DisposePipeline();
        reconcile();
    }

    private void Halt(ProcessingStatus status, RoleArbiter arbiter, DeviceCatalog catalog, IDeviceSystem system)
    {
        DisposePipeline();
        ReleaseRoles(arbiter, catalog, system);
        Status = status;
    }

    private void DisposePipeline()
    {
        if (_pipeline is null)
            return;
        try { _pipeline.Dispose(); }
        catch (Exception ex) { log.LogWarning(ex, "Disposing the mic pipeline failed"); }
        _pipeline = null;
        _source = _sink = null;
    }

    // The marker is written before the roles change, so a crash right after still knows what to restore.
    private void TakeRoles(string cableCaptureId, RoleArbiter arbiter, DeviceCatalog catalog)
    {
        if (_claimed && PriorityPolicy.Eq(_marker?.CableCaptureId, cableCaptureId))
            return;
        // A marker still waiting for a real mic to put back keeps what it recorded.
        if (_marker is null || !PriorityPolicy.Eq(_marker.CableCaptureId, cableCaptureId))
        {
            var replaced = CaptureRoles
                .Select(role => (role, id: catalog.Default(new RoleKey(AudioFlow.Capture, role))))
                .Where(r => r.id is not null && !PriorityPolicy.Eq(r.id, cableCaptureId))
                .ToDictionary(r => r.role, r => r.id!);
            _marker = new ProcessingMarker(cableCaptureId, replaced);
            _saveMarker?.Invoke(_marker);
        }
        arbiter.Claim(RoleOwner.Processing, AudioFlow.Capture, CaptureRoles, _ => cableCaptureId);
        _claimed = true;
    }

    // Roles left on the cable go back to a real mic. With none active the marker stays, here and on
    // disk, and every reconcile tries again until one appears.
    private void ReleaseRoles(RoleArbiter arbiter, DeviceCatalog catalog, IDeviceSystem system)
    {
        if (_marker is null)
            return;
        var cableJustLost = false;
        if (_claimed)
        {
            arbiter.Release(RoleOwner.Processing, AudioFlow.Capture, null, catalog, system);
            _claimed = false;
            cableJustLost = !catalog.Active(AudioFlow.Capture).Any(e => PriorityPolicy.Eq(e.Id, _marker.CableCaptureId));
        }
        var preferred = _request?.Source(catalog.Active(AudioFlow.Capture).Where(e => !VbCable.IsCable(e)).ToList());
        if (!arbiter.Recover(_marker, preferred, catalog, system, cableJustLost))
            return;
        _marker = null;
        _saveMarker?.Invoke(null);
    }
}
