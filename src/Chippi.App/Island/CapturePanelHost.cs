// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors
using System.IO;
using System.Windows.Controls;
using Chippi.Core.Shortcuts;
using System.Windows.Threading;
using Chippi.App.Shell;
using Chippi.Capture;
using Chippi.Core.Modules;
using Chippi.Features.ScreenCapture;
using Chippi.Features.ScreenText;
using Chippi.Features.ColorPicker;
using Chippi.Features.Recording;
using Chippi.Media;
using Chippi.UI.Capture;
using Chippi.UI.Controls;

namespace Chippi.App.Island;

/// <summary>The shared Capture page follows the installed providers, independently of Screen capture.</summary>
internal sealed class CapturePanelHost : ICapturePanelModel, IDisposable
{
    private readonly ShellContext _shell;
    private readonly ScreenCaptureModule _capture;
    private readonly ScreenTextModule _text;
    private readonly ColorPickerModule _color;
    private readonly RecordingModule? _recording;
    private ScreenCaptureModel? _observed;
    private readonly CapturePanelView _view;
    private readonly ContentControl _body = new();
    private readonly ContentControl _header = new();
    private CaptureRecentModel? _recentModel;
    private CaptureRecentView? _recentView;
    private bool _showingRecent;
    private IHotkey? _open;
    private bool _refreshing;
    private bool _disposed;
    public sealed record HostSettings { public KeyShortcut? OpenCapture { get; init; } }
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    public CapturePanelHost(ShellContext shell, ScreenCaptureModule capture, ScreenTextModule text, ColorPickerModule color, RecordingModule? recording = null)
    {
        _shell = shell; _capture = capture; _text = text; _color = color;
        _recording = recording;
        if (recording != null) recording.Changed += QueueRefresh;
        var view = _view = new CapturePanelView(this, shell.Services.HoldIslandOpen);
        Tile = new IslandTileContribution("capture", new IslandTileSpec("capture", "Capture", "Scan", 75, 0, () => _body)
        { CreateHeader = () => _header, InitialFocus = () => _showingRecent ? _recentView?.InitialFocus : view.InitialFocus, HandleBack = Back });
        ShowPanel();
        _body.Unloaded += (_, _) => ShowPanel();
        shell.Services.SharedIslandTiles = () => Visible ? [Tile] : [];
        capture.OpenDelay = OpenDelay;
        shell.Registry.Changed += RegistryChanged;
        shell.Shortcuts.Changed += QueueRefresh;
    }
    private void RegistryChanged(object? sender, EventArgs e) => QueueRefresh();
    private void QueueRefresh() => _dispatcher.BeginInvoke(Refresh);
    public void Dispose()
    {
        _disposed = true;
        _shell.Registry.Changed -= RegistryChanged;
        _shell.Shortcuts.Changed -= QueueRefresh;
        if (_recording != null) _recording.Changed -= QueueRefresh;
        if (_observed != null) _observed.Changed -= Refresh;
        _open?.Dispose(); _open = null;
        _capture.OpenDelay = null;
        _shell.Services.SharedIslandTiles = () => [];
    }
    public IslandTileContribution Tile { get; }
    private bool Running(string id) => _shell.Registry.Find(id)?.IsRunning == true;
    public bool Visible => CanCapture || Running(ScreenTextModule.Id) || Running(ColorPickerModule.Id) || Running(RecordingModule.Id);
    public bool CanCapture => Running(ScreenCaptureModule.Id);
    public bool IsRecording => _recording?.Model?.Active == true;
    public bool HasRecent => CanCapture || Running(RecordingModule.Id);
    public int Delay { get; set; }
    public event Action? Changed;
    internal void Refresh()
    {
        if (_disposed) return;
        if (!_dispatcher.CheckAccess()) { _dispatcher.BeginInvoke(Refresh); return; }
        if (_refreshing) return;
        _refreshing = true;
        try
        {
            if (!ReferenceEquals(_observed, _capture.Model))
            {
                if (_observed != null) _observed.Changed -= Refresh;
                _observed = _capture.Model;
                if (_observed != null) _observed.Changed += Refresh;
            }
            if (Visible && _open == null)
            {
                _open = _shell.Shortcuts.Add("capture", "Open Capture", new ShortcutEdits(
                    () => _shell.Config.GetSection<HostSettings>("captureHost").OpenCapture,
                    key => ChangeOpen(key), () => ChangeOpen(null)) { Icon = "Scan" });
                if (_shell.Config.GetSection<HostSettings>("captureHost").OpenCapture is { } keys) _open.Register(keys, Open);
            }
            else if (!Visible && _open != null) { var key = _open; _open = null; key.Dispose(); }
            Changed?.Invoke();
            }
        finally { _refreshing = false; }
    }
    private void ShowPanel()
    {
        _showingRecent = false;
        _body.Content = _view;
        _header.Content = _view.Header;
    }
    private bool Back() { if (!_showingRecent) return false; ShowPanel(); return true; }
    internal void OpenDelay()
    {
        ShowPanel();
        Open();
        _dispatcher.BeginInvoke(() => { if (CanCapture && _view.IsVisible) _view.OpenDelayMenu(); });
    }
    private void Open()
    {
        ShowPanel();
        ShowPage();
    }
    private void ShowPage()
    {
        if (!_shell.Commands.TryShowPage("capture") && !_shell.Commands.SummonPage("capture")) _shell.Commands.OpenSettings("features");
    }
    private string? ChangeOpen(KeyShortcut? keys)
    {
        var old = _shell.Config.GetSection<HostSettings>("captureHost");
        if (keys != null && _open?.Register(keys, Open) != true) return _open?.Error ?? "Shortcut failed";
        try { _shell.Config.SetSection("captureHost", old with { OpenCapture = keys }); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (old.OpenCapture is { } previous) _open?.Register(previous, Open); else _open?.Unregister();
            return "Save failed";
        }
        if (keys == null) _open?.Unregister();
        return null;
    }
    public string? Keys(CaptureMode mode)
    {
        string name = mode switch
        {
            CaptureMode.Region => ScreenCaptureShortcuts.CaptureRegion,
            CaptureMode.Window => ScreenCaptureShortcuts.CaptureWindow,
            CaptureMode.Screen => ScreenCaptureShortcuts.CaptureFullScreen,
            CaptureMode.AllDisplays => ScreenCaptureShortcuts.CaptureAllDisplays,
            CaptureMode.Freeform => ScreenCaptureShortcuts.CaptureFreeform,
            _ => ScreenCaptureShortcuts.CaptureLastRegion,
        };
        var slot = _shell.Shortcuts.List.FirstOrDefault(x => x.Owner == ScreenCaptureModule.Id && x.Name == name);
        return slot?.Error == null ? slot?.Shortcut?.ToString() : null;
    }
    private string? ToolKeys(string owner, string name) => _shell.Shortcuts.List.FirstOrDefault(x => x.Owner == owner && x.Name == name && x.Error == null)?.Shortcut?.ToString();
    public IReadOnlyList<CapturePanelTool> Tools
    {
        get
        {
            List<CapturePanelTool> tools = [];
            if (Running(RecordingModule.Id)) tools.Add(new("recording/start", IsRecording ? "Recording" : "Record", IsRecording ? "Recording controls" : "Record screen", LucideIcons.Video, () => { if (IsRecording) _recording?.OpenControls(); else _recording?.Setup(RecordingTarget.Screen); }));
            if (Running(ScreenTextModule.Id))
            {
                tools.Add(new("screen-text/extract", "Text", "Extract text", LucideIcons.ScanText, () => _text.Start(ScreenTextVerb.ExtractText, CaptureDoor.Island)) { Keys = ToolKeys(ScreenTextModule.Id, ScreenTextModule.ExtractName) });
                tools.Add(new("screen-text/qr", "QR code", "Scan QR", LucideIcons.QrCode, () => _text.Start(ScreenTextVerb.ScanQr, CaptureDoor.Island)) { Keys = ToolKeys(ScreenTextModule.Id, ScreenTextModule.ScanName) });
            }
            if (Running(ColorPickerModule.Id)) tools.Add(new("color-picker/pick", "Color", "Pick color", LucideIcons.Pipette, () => _color.Pick(false, CaptureDoor.Island)) { Keys = ToolKeys(ColorPickerModule.Id, ColorPickerModule.PickName) });
            return tools.Select(t => t with { Enabled = !IsRecording || t.Id == "recording/start" }).ToArray();
        }
    }
    public IReadOnlyList<CapturePanelRecent> Recent => (_capture.Model?.Recent.Select(x => (x.Time, Item: new CapturePanelRecent(x.Id.ToString(), $"{x.Width} × {x.Height}", x.Time.ToLocalTime().ToString("g"), _capture.Model.Thumbnail(x.Id)))) ?? [])
        .Concat(_recording?.Model?.Recent.Select(x => (Time: x.Created, Item: new CapturePanelRecent("recording/" + x.Id, $"Video · {RecordingModule.FormatTime(x.Duration)}", x.Created.ToLocalTime().ToString("g"), _recording.Model.Poster(x)))) ?? [])
        .OrderByDescending(x => x.Time).Select(x => x.Item).ToArray();
    public void Start(CaptureMode mode) { if (CanCapture) _capture.Start(mode, CaptureDoor.Island, Delay); }
    public void OpenRecent(string id)
    {
        if (id.StartsWith("recording/", StringComparison.Ordinal) && Guid.TryParse(id[10..], out var video) && _recording?.Model?.Recent.FirstOrDefault(x => x.Id == video) is { } entry)
        { _recording.Open(entry); return; }
        if (!Guid.TryParse(id, out var key) || _capture.RecentModel is not { } model) return;
        if (!ReferenceEquals(_recentModel, model)) { _recentModel = model; _recentView = new CaptureRecentView(model); }
        _ = model.OpenAsync(key);
        _showingRecent = true;
        _body.Content = _recentView;
        _header.Content = _recentView!.Header;
        ShowPage();
    }
    public void OpenFolder()
    {
        if (_capture.Model is not { } model)
        {
            if (_recording?.Model is { } recording && !CaptureFiles.OpenFolder(recording.SaveFolder)) recording.Report("Open folder failed");
            return;
        }
        string? folder = model.Recent.FirstOrDefault(x => x.SavedFile != null)?.SavedFile is { } saved ? Path.GetDirectoryName(saved) : model.SaveFolder;
        if (!CaptureFiles.OpenFolder(folder)) _capture.Report("Open folder failed", failed: true);
    }
}
