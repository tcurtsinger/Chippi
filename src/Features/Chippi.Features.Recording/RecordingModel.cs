// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.IO;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Chippi.Core.Modules;
using Chippi.Core.Shortcuts;
using Chippi.Media;
using Microsoft.Extensions.Logging;

namespace Chippi.Features.Recording;

public sealed record RecordingSettings
{
    public string? SaveFolder { get; init; }
    public bool OriginalSize { get; init; }
    public int FrameRate { get; init; } = 30;
    public bool IncludeCursor { get; init; } = true;
    public Dictionary<string, KeyShortcut?> Shortcuts { get; init; } = [];
}

public sealed class RecordingModel : IDisposable
{
    private readonly ModuleContext _context;
    private readonly IRecordingBackend _backend;
    private readonly IRecordingStore _store;
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private CancellationTokenSource? _start, _export;
    private IRecordingSession? _session;
    private Task _completion = Task.CompletedTask, _starting = Task.CompletedTask, _exporting = Task.CompletedTask;
    private string? _partial;
    private bool _discard, _disposed;
    private readonly Dictionary<Guid, BitmapSource?> _posters = [];
    internal Func<TimeSpan, CancellationToken, Task> Delay { get; set; } = Task.Delay;
    internal Func<string, CancellationToken, Task<RecordingInfo>> Inspect { get; set; } = RecordingExports.InspectAsync;
    public Func<bool> CanStart { get; set; } = () => true;
    public RecordingSettings Settings { get; private set; }
    public string SaveFolder => string.IsNullOrWhiteSpace(Settings.SaveFolder) ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "Chippi") : Path.GetFullPath(Settings.SaveFolder);
    public RecordingPhase Phase { get; private set; }
    public int Countdown { get; private set; }
    public TimeSpan Elapsed => _session?.Elapsed ?? TimeSpan.Zero;
    public bool IsPaused => Phase == RecordingPhase.Paused;
    public bool Active => Phase is not RecordingPhase.Idle;
    public bool SystemAudio { get; set; } = true;
    public bool MicAudio { get; set; }
    public RecordingTarget Target { get; private set; } = RecordingTarget.Screen;
    public RecordingSource? Source { get; set; }
    public IReadOnlyList<RecordingSource> Sources { get; private set; } = [];
    public RecordingDeviceNames Devices { get; private set; } = new(null, null);
    public IReadOnlyList<RecordingEntry> Recent { get; private set; }
    public RecordingEntry? Selected { get; private set; }
    public string? Message { get; private set; }
    public bool Exporting => _export != null;
    public double ExportProgress { get; private set; }
    public event Action? Changed;
    public event Action? ResultReady;
    public event Action? Starting;

    public RecordingModel(ModuleContext context, IRecordingBackend backend, IRecordingStore store)
    {
        _context = context; _backend = backend; _store = store;
        Settings = context.Settings.Get<RecordingSettings>();
        if (Settings.FrameRate is not (30 or 60)) Settings = Settings with { FrameRate = 30 };
        Recent = store.Read();
        _timer.Tick += (_, _) => Changed?.Invoke();
    }

    public void Setup(RecordingTarget target)
    {
        if (Active) { Message = "Already recording"; Changed?.Invoke(); return; }
        Target = target; Selected = null; SystemAudio = true; MicAudio = false; Message = null;
        RefreshSources();
    }
    public void ChangeTarget(RecordingTarget target)
    {
        if (Active) return;
        Target = target; Source = null; RefreshSources();
    }
    public void RefreshSources()
    {
        if (Active) return;
        try
        {
            Sources = _backend.Sources(Target); Devices = _backend.Devices();
            Source = Sources.FirstOrDefault(x => x.Handle == Source?.Handle) ?? Sources.FirstOrDefault();
        }
        catch (Exception ex) when (ex is IOException or System.Runtime.InteropServices.COMException or InvalidOperationException)
        { Message = "Recording sources unavailable"; }
        Changed?.Invoke();
    }
    public bool SaveSettings(RecordingSettings settings)
    {
        if (settings.FrameRate is not (30 or 60)) return false;
        try { _context.Settings.Save(settings); Settings = settings; Changed?.Invoke(); return true; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Report("Save failed"); return false; }
    }
    public void Report(string message) { Message = message; Changed?.Invoke(); }
    public void Interrupt(string message) => _dispatcher.BeginInvoke(() => { if (Active) { Report(message); _ = StopAsync(); } });
    public Task StartAsync()
    {
        if (Active || Exporting) { Report("Already recording or exporting"); return Task.CompletedTask; }
        if (!CanStart()) { Report("Finish the current capture first"); return Task.CompletedTask; }
        if (Source == null) { Report("Choose a window or display"); return Task.CompletedTask; }
        _starting = StartCoreAsync(Source); return _starting;
    }
    private async Task StartCoreAsync(RecordingSource source)
    {
        using var cancellation = new CancellationTokenSource(); _start = cancellation;
        _discard = false; _partial = null; Message = null;
        Selected = null;
        try
        {
            Directory.CreateDirectory(SaveFolder);
            _partial = Path.Combine(SaveFolder, ".chippi-recording-" + Guid.NewGuid().ToString("N") + ".partial.mp4");
            Phase = RecordingPhase.Countdown; Starting?.Invoke();
            for (int seconds = 3; seconds > 0; seconds--)
            { Countdown = seconds; Changed?.Invoke(); await Delay(TimeSpan.FromSeconds(1), cancellation.Token); }
            cancellation.Token.ThrowIfCancellationRequested();
            Phase = RecordingPhase.Starting; Changed?.Invoke();
            // Give the shell's capture-exclusion update a dispatcher turn before the first frame is requested.
            await Dispatcher.Yield(DispatcherPriority.Background);
            var options = new RecordingOptions { SystemAudio = SystemAudio, MicAudio = MicAudio, OriginalSize = Settings.OriginalSize, FrameRate = Settings.FrameRate, IncludeCursor = Settings.IncludeCursor };
            _session = await _backend.StartAsync(source, options, _partial, cancellation.Token);
            _session.Warning += OnWarning;
            Phase = RecordingPhase.Recording; _timer.Start(); Changed?.Invoke();
            _completion = ObserveAsync(_session, source, _partial);
        }
        catch (OperationCanceledException) { Phase = RecordingPhase.Idle; DeleteOwnedEmptyPartial(); Changed?.Invoke(); }
        catch (Exception ex)
        {
            Phase = RecordingPhase.Idle;
            _context.Logger.LogError(new Microsoft.Extensions.Logging.EventId(0), ex, "Recording start failed");
            Message = ex is NotSupportedException ? ex.Message : "Recording could not start. Check the source, audio devices, and hardware encoder.";
            DeleteOwnedEmptyPartial(); Changed?.Invoke();
        }
        finally { _start = null; }
    }

    private void OnWarning(string message) => _dispatcher.BeginInvoke(() => { if (!_disposed) { _context.Logger.LogWarning("Recording: {Warning}", message); Report(message); } });
    private async Task ObserveAsync(IRecordingSession session, RecordingSource source, string partial)
    {
        try
        {
            var result = await session.Completion;
            _context.Logger.LogInformation("Recording finished: {Encoder}; {Width}x{Height}; {Frames} frames; {Dropped} dropped; {Duration}", result.Encoder, result.Width, result.Height, result.Frames, result.DroppedFrames, result.Duration);
            if (!string.Equals(Path.GetFullPath(result.Path), Path.GetFullPath(partial), StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Unexpected recording output path");
            if (_discard) { File.Delete(partial); Message = "Recording discarded"; return; }
            var info = await Inspect(partial, CancellationToken.None);
            if (info.Duration <= TimeSpan.Zero) throw new InvalidDataException("The recording has no playable video");
            string saved = Path.Combine(Path.GetDirectoryName(partial)!, $"Chippi {DateTime.Now:yyyy-MM-dd HH-mm-ss}-{Guid.NewGuid().ToString("N")[..6]}.mp4");
            File.Move(partial, saved);
            var file = new FileInfo(saved);
            var entry = new RecordingEntry(Guid.NewGuid(), saved, source.Name, DateTimeOffset.Now, info.Duration, info.Width, info.Height, file.Length, file.LastWriteTimeUtc);
            try { _store.Add(entry, info.Poster); Recent = _store.Read(); foreach (var id in _posters.Keys.Except(Recent.Select(x => x.Id)).ToArray()) _posters.Remove(id); }
            catch (Exception ex) { _context.Logger.LogError(new Microsoft.Extensions.Logging.EventId(0), ex, "Recording recent index save failed"); Message = "Video saved; Recent could not be updated"; }
            Selected = entry; Message ??= result.Warning ?? "Recording saved";
        }
        catch (Exception ex)
        {
            _context.Logger.LogError(new Microsoft.Extensions.Logging.EventId(0), ex, "Recording finish failed");
            Message = File.Exists(partial) ? "Recording interrupted. The partial file was kept in the recording folder." : "Recording failed";
        }
        finally
        {
            session.Warning -= OnWarning;
            try { await session.DisposeAsync(); } catch (Exception ex) { _context.Logger.LogError(new Microsoft.Extensions.Logging.EventId(0), ex, "Recording cleanup failed"); }
            _session = null; _partial = null; _timer.Stop(); Phase = RecordingPhase.Idle; Changed?.Invoke();
        }
        if (Selected != null && !_discard) ResultReady?.Invoke();
    }
    public void TogglePause()
    {
        if (_session == null || Phase is not (RecordingPhase.Recording or RecordingPhase.Paused)) return;
        if (IsPaused) { _session.Resume(); Phase = RecordingPhase.Recording; } else { _session.Pause(); Phase = RecordingPhase.Paused; }
        Changed?.Invoke();
    }
    public async Task StopAsync()
    {
        if (_session == null && _start != null) { _start.Cancel(); await _starting; return; }
        if (_session == null) { Report("Not recording"); return; }
        Phase = RecordingPhase.Stopping; Changed?.Invoke();
        try { await _session.StopAsync(); } catch { /* ObserveAsync preserves and reports any partial result. */ }
        await _completion;
    }
    public async Task DiscardAsync() { if (!Active) return; _discard = true; await StopAsync(); }
    public void Open(RecordingEntry entry)
    {
        if (!Available(entry)) { Report("Recording file is missing or changed"); return; }
        Selected = entry; Changed?.Invoke();
    }
    public BitmapSource? Poster(RecordingEntry entry)
    {
        if (_posters.TryGetValue(entry.Id, out var cached)) return cached;
        var bytes = _store.Poster(entry.Id);
        if (bytes == null || bytes.Length == 0) return null;
        using var stream = new MemoryStream(bytes); var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = stream; image.EndInit(); image.Freeze(); _posters[entry.Id] = image; return image;
    }
    public static bool Available(RecordingEntry entry)
    {
        try { var file = new FileInfo(entry.Path); return file.Exists && file.Length == entry.Bytes && file.LastWriteTimeUtc == entry.LastWriteUtc; }
        catch (IOException) { return false; } catch (UnauthorizedAccessException) { return false; }
    }
    public Task ExportAsync(string destination, TimeSpan start, TimeSpan end, bool gif)
    {
        if (Active || Exporting) return Task.CompletedTask;
        return _exporting = ExportCoreAsync(destination, start, end, gif);
    }
    public Task SaveCopyAsync(string destination)
    {
        if (Active || Exporting) return Task.CompletedTask;
        return _exporting = ExportCoreAsync(destination, TimeSpan.Zero, Selected?.Duration ?? TimeSpan.Zero, null);
    }
    private async Task ExportCoreAsync(string destination, TimeSpan start, TimeSpan end, bool? gif)
    {
        if (Active || Exporting || Selected is not { } entry) return;
        if (!Available(entry)) { Report("Recording file is missing or changed"); return; }
        using var cancellation = new CancellationTokenSource(); _export = cancellation; ExportProgress = 0; Changed?.Invoke();
        try
        {
            var progress = new Progress<double>(value => { ExportProgress = value; Changed?.Invoke(); });
            if (gif == null) await RecordingExports.CopyAsync(entry.Path, destination, progress, cancellation.Token);
            else if (gif == true) await RecordingExports.GifAsync(entry.Path, destination, start, end, progress, cancellation.Token);
            else await RecordingExports.TrimAsync(entry.Path, destination, start, end, progress, cancellation.Token);
            Message = "Export saved";
        }
        catch (OperationCanceledException) { Message = "Export cancelled"; }
        catch (Exception ex) { _context.Logger.LogError(new Microsoft.Extensions.Logging.EventId(0), ex, "Recording export failed"); Message = ex is ArgumentException ? ex.Message : "Export failed; the original recording is unchanged"; }
        finally { _export = null; Changed?.Invoke(); }
    }
    public void CancelExport() => _export?.Cancel();
    private void DeleteOwnedEmptyPartial()
    {
        try { if (_partial != null && File.Exists(_partial) && new FileInfo(_partial).Length == 0) File.Delete(_partial); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { _context.Logger.LogWarning(ex, "Empty recording cleanup failed"); }
        _partial = null;
    }
    public async Task ShutdownAsync() { CancelExport(); if (Active) await StopAsync(); await _exporting; }
    public void Dispose() { if (_disposed) return; _disposed = true; _timer.Stop(); _start?.Cancel(); _export?.Cancel(); }
}
