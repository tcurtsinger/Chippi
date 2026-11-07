// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Runtime.InteropServices;
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

    public static unsafe Direct3D Create()
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

    public void Dispose()
    {
        (WinRT as IDisposable)?.Dispose();
        Marshal.ReleaseComObject(Context);
        Marshal.ReleaseComObject(Device);
    }
}
