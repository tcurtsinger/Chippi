// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Chippi.Media.Encoding;
using Windows.Graphics;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Media.Core;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Direct3D11;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.System.WinRT.Graphics.Capture;
using WinRT;

namespace Chippi.Media.Capture;

/// <summary>A rectangle in a display's physical pixels, from its top left.</summary>
public readonly record struct PixelRect(int X, int Y, int Width, int Height);

/// <summary>
/// Frames of a display, a region of one, or a window, through Windows Graphics Capture (screen-recording.md, Video;
/// architecture.md section 3). WGC hands over a frame for every change; only the frames the recorder keeps are
/// copied, on WGC's thread, into a texture of the fixed output size: a region is cropped on the GPU, and a window
/// that grows or shrinks is cropped or padded with black. The display changing, the window closing or the device
/// going ends the source.
/// </summary>
public sealed class WgcVideoSource : IVideoSource
{
    private static readonly Guid CaptureItemId = new("79C3F95B-31F7-4EC2-A464-632EF5D30760");
    private static readonly Guid Session3Id = new("F2CDD966-22AE-5EA1-9596-3A289344C3BE");

    private readonly Direct3D _d3d;
    private readonly GraphicsCaptureItem _item;
    private readonly PixelRect? _region;
    private readonly bool _window;
    private readonly ConcurrentBag<ID3D11Texture2D> _spare = [];
    private readonly object _gate = new();
    private Direct3D11CaptureFramePool? _pool;
    private GraphicsCaptureSession? _session;
    private SizeInt32 _poolSize;
    private ID3D11Texture2D? _black;
    private int _ended;
    private bool _disposed;

    private WgcVideoSource(Direct3D d3d, GraphicsCaptureItem item, PixelRect? region, bool window, bool cursor, bool border)
    {
        _d3d = d3d;
        _item = item;
        _region = region;
        _window = window;
        Cursor = cursor;
        Border = border;
        var size = region is { } r ? (r.Width, r.Height) : (item.Size.Width, item.Size.Height);
        Width = Even(size.Item1);
        Height = Even(size.Item2);
    }

    public int Width { get; }

    public int Height { get; }

    public bool Cursor { get; }

    /// <summary>Whether WGC was asked to draw its yellow border. Whether Windows honours false is checked in Sandbox.</summary>
    public bool Border { get; }

    /// <summary>What the border request came back with: null when Windows has no such switch (before build 20348).</summary>
    public bool? BorderSet { get; private set; }

    public event Action<IVideoFrame>? FrameArrived;

    public event Action<string>? Ended;

    public static bool IsSupported => GraphicsCaptureSession.IsSupported();

    /// <param name="monitor">The display's <c>HMONITOR</c>.</param>
    /// <param name="region">The part to record, in the display's physical pixels; null for all of it.</param>
    public static WgcVideoSource ForDisplay(nint monitor, PixelRect? region, bool cursor = true, bool border = false)
    {
        var d3d = Direct3D.Create();
        try
        {
            var item = CreateItem(interop => { var id = CaptureItemId; return Create(interop, (HMONITOR)monitor, id); });
            if (region is { } r && (r.X < 0 || r.Y < 0 || r.Width < 2 || r.Height < 2
                || r.X + r.Width > item.Size.Width || r.Y + r.Height > item.Size.Height))
                throw new ArgumentOutOfRangeException(nameof(region), "The region must lie on the display.");
            return new WgcVideoSource(d3d, item, region, window: false, cursor, border);
        }
        catch
        {
            d3d.Dispose();
            throw;
        }
    }

    /// <exception cref="ArgumentException">Windows refuses to capture the window ("Can't record this window").</exception>
    public static WgcVideoSource ForWindow(nint window, bool cursor = true, bool border = false)
    {
        var d3d = Direct3D.Create();
        try
        {
            var item = CreateItem(interop => { var id = CaptureItemId; return Create(interop, (HWND)window, id); });
            return new WgcVideoSource(d3d, item, null, window: true, cursor, border);
        }
        catch
        {
            d3d.Dispose();
            throw;
        }
    }

    private static unsafe nint Create(IGraphicsCaptureItemInterop interop, HMONITOR monitor, Guid id)
    {
        interop.CreateForMonitor(monitor, &id, out var item);
        return (nint)item;
    }

    private static unsafe nint Create(IGraphicsCaptureItemInterop interop, HWND window, Guid id)
    {
        interop.CreateForWindow(window, &id, out var item);
        return (nint)item;
    }

    private static GraphicsCaptureItem CreateItem(Func<IGraphicsCaptureItemInterop, nint> create)
    {
        using var factory = ActivationFactory.Get("Windows.Graphics.Capture.GraphicsCaptureItem");
        var interop = (IGraphicsCaptureItemInterop)Marshal.GetObjectForIUnknown(factory.ThisPtr);
        var pointer = create(interop);
        try
        {
            return GraphicsCaptureItem.FromAbi(pointer);
        }
        finally
        {
            Marshal.Release(pointer);
            Marshal.ReleaseComObject(interop);
        }
    }

    private static int Even(int value) => Math.Max(2, value & ~1);

