// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Chippi.Capture;
using Chippi.Media;
using Chippi.UI.Capture;
using CaptureMode = Chippi.Capture.CaptureMode;

namespace Chippi.Features.Recording.Tests;

internal sealed class FakeLayer : ICaptureLayer
{
    public List<CaptureRequest> Requests { get; } = [];
    public FakeSession? Session { get; private set; }
    public ICaptureSession? Current => Session is { Closed: false } s ? s : null;
    public PixelRect? LastRegion { get; set; }

    public IDisposable Use() => new Use();

    public ICaptureSession? Start(CaptureRequest request)
    {
        if (Current is not null) return null;
        Requests.Add(request);
        return Session = new FakeSession(request);
    }

    public ICaptureSession? Reopen(CapturedImage image, CaptureDoor door) => null;
}

internal sealed class Use : IDisposable { public void Dispose() { } }

internal sealed class FakeSession(CaptureRequest request) : ICaptureSession
{
    /// <summary>Main is 0,0 3440×1440 (monitor 1); Left is -1920,360 1920×1080 (monitor 2).</summary>
    public static readonly Freeze Screens = new(
    [
        new CapturedScreen(@"\\?\DISPLAY#0", 1, new PixelRect(0, 0, 3440, 1440), 96, new byte[3440 * 1440 * 4]),
        new CapturedScreen(@"\\?\DISPLAY#1", 2, new PixelRect(-1920, 360, 1920, 1080), 144, new byte[1920 * 1080 * 4]),
    ], [], DateTimeOffset.UnixEpoch);

    public CaptureRequest Request { get; } = request;
    public Freeze? Freeze => Screens;
    public Selection? Selection { get; private set; }
    public IReadOnlyList<CaptureSurface> Surfaces => [];
    public bool Tooling { get; set; }
    public bool Closed { get; private set; }
    public CaptureEnd? End { get; private set; }
    public FrameworkElement? Output { get; private set; }
    public int BarsSet { get; private set; }
    public event Action<Selection>? Selected;
    public event Action<PixelPoint, uint>? Picked { add { } remove { } }
    public event Action<KeyEventArgs>? KeyDown { add { } remove { } }
    public event Action? Frozen { add { } remove { } }
    public event Action<CaptureEnd>? ClosedEvent;
    event Action<CaptureEnd>? ICaptureSession.Closed { add => ClosedEvent += value; remove => ClosedEvent -= value; }

    public void Select(PixelRect rect, int display = 0, FrozenWindow? window = null)
    {
        var screen = Screens.Screens[display];
        Selection = new Selection(rect, Screens, Request.Mode, null, window, false, screen);
        Selected?.Invoke(Selection);
    }

    public void SetBars(FrameworkElement? tools, FrameworkElement? output)
    {
        Output = output;
        BarsSet++;
    }

    public void Close(CaptureEnd end = CaptureEnd.Finished, bool handBack = true)
    {
        if (Closed) return;
        Closed = true;
        End = end;
        ClosedEvent?.Invoke(end);
    }
}

internal sealed class FakeFrame(TimeSpan time) : IVideoFrame
{
    public TimeSpan Time { get; } = time;
    public void Dispose() { }
}

internal sealed class FakeVideo(int width, int height) : IVideoSource
{
    public int Width { get; } = width;
    public int Height { get; } = height;
    public bool Disposed { get; private set; }
    public event Action<IVideoFrame>? FrameArrived;
    public event Action<string>? Ended;
    public void Send(TimeSpan at) => FrameArrived?.Invoke(new FakeFrame(at));
    public void End(string because) => Ended?.Invoke(because);
    public void Start() { }
    public void Dispose() => Disposed = true;
}

internal sealed class FakeAudio(string name) : IAudioSource
{
    public string Name { get; } = name;
    public event Action<TimeSpan, float[]>? PacketArrived { add { } remove { } }
    public event Action<string>? Ended { add { } remove { } }
    public void Start() { }
    public void Dispose() { }
}

internal sealed class FakeWriter : IMediaWriter, IMediaWriterFactory
{
    public int Frames { get; private set; }
    public VideoFormat? Format { get; private set; }
    public event Action<Exception>? Failed { add { } remove { } }

    public IMediaWriter Create(string path, VideoFormat format)
    {
        Frames = 0;
        Format = format;
        File.WriteAllText(path, "mp4");
        return this;
    }

