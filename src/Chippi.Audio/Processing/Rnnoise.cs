// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Reflection;
using System.Runtime.InteropServices;
using NAudio.Wave;

namespace Chippi.Audio.Processing;

/// <summary>
/// The bundled classic RNNoise (native/rnnoise, BSD-3). The DLL for the process architecture ships
/// at <c>runtimes\win-x64\native</c> or <c>runtimes\win-arm64\native</c> next to Chippi.Audio.dll.
/// </summary>
internal static class RnnoiseNative
{
    private const string Dll = "rnnoise";

    /// <summary>Samples per frame for the built-in model: 480 (10 ms at 48 kHz mono).</summary>
    public const int FrameSize = 480;

    static RnnoiseNative() => NativeLibrary.SetDllImportResolver(typeof(RnnoiseNative).Assembly, Resolve);

    /// <summary>Where the DLL for this process's architecture is expected.</summary>
    public static string LibraryPath
    {
        get
        {
            var arch = RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "win-arm64" : "win-x64";
            var root = Path.GetDirectoryName(typeof(RnnoiseNative).Assembly.Location) ?? AppContext.BaseDirectory;
            return Path.Combine(root, "runtimes", arch, "native", "rnnoise.dll");
        }
    }

    // Only the bundled copy: a failed load throws rather than falling back to a search by bare name.
    private static IntPtr Resolve(string name, Assembly assembly, DllImportSearchPath? path) =>
        name == Dll ? NativeLibrary.Load(LibraryPath) : IntPtr.Zero;

#pragma warning disable SYSLIB1054 // blittable arguments only; DllImport keeps the project free of unsafe code
    /// <summary>Allocates a denoiser. <see cref="IntPtr.Zero"/> uses the built-in model.</summary>
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr rnnoise_create(IntPtr model);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern void rnnoise_destroy(IntPtr state);

    /// <summary>
    /// Denoises one 480-sample frame. Samples are in int16 range (about ±32768), not -1 to 1.
    /// <paramref name="output"/> and <paramref name="input"/> may be the same array. Returns the
    /// voice probability, 0 to 1.
    /// </summary>
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern float rnnoise_process_frame(IntPtr state, [Out] float[] output, [In] float[] input);
#pragma warning restore SYSLIB1054
}

/// <summary>
/// Noise suppression through RNNoise (Hopper's): runs a 48 kHz mono source one 480-sample frame at
/// a time, keeping partial frames across reads, scaling ×32768 in and back out. Reads its switch
/// live; off passes audio through. Dispose only after the render thread that pulls it has stopped.
/// </summary>
internal sealed class RnnoiseSuppressor : ISampleProvider, IDisposable
{
    private const int Frame = RnnoiseNative.FrameSize;
    private const float ToInt16 = 32768f;
    private const float FromInt16 = 1f / 32768f;

    private readonly ISampleProvider _source;
    private readonly MicSettings _settings;
    private readonly IntPtr _state;
    private readonly float[] _in = new float[Frame];
    private readonly float[] _work = new float[Frame];
    private int _inFill;
    private int _outPos;
    private int _outAvail;
    private bool _disposed;

    public RnnoiseSuppressor(ISampleProvider source, MicSettings settings)
    {
        if (source.WaveFormat.Channels != 1 || source.WaveFormat.SampleRate != 48000)
            throw new ArgumentException("Unsupported format", nameof(source));
        _source = source;
        _settings = settings;
        _state = RnnoiseNative.rnnoise_create(IntPtr.Zero);
        if (_state == IntPtr.Zero)
            throw new AudioFailure("Noise suppression failed");
    }

    public WaveFormat WaveFormat => _source.WaveFormat;

    /// <summary>Voice probability of the last frame, 0 to 1.</summary>
    public float VoiceProbability { get; private set; }

    public int Read(float[] buffer, int offset, int count)
    {
        if (!_settings.Suppression)
        {
            _inFill = _outPos = _outAvail = 0;
            return _source.Read(buffer, offset, count);
        }

        var produced = 0;
        while (produced < count)
        {
            if (_outAvail > 0)
            {
                var take = Math.Min(_outAvail, count - produced);
                Array.Copy(_work, _outPos, buffer, offset + produced, take);
                _outPos += take;
                _outAvail -= take;
                produced += take;
                continue;
            }

            var got = _source.Read(_in, _inFill, Frame - _inFill);
            if (got == 0)
                break; // source dry: the partial frame waits for the next read
            _inFill += got;
            if (_inFill < Frame)
                continue;

            for (var i = 0; i < Frame; i++)
                _work[i] = _in[i] * ToInt16;
            VoiceProbability = RnnoiseNative.rnnoise_process_frame(_state, _work, _work);
            for (var i = 0; i < Frame; i++)
                _work[i] *= FromInt16;
            _inFill = 0;
            _outPos = 0;
            _outAvail = Frame;
        }
        return produced;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        RnnoiseNative.rnnoise_destroy(_state);
    }
}
