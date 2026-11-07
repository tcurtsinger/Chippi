// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Diagnostics;
using System.IO;
using Chippi.Island.Tests;
using Chippi.Media;
using Chippi.Media.Encoding;

namespace Chippi.Features.Recording.Tests;

/// <summary>
/// A recording started and stopped on the UI thread, as the controller does, with the real MP4 writer and memory frames
/// arriving in real time on their own thread. Nothing reads the screen.
/// </summary>
public sealed class UiThreadRecordingTests : IDisposable
{
    private const int W = 320, H = 180;
    private readonly string _folder = Directory.CreateTempSubdirectory("chippi-rec-ui-").FullName;

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

    [Fact]
    public async Task A_recording_started_on_the_UI_thread_is_playable()
    {
        if (MediaSupport.MissingEncoder is not null) return;
        var path = Path.Combine(_folder, "ui.mp4");
        Recorder recorder = null!;
        WpfTestThread.Run(() =>
        {
            recorder = new Recorder(new Mp4WriterFactory());
            Assert.Equal(StartResult.Started, recorder.Start(new RecordingRequest(new TimedSource(), [], path)));
        });
        await Task.Delay(2000);
        Task<RecordingResult?> stopping = null!;
        WpfTestThread.Run(() => stopping = recorder.StopAsync());
        var result = await stopping.WaitAsync(TimeSpan.FromSeconds(20));
        recorder.Dispose();
        Assert.NotNull(result);
        Assert.True(result.Playable, result.Stopped);
        Assert.InRange(result.Frames, 50, 66);
    }

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); }
        catch (IOException) { }
    }
}
