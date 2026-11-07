// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors
using System.IO;
using System.Windows.Media.Imaging;
using Chippi.Core.Modules;
using Chippi.UI.Capture;
using Microsoft.Win32;

namespace Chippi.Features.ScreenCapture;

public sealed class CaptureRecentModel : ICaptureRecentModel, IDisposable
{
    private readonly ScreenCaptureModel _page;
    private readonly RecentCaptureStore _store;
    private readonly ModuleContext _context;
    private Guid? _id;
    private int _generation;
    private bool _disposed;
    public CaptureRecentModel(ScreenCaptureModel page, RecentCaptureStore store, ModuleContext context)
    {
        _page = page; _store = store; _context = context;
        page.Changed += Refresh;
    }
    public CaptureRecentResult? Result { get; private set; }
    public event Action? Changed;
    public async Task OpenAsync(Guid id)
    {
        _id = id;
        int generation = ++_generation;
        Refresh();
        try
        {
            var image = await CaptureFiles.OnWorkerAsync(() => _store.Load(id));
            if (!_disposed && generation == _generation && Result is { } result)
            { Result = result with { Image = image }; Changed?.Invoke(); }
        }
        catch (Exception ex) when (CaptureFiles.ReadFailed(ex)) { }
    }
    private void Refresh()
    {
        if (_disposed) return;
        var entry = _page.Recent.FirstOrDefault(x => x.Id == _id);
        Result = entry == null ? null : new CaptureRecentResult(entry.Id.ToString(), entry.Width, entry.Height,
            entry.Time.ToLocalTime().ToString("g"), _page.Thumbnail(entry.Id))
        {
            Image = Result?.Id == entry.Id.ToString() ? Result.Image : null,
            SavedFile = entry.SavedFile,
            FileMissing = entry.SavedFile != null && (entry.SavedIdentity == null || CaptureSavedFile.Identity(entry.SavedFile) != entry.SavedIdentity),
        };
        Changed?.Invoke();
    }
    private async Task<BitmapSource?> PixelsAsync()
    {
        if (_id is not { } id || Result == null) return null;
        return Result.Image as BitmapSource ?? await CaptureFiles.OnWorkerAsync(() => _store.Load(id));
    }
    public async Task<bool> CopyAsync()
    {
        try
        {
            var pixels = await PixelsAsync();
            return !_disposed && pixels != null && _context.Clipboard != null && await CaptureClipboard.CopyAsync(_context.Clipboard, pixels);
        }
        catch (Exception ex) when (CaptureFiles.ReadFailed(ex)) { return false; }
    }
    public async Task<CaptureSave> SaveAsAsync()
    {
        if (Result == null || _disposed) return new(CaptureSaveOutcome.Failed);
        var dialog = new SaveFileDialog { Filter = "PNG image|*.png|JPEG image|*.jpg", FileName = "Screenshot.png", OverwritePrompt = true, AddExtension = true };
        if (dialog.ShowDialog() != true) return new(CaptureSaveOutcome.Cancelled);
        try
        {
            var pixels = await PixelsAsync();
            if (_disposed || pixels == null) return new(CaptureSaveOutcome.Failed);
            await CaptureFiles.OnWorkerAsync(() => { CaptureFiles.SaveAs(pixels, dialog.FileName, dialog.FilterIndex == 2 ? CaptureFileType.Jpeg : CaptureFileType.Png, true); return true; });
            return new(CaptureSaveOutcome.Saved, dialog.FileName);
        }
        catch (Exception ex) when (CaptureFiles.ReadFailed(ex)) { return new(CaptureSaveOutcome.Failed); }
    }
    public bool Annotate()
    {
        if (_id is not { } id || Result == null || _disposed) return false;
        // Read before closing, so a failure shows on this page instead of a closed island.
        return _page.Annotate(id, _context.CloseIsland);
    }
    public bool OpenFolder()
    {
        Refresh();
        if (Result is not { } result) return false;
        return CaptureFiles.OpenFolder(result.SavedFile is { } file ? Path.GetDirectoryName(file) : _page.SaveFolder);
    }
    public void Dispose() { _disposed = true; ++_generation; _page.Changed -= Refresh; Result = null; Changed?.Invoke(); }
}
