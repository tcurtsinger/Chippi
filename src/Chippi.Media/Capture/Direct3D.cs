// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Runtime.InteropServices;
using Chippi.Media.Encoding;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Direct3D;
using Windows.Win32.Graphics.Direct3D11;
using Windows.Win32.Graphics.Dxgi;
using Windows.Win32.Graphics.Dxgi.Common;
using Windows.Win32.System.WinRT.Direct3D11;
using WinRT;

namespace Chippi.Media.Capture;

/// <summary>
/// One D3D11 device for a recording, shared by WGC's frame pool, the crop and Media Foundation's encoder. Its context
/// is multithread-protected: WGC's thread copies while the encoder's thread reads.
/// </summary>
internal sealed class Direct3D : IDisposable
{
    private static readonly Guid Texture2DId = typeof(ID3D11Texture2D).GUID;

    private Direct3D(ID3D11Device device, ID3D11DeviceContext context, IDirect3DDevice winrt)
    {
        Device = device;
        Context = context;
        WinRT = winrt;
    }

    public ID3D11Device Device { get; }

    public ID3D11DeviceContext Context { get; }

    /// <summary>The device as WGC and <c>MediaStreamSample</c> take it.</summary>
    public IDirect3DDevice WinRT { get; }

    /// <summary>
    /// The device's COM objects are made, and released, in the multithreaded apartment, whatever thread asks: WGC
    /// copies frames on its own free-threaded thread, and D3D11 objects can't be marshalled, so a device made on the UI
    /// thread (an STA) failed every frame there with an InvalidCastException (the Sandbox, 2026-11-07).
    /// </summary>
    public static Direct3D Create() => OnMta(CreateCore);

    internal static T OnMta<T>(Func<T> work) =>
        Thread.CurrentThread.GetApartmentState() == ApartmentState.MTA ? work() : Task.Run(work).GetAwaiter().GetResult();

    private static unsafe Direct3D CreateCore()
    {
        ID3D11Device device;
        ID3D11DeviceContext context;
        var hr = MediaPInvoke.D3D11CreateDevice(null, D3D_DRIVER_TYPE.D3D_DRIVER_TYPE_HARDWARE, HMODULE.Null,
            D3D11_CREATE_DEVICE_FLAG.D3D11_CREATE_DEVICE_BGRA_SUPPORT | D3D11_CREATE_DEVICE_FLAG.D3D11_CREATE_DEVICE_VIDEO_SUPPORT,
            null, 0, MediaPInvoke.D3D11_SDK_VERSION, out device, null, out context);
        if (hr.Failed)
        {
            // WARP, for a PC (or Sandbox) without a usable GPU.
            MediaPInvoke.D3D11CreateDevice(null, D3D_DRIVER_TYPE.D3D_DRIVER_TYPE_WARP, HMODULE.Null,
                D3D11_CREATE_DEVICE_FLAG.D3D11_CREATE_DEVICE_BGRA_SUPPORT, null, 0, MediaPInvoke.D3D11_SDK_VERSION,
                out device, null, out context).ThrowOnFailure();
        }
        ((ID3D11Multithread)context).SetMultithreadProtected(true);
        MediaPInvoke.CreateDirect3D11DeviceFromDXGIDevice((IDXGIDevice)device, out var inspectable).ThrowOnFailure();
        return new Direct3D(device, context, FromInspectable<IDirect3DDevice>(inspectable));
    }

    /// <summary>A texture the crop writes into and the encoder reads.</summary>
    public ID3D11Texture2D CreateTexture(int width, int height)
    {
        var desc = new D3D11_TEXTURE2D_DESC
        {
            Width = (uint)width,
            Height = (uint)height,
            MipLevels = 1,
            ArraySize = 1,
            Format = DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_UNORM,
            SampleDesc = new DXGI_SAMPLE_DESC { Count = 1 },
            Usage = D3D11_USAGE.D3D11_USAGE_DEFAULT,
            BindFlags = D3D11_BIND_FLAG.D3D11_BIND_SHADER_RESOURCE | D3D11_BIND_FLAG.D3D11_BIND_RENDER_TARGET,
        };
        Device.CreateTexture2D(desc, null, out var texture);
        return texture;
    }

