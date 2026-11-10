// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

namespace Chippi.Media;

public enum MediaJobState { Waiting, Running, Done, Failed, Cancelled, Refused }

public sealed class MediaJob(string path)
{
    public string Path { get; } = path;
    public string Name => System.IO.Path.GetFileName(Path);
    public MediaFileInfo? Source { get; internal set; }
    public MediaExportResult? Output { get; internal set; }
    public MediaJobState State { get; internal set; }
    public string? Error { get; internal set; }
    public int Percent { get; internal set; }
}

/// <summary>One sequential batch owned by its caller's synchronization context; file lists are never persisted.</summary>
public sealed class MediaBatch(IMediaToolsCodec codec)
{
    private readonly List<MediaJob> _jobs = [];
    private CancellationTokenSource? _run;
    private Task _completion = Task.CompletedTask;
    private Task _inspection = Task.CompletedTask;
    private CancellationTokenSource? _inspect;
    public IReadOnlyList<MediaJob> Jobs => _jobs;
    public bool IsRunning => _run != null;
    public bool IsInspecting { get; private set; }
    public string? Error { get; private set; }
    public event Action? Changed;

    public Task AddAsync(IEnumerable<string> paths, CancellationToken cancellationToken = default)
    {
        if (IsRunning || IsInspecting) return _inspection;
        _inspect = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        return _inspection = AddCoreAsync(paths, _inspect);
    }
    private async Task AddCoreAsync(IEnumerable<string> paths, CancellationTokenSource inspection)
    {
        var cancellationToken = inspection.Token;
        IsInspecting = true; Error = null; Changed?.Invoke();
        try
        {
            foreach (string path in paths)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string fullPath;
                try { fullPath = Path.GetFullPath(path); }
                catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { Error = "Invalid file path"; continue; }
                if (_jobs.Any(job => string.Equals(job.Path, fullPath, StringComparison.OrdinalIgnoreCase))) continue;
                var job = new MediaJob(fullPath); _jobs.Add(job); Changed?.Invoke();
                try { job.Source = await Task.Run(() => codec.InspectAsync(fullPath, cancellationToken), cancellationToken); }
                catch (OperationCanceledException) { _jobs.Remove(job); throw; }
                catch (Exception ex) { job.State = MediaJobState.Refused; job.Error = Message(ex); }
                Changed?.Invoke();
            }
        }
        finally { IsInspecting = false; _inspect = null; inspection.Dispose(); Changed?.Invoke(); }
    }

    public void Remove(MediaJob job) { if (!IsRunning && !IsInspecting) { _jobs.Remove(job); Changed?.Invoke(); } }
    public void OptionsChanged()
    {
        if (IsRunning) return;
        foreach (var job in _jobs.Where(j => j.Source != null)) { job.State = MediaJobState.Waiting; job.Error = null; job.Output = null; job.Percent = 0; }
        Error = null; Changed?.Invoke();
    }
    public Task RunAsync(MediaToolOptions options, MediaJob? retry = null, CancellationToken cancellationToken = default)
    {
        if (IsRunning || IsInspecting) return _completion;
        options.Validate();
        _run = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        return _completion = RunCoreAsync(options, retry, _run);
    }
    private async Task RunCoreAsync(MediaToolOptions options, MediaJob? retry, CancellationTokenSource run)
    {
        Error = null; Changed?.Invoke();
        var pending = _jobs.Where(j => j.Source != null && (retry != null ? ReferenceEquals(j, retry) : j.State != MediaJobState.Done)).ToArray();
        try
        {
            foreach (var job in pending)
            {
                if (run.IsCancellationRequested) break;
                if (MediaToolsCodec.Refusal(job.Source!, options) is { } refusal)
                { job.State = MediaJobState.Refused; job.Error = refusal; Changed?.Invoke(); continue; }
                job.State = MediaJobState.Running; job.Percent = 0; job.Error = null; Changed?.Invoke();
                var progress = new Progress<double>(value =>
                {
                    if (job.State != MediaJobState.Running) return;
                    int percent = (int)Math.Clamp(value, 0, 100);
                    if (job.Percent == percent) return;
                    job.Percent = percent; Changed?.Invoke();
                });
                try
                {
                    job.Output = await Task.Run(() => MediaToolExport.RunAsync(codec, job.Source!, options, progress, run.Token), run.Token);
                    job.State = MediaJobState.Done; job.Percent = 100;
                }
                catch (OperationCanceledException) when (run.IsCancellationRequested) { job.State = MediaJobState.Cancelled; }
                catch (Exception ex)
                {
                    job.State = MediaJobState.Failed; job.Error = Message(ex);
                    if (DiskFull(ex)) { Error = "Disk full"; run.Cancel(); }
                }
                Changed?.Invoke();
            }
            if (run.IsCancellationRequested)
                foreach (var job in pending.Where(j => j.State == MediaJobState.Waiting)) job.State = MediaJobState.Cancelled;
        }
        finally { _run = null; run.Dispose(); Changed?.Invoke(); }
    }
    public void Cancel() => _run?.Cancel();
    public async Task StopAsync()
    {
        Cancel(); _inspect?.Cancel();
        try { await Task.WhenAll(_completion, _inspection); }
        catch (OperationCanceledException) { }
    }
    private static bool DiskFull(Exception exception) => (exception.HResult & 0xffff) is 112 or 39 || (exception.InnerException != null && DiskFull(exception.InnerException));
    private static string Message(Exception exception) => exception switch
    {
        FileNotFoundException or DirectoryNotFoundException => "File not found",
        UnauthorizedAccessException => "Access denied",
        _ when DiskFull(exception) => "Disk full",
        IOException => "File access failed · " + exception.Message,
        _ => exception.Message,
    };
}
