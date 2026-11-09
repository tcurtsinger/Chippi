// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Runtime.InteropServices;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Foundation;
using Windows.Security.Authorization.AppCapabilityAccess;
using WinRT;

namespace Chippi.Media;

internal static class CaptureInterop
{
    private static readonly Guid ItemId = new("79c3f95b-31f7-4ec2-a464-632ef5d30760");

    public static GraphicsCaptureItem Item(RecordingSource source)
    {
        const string runtimeName = "Windows.Graphics.Capture.GraphicsCaptureItem";
        Marshal.ThrowExceptionForHR(WindowsCreateString(runtimeName, (uint)runtimeName.Length, out var name));
        nint factory = 0, item = 0;
        try
        {
            var iid = typeof(IGraphicsCaptureItemInterop).GUID;
            Marshal.ThrowExceptionForHR(RoGetActivationFactory(name, in iid, out factory));
            var interop = (IGraphicsCaptureItemInterop)Marshal.GetObjectForIUnknown(factory);
            try
            {
                if (source.Target == RecordingTarget.Window) interop.CreateForWindow(source.Handle, in ItemId, out item);
                else interop.CreateForMonitor(source.Handle, in ItemId, out item);
                return MarshalInspectable<GraphicsCaptureItem>.FromAbi(item);
            }
            finally { Marshal.ReleaseComObject(interop); }
        }
        finally { if (item != 0) Marshal.Release(item); if (factory != 0) Marshal.Release(factory); WindowsDeleteString(name); }
    }

    public static IDirect3DDevice ProjectDevice(nint dxgi)
    {
        Marshal.ThrowExceptionForHR(CreateDirect3D11DeviceFromDXGIDevice(dxgi, out var inspectable));
        try { return MarshalInterface<IDirect3DDevice>.FromAbi(inspectable); }
        finally { Marshal.Release(inspectable); }
    }

    public static unsafe nint Texture(IDirect3DSurface surface)
    {
        using var reference = MarshalInterface<IDirect3DSurface>.CreateMarshaler(surface);
        var accessId = new Guid("a9b3d012-3df2-4ee3-b8d1-8695f457d3c1");
        Marshal.ThrowExceptionForHR(Marshal.QueryInterface(reference.ThisPtr, in accessId, out var access));
        var iid = new Guid("6f15aaf2-d208-4e89-9ab4-489535d34f9c");
        try
        {
            nint texture = 0;
            var get = (delegate* unmanaged[Stdcall]<nint, Guid*, nint*, int>)(*(nint**)access)[3];
            Marshal.ThrowExceptionForHR(get(access, &iid, &texture)); return texture;
        }
        finally { Marshal.Release(access); }
    }

    public static unsafe void FrameInterval(GraphicsCaptureSession session, TimeSpan interval)
    {
        // Optional newer contract, queried through its documented SDK ABI without raising the app's Windows minimum.
        using var reference = MarshalInspectable<GraphicsCaptureSession>.CreateMarshaler(session);
        var id = new Guid("67c0ea62-1f85-5061-925a-239be0ac09cb");
        if (Marshal.QueryInterface(reference.ThisPtr, in id, out var pointer) < 0) return;
        try
        {
            var vtable = *(nint**)pointer;
            var set = (delegate* unmanaged[Stdcall]<nint, long, int>)vtable[7];
            Marshal.ThrowExceptionForHR(set(pointer, interval.Ticks));
        }
        finally { Marshal.Release(pointer); }
    }

    public static async Task<bool> BorderlessAsync(CancellationToken cancellationToken)
    {
        try
        {
            var operation = RequestBorderless();
            return await operation.AsTask(cancellationToken).ConfigureAwait(false) == AppCapabilityAccessStatus.Allowed;
        }
        catch (COMException) { return false; }
        catch (TypeLoadException) { return false; }
    }

    private static unsafe IAsyncOperation<AppCapabilityAccessStatus> RequestBorderless()
    {
        const string runtimeName = "Windows.Graphics.Capture.GraphicsCaptureAccess";
        Marshal.ThrowExceptionForHR(WindowsCreateString(runtimeName, (uint)runtimeName.Length, out var name));
        nint factory = 0, operation = 0;
        try
        {
            var id = new Guid("743ed370-06ec-5040-a58a-901f0f757095");
            Marshal.ThrowExceptionForHR(RoGetActivationFactory(name, in id, out factory));
            var request = (delegate* unmanaged[Stdcall]<nint, int, nint*, int>)(*(nint**)factory)[6];
            Marshal.ThrowExceptionForHR(request(factory, 0, &operation));
            return MarshalInterface<IAsyncOperation<AppCapabilityAccessStatus>>.FromAbi(operation);
        }
        finally { if (operation != 0) Marshal.Release(operation); if (factory != 0) Marshal.Release(factory); WindowsDeleteString(name); }
    }

    public static unsafe void DisableBorder(GraphicsCaptureSession session)
    {
        using var reference = MarshalInspectable<GraphicsCaptureSession>.CreateMarshaler(session);
        var id = new Guid("f2cdd966-22ae-5ea1-9596-3a289344c3be");
        if (Marshal.QueryInterface(reference.ThisPtr, in id, out var pointer) < 0) return;
        try
        {
            var set = (delegate* unmanaged[Stdcall]<nint, byte, int>)(*(nint**)pointer)[7];
            Marshal.ThrowExceptionForHR(set(pointer, 0));
        }
        finally { Marshal.Release(pointer); }
    }

    [ComImport, Guid("3628e81b-3cac-4c60-b7f4-23ce0e0c3356"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IGraphicsCaptureItemInterop
    {
        void CreateForWindow(nint window, in Guid iid, out nint item);
        void CreateForMonitor(nint monitor, in Guid iid, out nint item);
    }

    [DllImport("combase.dll", CharSet = CharSet.Unicode)] private static extern int WindowsCreateString(string text, uint length, out nint value);
    [DllImport("combase.dll")] private static extern int WindowsDeleteString(nint value);
    [DllImport("combase.dll")] private static extern int RoGetActivationFactory(nint name, in Guid iid, out nint factory);
    [DllImport("d3d11.dll")] private static extern int CreateDirect3D11DeviceFromDXGIDevice(nint device, out nint inspectable);
}
