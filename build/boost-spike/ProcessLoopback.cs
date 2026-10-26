// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Diagnostics;
using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using NAudio.Wave;

namespace BoostSpike;

/// <summary>One process tree's audio through Windows' process loopback (build 20348+).</summary>
internal sealed class ProcessCapture : IDisposable
{
    public static readonly WaveFormat Format = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);

    private readonly AudioClient _client;
    private readonly Thread _thread;
    private volatile bool _stop;

    /// <summary>Interleaved stereo samples, frame count, QPC ticks (Stopwatch) at the packet's first frame, discontinuity flag.</summary>
    public event Action<float[], int, long, bool>? Data;

    public int Discontinuities { get; private set; }
    public long Frames { get; private set; }

    private ProcessCapture(AudioClient client)
    {
        _client = client;
        _thread = new Thread(Run) { IsBackground = true, Priority = ThreadPriority.Highest };
    }

    public static async Task<ProcessCapture> Create(int pid, bool includeTree)
    {
        var args = new ActivationParams { Type = 1, Pid = (uint)pid, Mode = includeTree ? 0 : 1 };
        var size = Marshal.SizeOf<ActivationParams>();
        var blob = Marshal.AllocHGlobal(size);
        var variant = Marshal.AllocHGlobal(Marshal.SizeOf<BlobVariant>());
        try
        {
            Marshal.StructureToPtr(args, blob, false);
            Marshal.StructureToPtr(new BlobVariant { Vt = 65, Size = (uint)size, Data = blob }, variant, false);
            var iid = typeof(IAudioClient).GUID;
            var done = new Completion();
            Native.ActivateAudioInterfaceAsync(@"VAD\Process_Loopback", ref iid, variant, done, out _);
            var client = (IAudioClient)await done.Result.Task;
            var audio = new AudioClient(client);
            const AudioClientStreamFlags flags = AudioClientStreamFlags.Loopback | (AudioClientStreamFlags)0x80000000 | (AudioClientStreamFlags)0x08000000;
            audio.Initialize(AudioClientShareMode.Shared, flags, 200_000, 0, Format, Guid.Empty);
            return new ProcessCapture(audio);
        }
        finally
        {
            Marshal.FreeHGlobal(variant);
            Marshal.FreeHGlobal(blob);
        }
    }

    public void Start()
    {
        _client.Start();
        _thread.Start();
    }

    private void Run()
    {
        var capture = _client.AudioCaptureClient;
        while (!_stop)
        {
            Thread.Sleep(2);
            while (capture.GetNextPacketSize() > 0)
            {
                var data = capture.GetBuffer(out var frames, out var flags, out _, out var qpc);
                var samples = new float[frames * 2];
                var silent = (flags & AudioClientBufferFlags.Silent) != 0;
                if (!silent)
                    Marshal.Copy(data, samples, 0, samples.Length);
                capture.ReleaseBuffer(frames);
                var discontinuity = (flags & AudioClientBufferFlags.DataDiscontinuity) != 0;
                if (discontinuity && Frames > 0)
                    Discontinuities++;
                Frames += frames;
                // Packet QPC is in 100 ns units; convert to Stopwatch ticks.
                var ticks = qpc == 0 ? Stopwatch.GetTimestamp() : (long)(qpc / 1e7 * Stopwatch.Frequency);
                Data?.Invoke(samples, frames, ticks, discontinuity);
            }
        }
    }

    public void Dispose()
    {
        _stop = true;
        if (_thread.IsAlive)
            _thread.Join(1000);
        _client.Stop();
        _client.Dispose();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ActivationParams
    {
        public int Type;
        public uint Pid;
        public int Mode;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct BlobVariant
    {
        [FieldOffset(0)] public ushort Vt;
        [FieldOffset(8)] public uint Size;
        [FieldOffset(16)] public IntPtr Data;
    }

    private sealed class Completion : IActivateAudioInterfaceCompletionHandler, IAgileObject
    {
        public TaskCompletionSource<object> Result { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void ActivateCompleted(IActivateAudioInterfaceAsyncOperation operation)
        {
            operation.GetActivateResult(out var hr, out var unknown);
            if (hr < 0)
                Result.SetException(Marshal.GetExceptionForHR(hr)!);
            else
                Result.SetResult(unknown);
        }
    }
}

[ComImport, Guid("41D949AB-9862-444A-80F6-C261334DA5EB"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IActivateAudioInterfaceCompletionHandler
{
    void ActivateCompleted(IActivateAudioInterfaceAsyncOperation operation);
}

[ComImport, Guid("72A22D78-CDE4-431D-B8CC-843A71199B6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IActivateAudioInterfaceAsyncOperation
{
    void GetActivateResult(out int hr, [MarshalAs(UnmanagedType.IUnknown)] out object unknown);
}

[ComImport, Guid("94ea2b94-e9cc-49e0-c0ff-ee64ca8f5b90"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAgileObject;

internal static class Native
{
    [DllImport("Mmdevapi.dll", ExactSpelling = true, PreserveSig = false)]
    public static extern void ActivateAudioInterfaceAsync([MarshalAs(UnmanagedType.LPWStr)] string path, ref Guid iid, IntPtr parameters,
        IActivateAudioInterfaceCompletionHandler handler, out IActivateAudioInterfaceAsyncOperation operation);
}
