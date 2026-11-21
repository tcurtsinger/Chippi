// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Runtime.InteropServices;
using Chippi.Capture;
using Windows.Graphics;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Win32;
using Windows.Win32.Graphics.Direct3D;
using Windows.Win32.Graphics.Direct3D11;
using Windows.Win32.Graphics.Dxgi.Common;

namespace Chippi.Media;

/// <summary>
/// Live frames of an area of a display from Windows Graphics Capture, on <see cref="CaptureInterop"/> as Screen recording
/// uses it: a free-threaded frame pool, borderless where Windows allows, cursor capture off. Each new frame's area is copied
/// into a staging texture and read back to tight BGRA rows. Only the newest frame is kept; a worker thread delivers it.
/// </summary>
public sealed class WgcAreaFrames : IAreaFrames
{
    private readonly object _gate = new();
    private readonly AutoResetEvent _wake = new(false);
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Direct3D11CaptureFrame? _latest;
    private Direct3D11CaptureFramePool? _pool;
    private GraphicsCaptureSession? _session;
    private AreaFramesRequest? _request;
    private SizeInt32 _size;
    private Thread? _worker;
    private volatile bool _stopping;
    private int _started, _failed, _disposed;

    public event Action<AreaFrame>? Frame;
    public event Action<string>? Failed;

    /// <summary>Test seam: skip the borderless access request.</summary>
    internal bool SkipBorderAccessForFixture { get; init; }

