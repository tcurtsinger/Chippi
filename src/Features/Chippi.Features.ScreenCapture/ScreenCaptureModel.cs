// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Chippi.Core.Modules;
using Chippi.Core.Shortcuts;

namespace Chippi.Features.ScreenCapture;

/// <summary>The Settings page's state, shared with the running capture controller.</summary>
public sealed class ScreenCaptureModel : IScreenCapturePageModel, IDisposable
{
    private readonly ModuleContext _context;
    private readonly RecentCaptureStore _store;
    private readonly Action<BitmapSource> _annotate;
    private readonly Func<RecentCapture, bool> _recycle;
    private readonly Dictionary<Guid, ImageSource> _thumbnails = [];

    public ScreenCaptureModel(ModuleContext context, RecentCaptureStore store,
        Action<BitmapSource> annotate, Func<RecentCapture, bool> recycle)
    {
        _context = context;
        _store = store;
        _annotate = annotate;
        _recycle = recycle;
        Settings = context.Settings.Get<ScreenCaptureSettings>();
        RefreshRecent();
        context.Shortcuts.Changed += ShortcutsChanged;
    }

    public ScreenCaptureSettings Settings { get; private set; }
    public string SaveFolder => Settings.SaveFolder ?? CaptureFiles.DefaultFolder;
    public bool SaveFolderMissing => Settings.SaveFolder != null && !Directory.Exists(SaveFolder);
    public string? SettingsError { get; private set; }
    public IReadOnlyList<RecentCapture> Recent { get; private set; } = [];
    public event Action? Changed;

    public void RefreshRecent()
    {
        var recent = _store.Read();
        var thumbnails = new Dictionary<Guid, ImageSource>();
        foreach (var capture in recent)
        {
            if (Recent.Any(x => x.Id == capture.Id && x.Image == capture.Image) && _thumbnails.TryGetValue(capture.Id, out var existing))
            {
                thumbnails[capture.Id] = existing;
                continue;
            }
            using var stream = new MemoryStream(_store.Thumbnail(capture.Id));
            var image = new BitmapImage();
            image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = stream; image.EndInit(); image.Freeze();
            thumbnails[capture.Id] = image;
        }
        _thumbnails.Clear();
        foreach (var item in thumbnails) _thumbnails.Add(item.Key, item.Value);
        Recent = recent;
        Changed?.Invoke();
    }

    public ImageSource? Thumbnail(Guid id) => _thumbnails.GetValueOrDefault(id);
    public bool FileMissing(RecentCapture capture) => capture.SavedFile != null && !File.Exists(capture.SavedFile);
    public (KeyShortcut? Keys, string? Error) Shortcut(string name)
    {
        var slot = _context.Shortcuts.List.FirstOrDefault(x => x.Name == name &&
            x.Owner == (name == ScreenCaptureShortcuts.OpenCapture ? "capture" : _context.ModuleId));
        return (slot?.Shortcut, slot?.Error);
    }

    internal bool Save(ScreenCaptureSettings settings)
    {
        try
        {
            _context.Settings.Save(settings);
            Settings = settings;
            SettingsError = null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SettingsError = "Save failed";
            Changed?.Invoke();
            return false;
        }
        Changed?.Invoke();
        return true;
    }

    public bool SetEnter(EnterAction action) => Enum.IsDefined(action) && Save(Settings with { Enter = action });
    public bool SetFileType(CaptureFileType type) => Enum.IsDefined(type) && Save(Settings with { FileType = type });
    public bool SetHistory(bool on) => Save(Settings with { History = on });
    public bool SetCtrlDragCopies(bool on) => Save(Settings with { CtrlDragCopies = on });
    public bool SetMagnifier(bool on) => Save(Settings with { Magnifier = on });
    public bool SetSaveFolder(string? folder)
    {
        if (folder != null && (!Path.IsPathFullyQualified(folder) || !Directory.Exists(folder)))
        {
            SettingsError = "Save failed";
            Changed?.Invoke();
            return false;
        }
        return Save(Settings with { SaveFolder = folder });
    }

    public void Annotate(Guid id) => _annotate(_store.Load(id));
    public void Remove(Guid id) { _store.Remove(id); RefreshRecent(); }
    public void ClearRecent() { _store.Clear(); RefreshRecent(); }
    public bool DeleteFile(Guid id)
    {
        var capture = Recent.FirstOrDefault(x => x.Id == id);
        if (capture?.SavedFile == null || FileMissing(capture)) return false;
        bool deleted = _recycle(capture);
        Changed?.Invoke();
        return deleted;
    }
    public void EditShortcut(string name)
    {
        if (name == ScreenCaptureShortcuts.OpenCapture) _context.EditShortcutFor("capture", name);
        else _context.EditShortcut(name);
    }
    private void ShortcutsChanged() => Changed?.Invoke();
    public void Dispose() => _context.Shortcuts.Changed -= ShortcutsChanged;
}
