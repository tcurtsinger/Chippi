// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Diagnostics;
using Chippi.Media.Encoding;
using Xunit.Abstractions;

namespace Chippi.Media.Tests;

/// <summary>
/// The recorder with the real MP4 writer, as a recording runs: frames arrive on their own thread in real time, stamped on
/// the performance counter as WGC stamps them. Memory frames only: nothing reads the screen.
/// </summary>
[Collection(nameof(GpuFrameTests))]
public sealed class RecorderEncoderTests(ITestOutputHelper output) : IDisposable
{
    private const int W = 320;
    private const int H = 180;
    private readonly string _folder = Directory.CreateTempSubdirectory("chippi-media-rec-").FullName;

    private sealed class TimedSource : IVideoSource
    {
        private Thread? _thread;
        private volatile bool _stop;
        public int Width => W;
        public int Height => H;
        public event Action<IVideoFrame>? FrameArrived;
        public event Action<string>? Ended { add { } remove { } }

        public void Start()
        {
            _thread = new Thread(() =>
            {
                for (var i = 0; !_stop; i++)
                {
                    var pixels = new byte[W * H * 4];
                    for (var p = 0; p < pixels.Length; p += 4) { pixels[p] = (byte)(i * 3); pixels[p + 1] = 100; pixels[p + 3] = 255; }
                    var now = TimeSpan.FromTicks((long)(Stopwatch.GetTimestamp() * ((double)TimeSpan.TicksPerSecond / Stopwatch.Frequency)));
                    FrameArrived?.Invoke(new BgraFrame(now, pixels, W));
                    Thread.Sleep(16);
                }
            }) { IsBackground = true };
            _thread.Start();
        }

        public void Dispose()
        {
            _stop = true;
            _thread?.Join();
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_two_second_recording_of_live_frames_is_playable(bool hardware)
    {
        if (MediaSupport.MissingEncoder is { } missing)
        {
            output.WriteLine($"Not run: {missing}.");
            return;
        }
        var was = MediaSupport.Hardware;
        MediaSupport.Hardware = hardware;
        try
        {
            var path = Path.Combine(_folder, $"rec-{hardware}.mp4");
            using var recorder = new Recorder(new Mp4WriterFactory());
            Assert.Equal(StartResult.Started, recorder.Start(new RecordingRequest(new TimedSource(), [], path)));
            await Task.Delay(2000);
            var result = await recorder.StopAsync();
            Assert.NotNull(result);
            output.WriteLine($"{result}");
            Assert.True(result.Playable, result.Stopped);
            using var reader = MediaReader.Open(path);
            var frame = new byte[reader.Stride * H];
            int frames = 0;
            while (reader.ReadFrame(frame) is not null) frames++;
            output.WriteLine($"{(hardware ? "GPU" : "software")}: {frames} frames, {reader.Duration.TotalSeconds:F2} s");
            Assert.InRange(frames, 50, 66);
        }
        finally
        {
            MediaSupport.Hardware = was;
        }
    }

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); }
        catch (IOException) { }
    }
}