    public async Task StartAsync(AreaFramesRequest request, CancellationToken cancellationToken = default)
    {
        if (!request.IsValid) throw new ArgumentException("The area is outside its display.", nameof(request));
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        if (Interlocked.Exchange(ref _started, 1) != 0) throw new InvalidOperationException("Already started.");
        _request = request;
        bool borderless = !SkipBorderAccessForFixture && await CaptureInterop.BorderlessAsync(cancellationToken).ConfigureAwait(false);
        _worker = new Thread(() => Run(borderless)) { IsBackground = true, Name = "Chippi area frames" };
        _worker.SetApartmentState(ApartmentState.MTA);
        _worker.Start();
        await _ready.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private void Fail(string reason)
    {
        if (Interlocked.Exchange(ref _failed, 1) != 0) return;
        _stopping = true; _wake.Set();
        try { Failed?.Invoke(reason); } catch { /* a receiver's fault must not stop the worker's cleanup */ }
    }

    private void OnClosed(GraphicsCaptureItem item, object args) => Fail("Source closed");

    private void OnFrame(Direct3D11CaptureFramePool pool, object args)
    {
        try
        {
            var frame = pool.TryGetNextFrame();
            if (frame == null) return;
            lock (_gate)
            {
                if (_stopping) { frame.Dispose(); return; }
                _latest?.Dispose();
                _latest = frame;
            }
            _wake.Set();
        }
        catch (Exception ex) when (ex is COMException or ObjectDisposedException) { Fail("Capture stopped"); }
    }

    private Direct3D11CaptureFrame? Take() { lock (_gate) { var frame = _latest; _latest = null; return frame; } }

    private unsafe void Run(bool borderless)
    {
        bool initialized = false;
        ID3D11Device* device = null; ID3D11DeviceContext* context = null; ID3D11Texture2D* staging = null;
        GraphicsCaptureItem? item = null;
        try
        {
            Marshal.ThrowExceptionForHR(RoInitialize(1)); initialized = true;
            if (!GraphicsCaptureSession.IsSupported()) throw new NotSupportedException("Windows screen capture is not available.");
            var request = _request!;
            var area = request.AreaOnDisplay;
            item = CaptureInterop.Item(new RecordingSource(RecordingTarget.Screen, request.Monitor, "", request.Display.Width, request.Display.Height));
            item.Closed += OnClosed;
            _size = item.Size;
            if (!AreaReadback.Fits(area.X, area.Y, area.Width, area.Height, _size.Width, _size.Height))
                throw new InvalidOperationException("The area is outside the display.");

            ID3D11Device* created = null; ID3D11DeviceContext* immediate = null;
            PInvoke.D3D11CreateDevice(null, D3D_DRIVER_TYPE.D3D_DRIVER_TYPE_HARDWARE, default,
                D3D11_CREATE_DEVICE_FLAG.D3D11_CREATE_DEVICE_BGRA_SUPPORT, null, 0, 7, &created, null, &immediate).ThrowOnFailure();
            device = created; context = immediate;
            var threadingId = typeof(ID3D11Multithread).GUID;
            ID3D11Multithread* threading = null;
            if (context->QueryInterface(&threadingId, (void**)&threading).Succeeded) { threading->SetMultithreadProtected(true); threading->Release(); }
            var desc = new D3D11_TEXTURE2D_DESC
            {
                Width = (uint)area.Width, Height = (uint)area.Height, MipLevels = 1, ArraySize = 1,
                Format = DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_UNORM, SampleDesc = new DXGI_SAMPLE_DESC { Count = 1 },
                Usage = D3D11_USAGE.D3D11_USAGE_STAGING, CPUAccessFlags = D3D11_CPU_ACCESS_FLAG.D3D11_CPU_ACCESS_READ,
            };
            ID3D11Texture2D* texture = null;
            device->CreateTexture2D(in desc, null, &texture); // a failure throws (the binding is not PreserveSig)
            if (texture == null) throw new InvalidOperationException("Capture failed");
            staging = texture;

            var dxgiId = new Guid("54ec77fa-1377-44e6-8c32-88fd5f44c84c");
            Marshal.ThrowExceptionForHR(Marshal.QueryInterface((nint)device, in dxgiId, out var dxgi));
            IDirect3DDevice projection;
            try { projection = CaptureInterop.ProjectDevice(dxgi); } finally { Marshal.Release(dxgi); }
            using (projection)
            {
                _pool = Direct3D11CaptureFramePool.CreateFreeThreaded(projection, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, _size);
                _pool.FrameArrived += OnFrame;
                _session = _pool.CreateCaptureSession(item);
                _session.IsCursorCaptureEnabled = false;
                if (borderless) CaptureInterop.DisableBorder(_session);
                _session.StartCapture();
                _ready.TrySetResult();
                Deliver(context, staging, area);
            }
        }
        catch (Exception ex)
        {
            if (!_ready.TrySetException(ex) && !_stopping) Fail("Capture failed");
        }
        finally
        {
            _stopping = true;
            if (_pool != null) _pool.FrameArrived -= OnFrame;
            if (item != null) item.Closed -= OnClosed;
            _session?.Dispose(); _session = null;
            _pool?.Dispose(); _pool = null;
            Take()?.Dispose();
            if (staging != null) staging->Release();
            if (context != null) context->Release();
            if (device != null) device->Release();
            if (initialized) RoUninitialize();
        }
    }

    private unsafe void Deliver(ID3D11DeviceContext* context, ID3D11Texture2D* staging, PixelRect area)
    {
        int row = area.Width * AreaReadback.BytesPerPixel;
        while (!_stopping)
        {
            _wake.WaitOne(250);
            if (_stopping) break;
            using var frame = Take();
            if (frame == null) continue;
            var content = frame.ContentSize;
            if (content.Width != _size.Width || content.Height != _size.Height)
            { Fail(AreaFrameReasons.DisplayChanged); break; }
            nint source = 0;
            try
            {
                source = CaptureInterop.Texture(frame.Surface);
                var box = new D3D11_BOX { left = (uint)area.X, top = (uint)area.Y, right = (uint)area.Right, bottom = (uint)area.Bottom, front = 0, back = 1 };
                context->CopySubresourceRegion((ID3D11Resource*)staging, 0, 0, 0, 0, (ID3D11Resource*)source, 0, &box);
                D3D11_MAPPED_SUBRESOURCE mapped = default;
                context->Map((ID3D11Resource*)staging, 0, D3D11_MAP.D3D11_MAP_READ, 0, &mapped); // a failure throws a COMException, caught below
                if (mapped.pData == null) { Fail("Capture failed"); break; }
                var bytes = new byte[row * area.Height];
                try { AreaReadback.CopyRows((byte*)mapped.pData, (int)mapped.RowPitch, area.Width, area.Height, bytes); }
                finally { context->Unmap((ID3D11Resource*)staging, 0); }
                AreaReadback.ForceOpaque(bytes);
                var time = frame.SystemRelativeTime;
                try { Frame?.Invoke(new AreaFrame(area.Width, area.Height, bytes, time)); }
                catch { /* a receiver's fault must not end capture */ }
            }
            catch (Exception ex) when (ex is COMException or ObjectDisposedException) { Fail("Capture failed"); break; }
            finally { if (source != 0) Marshal.Release(source); }
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _stopping = true; _wake.Set();
        var worker = _worker;
        if (worker != null && worker != Thread.CurrentThread) worker.Join(TimeSpan.FromSeconds(2));
        _ready.TrySetCanceled();
    }

    [DllImport("combase.dll")] private static extern int RoInitialize(uint type);
    [DllImport("combase.dll")] private static extern void RoUninitialize();
}