    public void WriteVideo(IVideoFrame frame, TimeSpan at) { Frames++; frame.Dispose(); }
    public void WriteAudio(AudioBlock block) { }
    public Task<WriteResult> FinishAsync(TimeSpan duration) => Task.FromResult(new WriteResult(Frames, true));
    public Task AbortAsync() => Task.CompletedTask;
    public void Dispose() { }
}

internal sealed class FakePc : IRecordingPc, IDisposable
{
    public readonly string Folder = Path.Combine(Path.GetTempPath(), "chippi-recording-tests", Guid.NewGuid().ToString("N"));

    public FakePc() => Directory.CreateDirectory(Folder);

    public string? Missing { get; set; }
    public FakeWriter Writer { get; } = new();
    public IMediaWriterFactory Writers => Writer;
    public FakeVideo? Video { get; private set; }
    public (nint Monitor, Chippi.Media.Capture.PixelRect? Region, bool Cursor)? DisplayAsked { get; private set; }
    public nint WindowAsked { get; private set; }
    public bool RefuseWindow { get; set; }
    public List<FakeAudio> Audio { get; } = [];
    public List<string> Temps { get; } = [];
    public List<(RecordingTarget Target, FakeOverlay Overlay)> Overlays { get; } = [];
    public List<(string Kind, string Target, TrimRange Span)> Work { get; } = [];

    public IVideoSource Display(nint monitor, Chippi.Media.Capture.PixelRect? region, bool cursor)
    {
        DisplayAsked = (monitor, region, cursor);
        return Video = new FakeVideo(region?.Width ?? 3440, region?.Height ?? 1440);
    }

    public IVideoSource Window(nint window, bool cursor)
    {
        WindowAsked = window;
        if (RefuseWindow) throw new ArgumentException("Windows refused the window.");
        return Video = new FakeVideo(800, 600);
    }

    public IAudioSource SystemSound() { var a = new FakeAudio("system"); Audio.Add(a); return a; }
    public IAudioSource Mic() { var a = new FakeAudio("mic"); Audio.Add(a); return a; }

    public string TempFile()
    {
        var path = Path.Combine(Folder, "temp", Guid.NewGuid().ToString("N") + ".mp4");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        Temps.Add(path);
        return path;
    }

    public string DefaultSaveFolder => Path.Combine(Folder, "Videos");

    public IRecordingOverlay ShowOverlay(RecordingTarget target, RecordingController recording)
    {
        var overlay = new FakeOverlay();
        Overlays.Add((target, overlay));
        return overlay;
    }

    public BitmapSource? Poster(string path) => null;

    public Task TrimAsync(string source, string target, TrimRange span, IProgress<double> progress, CancellationToken cancel)
    {
        Work.Add(("trim", target, span));
        File.WriteAllText(target, "trimmed");
        return Task.CompletedTask;
    }

    public Task<GifResult> GifAsync(string source, string target, TrimRange span, IProgress<double> progress, CancellationToken cancel)
    {
        Work.Add(("gif", target, span));
        File.WriteAllText(target, "gif");
        return Task.FromResult(new GifResult(10, span.Length > TimeSpan.FromSeconds(60)));
    }

    public void Dispose()
    {
        try { Directory.Delete(Folder, recursive: true); }
        catch (IOException) { }
    }
}

internal sealed class FakeOverlay : IRecordingOverlay
{
    public bool Disposed { get; private set; }
    public void Dispose() => Disposed = true;
}

internal sealed class FakeShell : IRecordingShell
{
    public List<(string Title, string Source, bool Failed)> Lines { get; } = [];
    public (string? Title, string? Source) Live { get; private set; }
    public bool CanOpenPage { get; set; } = true;
    public int PagesOpened { get; private set; }
    public List<(string File, TimeSpan Length)> Recent { get; } = [];
    public List<bool> Recording { get; } = [];

    public void Post(Action action) => action();
    public void Feedback(string title, string source, bool failed, Action? clicked = null) => Lines.Add((title, source, failed));
    void IRecordingShell.Live(string? title, string? source) => Live = (title, source);
    public bool OpenReview() { if (CanOpenPage) PagesOpened++; return CanOpenPage; }
    public void AddToRecent(string file, BitmapSource? poster, TimeSpan length, DateTimeOffset time) => Recent.Add((file, length));
    void IRecordingShell.Recording(bool on) => Recording.Add(on);
}