    private ID3D11Texture2D? _staging;
    private (int Width, int Height) _stagingSize;

    /// <summary>
    /// A frame in <paramref name="texture"/> as the encoder takes it: the texture itself when the GPU's encoders may be
    /// used, or its pixels read back into memory for Windows' software encoder, which can't take GPU surfaces (in the
    /// Sandbox, 2026-11-07: "no samples were processed by the sink"). <paramref name="giveBack"/> gets the texture once
    /// the encoder, or the read, is done with it.
    /// </summary>
    public IEncodableFrame Frame(TimeSpan time, ID3D11Texture2D texture, int width, int height, Action<ID3D11Texture2D> giveBack)
    {
        if (MediaSupport.Hardware) return new GpuFrame(giveBack, time, texture);
        try
        {
            return new BgraFrame(time, Read(texture, width, height), width);
        }
        finally
        {
            giveBack(texture);
        }
    }

    /// <summary>The texture's pixels, BGRA top-down. On one thread at a time (WGC's).</summary>
    public unsafe byte[] Read(ID3D11Texture2D texture, int width, int height)
    {
        if (_staging is null || _stagingSize != (width, height))
        {
            if (_staging is not null) Marshal.ReleaseComObject(_staging);
            var desc = new D3D11_TEXTURE2D_DESC
            {
                Width = (uint)width,
                Height = (uint)height,
                MipLevels = 1,
                ArraySize = 1,
                Format = DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_UNORM,
                SampleDesc = new DXGI_SAMPLE_DESC { Count = 1 },
                Usage = D3D11_USAGE.D3D11_USAGE_STAGING,
                CPUAccessFlags = D3D11_CPU_ACCESS_FLAG.D3D11_CPU_ACCESS_READ,
            };
            Device.CreateTexture2D(desc, null, out var staging);
            _staging = staging;
            _stagingSize = (width, height);
        }
        Context.CopyResource((ID3D11Resource)_staging, (ID3D11Resource)texture);
        D3D11_MAPPED_SUBRESOURCE mapped;
        Context.Map((ID3D11Resource)_staging, 0, D3D11_MAP.D3D11_MAP_READ, 0, &mapped);
        try
        {
            var pixels = new byte[width * height * 4];
            var row = width * 4;
            for (var y = 0; y < height; y++)
                new ReadOnlySpan<byte>((byte*)mapped.pData + (long)y * mapped.RowPitch, row).CopyTo(pixels.AsSpan(y * row, row));
            return pixels;
        }
        finally
        {
            Context.Unmap((ID3D11Resource)_staging, 0);
        }
    }

    public static IDirect3DSurface Surface(ID3D11Texture2D texture)
    {
        MediaPInvoke.CreateDirect3D11SurfaceFromDXGISurface((IDXGISurface)texture, out var inspectable).ThrowOnFailure();
        return FromInspectable<IDirect3DSurface>(inspectable);
    }

    /// <summary>The D3D11 texture behind a WGC frame's surface. The caller releases it.</summary>
    public static unsafe ID3D11Texture2D Texture(IDirect3DSurface surface)
    {
        var access = surface.As<IDirect3DDxgiInterfaceAccess>();
        var id = Texture2DId;
        access.GetInterface(&id, out var pointer);
        try
        {
            return (ID3D11Texture2D)Marshal.GetObjectForIUnknown((nint)pointer);
        }
        finally
        {
            Marshal.Release((nint)pointer);
        }
    }

    private static T FromInspectable<T>(Windows.Win32.System.WinRT.IInspectable inspectable)
    {
        var pointer = Marshal.GetIUnknownForObject(inspectable);
        try
        {
            return MarshalInterface<T>.FromAbi(pointer);
        }
        finally
        {
            Marshal.Release(pointer);
            Marshal.ReleaseComObject(inspectable);
        }
    }

    public void Dispose() => OnMta(() =>
    {
        if (_staging is not null) Marshal.ReleaseComObject(_staging);
        (WinRT as IDisposable)?.Dispose();
        Marshal.ReleaseComObject(Context);
        Marshal.ReleaseComObject(Device);
        return true;
    });
}
