// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Win32;
using Windows.Win32.Graphics.Direct3D;
using Windows.Win32.Graphics.Direct3D11;
using Windows.Win32.Graphics.Dxgi.Common;

namespace Chippi.Media;

/// <summary>A bounded set of GPU canvases. The encoder returns a canvas only after releasing its sample.</summary>
internal sealed unsafe class RecordingGraphics : IDisposable
{
    private ID3D11Device* _device;
    private ID3D11DeviceContext* _context;
    private readonly ConcurrentQueue<Surface> _free = new();
    private readonly object _poolGate = new();
    private bool _disposed;
    public IDirect3DDevice Projection { get; }
    public nint Device => (nint)_device;
    public int Width { get; }
    public int Height { get; }

    public RecordingGraphics(int width, int height)
    {
        Width = width; Height = height;
        ID3D11Device* device = null; ID3D11DeviceContext* context = null;
        PInvoke.D3D11CreateDevice(null, D3D_DRIVER_TYPE.D3D_DRIVER_TYPE_HARDWARE, default,
            D3D11_CREATE_DEVICE_FLAG.D3D11_CREATE_DEVICE_BGRA_SUPPORT | D3D11_CREATE_DEVICE_FLAG.D3D11_CREATE_DEVICE_VIDEO_SUPPORT,
            null, 0, 7, &device, null, &context).ThrowOnFailure();
        _device = device; _context = context;
        try
        {
            var dxgiId = new Guid("54ec77fa-1377-44e6-8c32-88fd5f44c84c");
            Marshal.ThrowExceptionForHR(Marshal.QueryInterface((nint)device, in dxgiId, out var dxgi));
            try { Projection = CaptureInterop.ProjectDevice(dxgi); } finally { Marshal.Release(dxgi); }
            ID3D11Multithread* threading = null;
            var threadingId = typeof(ID3D11Multithread).GUID;
            if (_context->QueryInterface(&threadingId, (void**)&threading).Succeeded)
            { threading->SetMultithreadProtected(true); threading->Release(); }
            for (int i = 0; i < 4; i++) _free.Enqueue(CreateSurface());
        }
        catch { Dispose(); throw; }
    }

    private Surface CreateSurface()
    {
        var desc = new D3D11_TEXTURE2D_DESC
        {
            Width = (uint)Width, Height = (uint)Height, MipLevels = 1, ArraySize = 1,
            Format = DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_UNORM, SampleDesc = new DXGI_SAMPLE_DESC { Count = 1 },
            Usage = D3D11_USAGE.D3D11_USAGE_DEFAULT,
            BindFlags = D3D11_BIND_FLAG.D3D11_BIND_RENDER_TARGET | D3D11_BIND_FLAG.D3D11_BIND_SHADER_RESOURCE,
        };
        ID3D11Texture2D* texture = null; ID3D11RenderTargetView* view = null;
        _device->CreateTexture2D(in desc, null, &texture);
        try { _device->CreateRenderTargetView((ID3D11Resource*)texture, null, &view); return new Surface(this, texture, view); }
        catch { texture->Release(); throw; }
    }

    public Surface? Copy(IDirect3DSurface source, int width, int height)
    {
        if (!_free.TryDequeue(out var target)) return null;
        nint from = 0;
        try
        {
            from = CaptureInterop.Texture(source);
            _context->ClearRenderTargetView(target.View, [0, 0, 0, 1]);
            var region = new D3D11_BOX { right = (uint)Math.Min(width, Width), bottom = (uint)Math.Min(height, Height), back = 1 };
            _context->CopySubresourceRegion((ID3D11Resource*)target.Texture, 0, 0, 0, 0, (ID3D11Resource*)from, 0, &region);
            _context->Flush();
            return target;
        }
        catch { target.Return(); throw; }
        finally { if (from != 0) Marshal.Release(from); }
    }

    internal Surface? FixtureFrame(int frame)
    {
        if (!_free.TryDequeue(out var target)) return null;
        _context->ClearRenderTargetView(target.View, frame % 2 == 0 ? [0f, 0f, 1f, 1f] : [1f, 0f, 0f, 1f]);
        _context->Flush();
        return target;
    }

    public void Dispose()
    {
        lock (_poolGate)
        {
            if (_disposed) return;
            _disposed = true;
            while (_free.TryDequeue(out var surface)) surface.Dispose();
        }
        Projection?.Dispose();
        if (_context != null) { _context->Release(); _context = null; }
        if (_device != null) { _device->Release(); _device = null; }
    }

    internal sealed class Surface(RecordingGraphics owner, ID3D11Texture2D* texture, ID3D11RenderTargetView* view) : IDisposable
    {
        private int _references = 1;
        public ID3D11Texture2D* Texture { get; } = texture;
        public ID3D11RenderTargetView* View { get; } = view;
        public Surface Retain() { Interlocked.Increment(ref _references); return this; }
        public void Return()
        {
            if (Interlocked.Decrement(ref _references) != 0) return;
            lock (owner._poolGate)
            {
                if (owner._disposed) Dispose();
                else { _references = 1; owner._free.Enqueue(this); }
            }
        }
        public void Dispose() { View->Release(); Texture->Release(); }
    }
}
