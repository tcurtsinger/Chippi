// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows.Media.Imaging;

namespace Chippi.Features.ScreenCapture;

public sealed record CaptureOutputResult(bool Copied, bool Saved, bool CopyFailed, bool SaveFailed, bool RecentFailed)
{
    public string? SavedFile { get; init; }
    public string Title => (Copied, Saved, CopyFailed, SaveFailed) switch
    {
        (true, _, _, true) => "Copied. Save failed",
        (_, true, true, _) => "Saved. Copy failed",
        (_, _, true, _) => "Copy failed",
        (_, _, _, true) => "Save failed",
        (true, true, _, _) => "Copied and saved",
        (true, _, _, _) => "Copied",
        _ => "Saved",
    };
}

/// <summary>One finish attempt over flattened pixels. Retry repeats only its failed outputs.</summary>
public sealed class CaptureOutput(
    BitmapSource flattened,
    Func<BitmapSource, Task<bool>> copy,
    Func<BitmapSource, Task<string>> save,
    Func<BitmapSource, string?, Task> retain)
{
    private bool _busy, _copied, _saved, _retained;
    private string? _savedFile;
    private EnterAction? _action;

    public Task<CaptureOutputResult> FinishAsync(EnterAction action)
    {
        if (!Enum.IsDefined(action)) throw new ArgumentOutOfRangeException(nameof(action));
        return RunAsync(action, retry: false);
    }

    public Task<CaptureOutputResult> RetryAsync() => _action is { } action
        ? RunAsync(action, retry: true)
        : throw new InvalidOperationException("No output to retry.");

    private async Task<CaptureOutputResult> RunAsync(EnterAction action, bool retry)
    {
        if (_busy) throw new InvalidOperationException("An output is already running.");
        _busy = true;
        try
        {
            if (!retry)
            {
                _action = action;
                _copied = _saved = _retained = false;
                _savedFile = null;
            }
            bool wantsCopy = action is EnterAction.Copy or EnterAction.CopyAndSave;
            bool wantsSave = action is EnterAction.Save or EnterAction.CopyAndSave;
            if (wantsCopy && !_copied)
            {
                try { _copied = await copy(flattened); }
                catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
                { _copied = false; }
            }
            if (wantsSave && !_saved)
            {
                try { _savedFile = await save(flattened); _saved = true; }
                catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
                { _saved = false; }
            }
            bool recentFailed = false;
            // Re-run retention after a successful save retry to update the existing draft's saved-file status.
            if ((_copied || _saved) && !_retained)
            {
                try { await retain(flattened, _savedFile); _retained = !wantsSave || _saved; }
                catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or Microsoft.Data.Sqlite.SqliteException)
                { recentFailed = true; }
            }
            return new(_copied, _saved, wantsCopy && !_copied, wantsSave && !_saved, recentFailed) { SavedFile = _savedFile };
        }
        finally { _busy = false; }
    }
}