    public void Start()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _poolSize = _item.Size;
            _pool = Direct3D11CaptureFramePool.CreateFreeThreaded(_d3d.WinRT, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, _poolSize);
            _pool.FrameArrived += OnFrameArrived;
            _item.Closed += OnItemClosed;
            _session = _pool.CreateCaptureSession(_item);
            _session.IsCursorCaptureEnabled = Cursor;
            BorderSet = TrySetBorder(_session, Border);
            _session.StartCapture();
        }
    }

    /// <summary>
    /// <c>GraphicsCaptureSession.IsBorderRequired</c> (Windows 11, build 20348 on) through its ABI, since the app builds
    /// against an older Windows SDK. Null when the session has no such switch.
    /// </summary>
    private static unsafe bool? TrySetBorder(GraphicsCaptureSession session, bool required)
    {
        var unknown = ((IWinRTObject)session).NativeObject.ThisPtr;
        var id = Session3Id;
        if (Marshal.QueryInterface(unknown, in id, out var session3) != 0) return null;
        try
        {
            // IInspectable's six slots, then get_IsBorderRequired, then put_IsBorderRequired.
            var vtable = *(nint**)session3;
            var put = (delegate* unmanaged[Stdcall]<nint, byte, int>)vtable[7];
            var get = (delegate* unmanaged[Stdcall]<nint, byte*, int>)vtable[6];
            if (put(session3, required ? (byte)1 : (byte)0) < 0) return null;
            byte now;
            return get(session3, &now) < 0 ? null : now != 0;
        }
        finally
        {
            Marshal.Release(session3);
        }
    }

    private void OnItemClosed(GraphicsCaptureItem sender, object args) => End(_window ? "Window closed" : "Display changed");

    private void OnFrameArrived(Direct3D11CaptureFramePool sender, object args)
    {
        try
        {
            using var frame = sender.TryGetNextFrame();
            if (frame is null || Volatile.Read(ref _ended) != 0) return;
            var content = frame.ContentSize;
            if (!_window && (content.Width != _poolSize.Width || content.Height != _poolSize.Height))
            {
                End("Display changed");
                return;
            }
            if (_window && (content.Width != _poolSize.Width || content.Height != _poolSize.Height) && content.Width > 0 && content.Height > 0)
            {
                // The window was resized: the pool follows; this frame still holds the old size, so it's used as is.
                _poolSize = content;
                sender.Recreate(_d3d.WinRT, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, content);
            }
            using var pending = new PendingFrame(this, frame);
            FrameArrived?.Invoke(pending);
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or ObjectDisposedException)
        {
            End("Recording stopped");
        }
    }

    private void End(string because)
    {
        if (Interlocked.Exchange(ref _ended, 1) == 0) Ended?.Invoke(because);
    }

    /// <summary>Copies a WGC frame into a texture of the output size. On WGC's thread, while the frame is current.</summary>
    private unsafe IEncodableFrame Copy(Direct3D11CaptureFrame frame)
    {
        var target = _spare.TryTake(out var spare) ? spare : _d3d.CreateTexture(Width, Height);
        var source = Direct3D.Texture(frame.Surface);
        try
        {
            var content = frame.ContentSize;
            int x = 0, y = 0, w, h;
            if (_region is { } r) (x, y, w, h) = (r.X, r.Y, Width, Height);
            else (w, h) = (Math.Min(Width, content.Width), Math.Min(Height, content.Height));
            if (_window && (w < Width || h < Height))
            {
                _black ??= _d3d.CreateTexture(Width, Height);
                _d3d.Context.CopyResource((ID3D11Resource)target, (ID3D11Resource)_black);
            }
            var box = new D3D11_BOX { left = (uint)x, top = (uint)y, front = 0, right = (uint)(x + w), bottom = (uint)(y + h), back = 1 };
            _d3d.Context.CopySubresourceRegion((ID3D11Resource)target, 0, 0, 0, 0, (ID3D11Resource)source, 0, &box);
        }
        finally
        {
            Marshal.ReleaseComObject(source);
        }
        return _d3d.Frame(frame.SystemRelativeTime, target, Width, Height, Return);
    }

    private void Return(ID3D11Texture2D texture)
    {
        lock (_gate)
        {
            if (_disposed) { Marshal.ReleaseComObject(texture); return; }
        }
        _spare.Add(texture);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _item.Closed -= OnItemClosed;
            if (_pool is not null) _pool.FrameArrived -= OnFrameArrived;
            _session?.Dispose();
            _pool?.Dispose();
        }
        // Textures still in the encoder's queue come back through Return and are released there.
        while (_spare.TryTake(out var texture)) Marshal.ReleaseComObject(texture);
        if (_black is not null) Marshal.ReleaseComObject(_black);
        _d3d.Dispose();
    }

    /// <summary>A frame as WGC delivered it: copied only if the writer keeps it.</summary>
    private sealed class PendingFrame(WgcVideoSource owner, Direct3D11CaptureFrame frame) : IEncodableFrame
    {
        public TimeSpan Time { get; } = frame.SystemRelativeTime;

        public IEncodableFrame Keep() => owner.Copy(frame);

        public MediaStreamSample ToSample(TimeSpan at) => throw new InvalidOperationException("Keep the frame first.");

        public void Dispose() { }
    }
}

/// <summary>A copied frame in one of a source's textures, given back when the encoder is done with it.</summary>
internal sealed class GpuFrame(Action<ID3D11Texture2D> giveBack, TimeSpan time, ID3D11Texture2D texture) : IEncodableFrame
{
    private IDirect3DSurface? _surface;
    private int _disposed;

    public TimeSpan Time { get; } = time;

    public IEncodableFrame Keep() => this;

    public MediaStreamSample ToSample(TimeSpan at)
    {
        _surface ??= Direct3D.Surface(texture);
        return MediaStreamSample.CreateFromDirect3D11Surface(_surface, at);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        (_surface as IDisposable)?.Dispose();
        giveBack(texture);
    }
}
