// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.Media.Capture;
using Chippi.Media.Encoding;
using Windows.Win32.Graphics.Direct3D11;
using Xunit.Abstractions;

namespace Chippi.Media.Tests;

/// <summary>
/// Frames as WGC hands them over, in D3D11 textures, through the real writer on both encoder paths: the GPU's (the
/// texture goes in as is) and Windows' software one (read back into memory first; in the Sandbox, GPU surfaces gave
/// "no samples were processed by the sink"). The textures are filled from memory: nothing reads the screen.
/// </summary>
[Collection(nameof(GpuFrameTests))]
[CollectionDefinition(nameof(GpuFrameTests), DisableParallelization = true)]
public sealed class GpuFrameTests(ITestOutputHelper output) : IDisposable
{
    private const int W = 320;
    private const int H = 180;
    private readonly string _folder = Directory.CreateTempSubdirectory("chippi-media-gpu-").FullName;

    /// <summary>Frame <paramref name="index"/>: blue 200 with a green 40 × 40 square 4 px further right each frame, at y 60.</summary>
    private static byte[] Pixels(int index)
    {
        var pixels = new byte[W * H * 4];
        for (var i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = 200;
            pixels[i + 3] = 255;
        }
        var x0 = 20 + (index * 4);
        for (var y = 60; y < 100; y++)
            for (var x = x0; x < x0 + 40; x++)
            {
                var at = ((y * W) + x) * 4;
                pixels[at] = 0;
                pixels[at + 1] = 220;
            }
        return pixels;
    }

    private static unsafe ID3D11Texture2D Upload(Direct3D d3d, byte[] pixels)
    {
        var texture = d3d.CreateTexture(W, H);
        fixed (byte* data = pixels)
            d3d.Context.UpdateSubresource((ID3D11Resource)texture, 0, null, data, W * 4, 0);
        return texture;
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Texture_frames_encode_and_decode_on_both_encoder_paths(bool hardware)
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
            var path = Path.Combine(_folder, $"gpu-{hardware}.mp4");
            using var d3d = Direct3D.Create();
            int givenBack = 0;
            using (var writer = new Mp4Writer(path, new VideoFormat(W, H, 30, false), queue: 60))
            {
                for (var i = 0; i < 30; i++)
                {
                    var at = TimeSpan.FromTicks(i * TimeSpan.TicksPerSecond / 30);
                    var frame = d3d.Frame(at, Upload(d3d, Pixels(i)), W, H, t => { givenBack++; System.Runtime.InteropServices.Marshal.ReleaseComObject(t); });
                    Assert.IsType(hardware ? typeof(GpuFrame) : typeof(BgraFrame), frame);
                    writer.WriteVideo(frame, at);
                }
                var result = await writer.FinishAsync(TimeSpan.FromSeconds(1));
                Assert.True(result.Playable);
                Assert.Equal(30, result.VideoFrames);
            }
            Assert.Equal(30, givenBack);

            using var reader = MediaReader.Open(path);
            Assert.Equal((W, H), (reader.Width, reader.Height));
            var frameBytes = new byte[reader.Stride * H];
            int frames = 0;
            while (reader.ReadFrame(frameBytes) is not null) frames++;
            Assert.InRange(frames, 29, 31);
            // The last frame's square is at x 136..176: inside it green, left of it blue, both upright.
            int Px(int x, int y, int c) => frameBytes[(y * reader.Stride) + (x * 4) + c];
            Assert.InRange(Px(156, 80, 1), 180, 255);
            Assert.InRange(Px(156, 80, 0), 0, 60);
            Assert.InRange(Px(60, 80, 0), 160, 240);
            Assert.InRange(Px(156, 140, 1), 0, 60);
            output.WriteLine($"{(hardware ? "GPU" : "software")} path: {frames} frames, {new FileInfo(path).Length} bytes.");
        }
        finally
        {
            MediaSupport.Hardware = was;
        }
    }

    [Fact]
    public void A_device_made_on_a_UI_thread_works_on_WGC_s_thread()
    {
        // Chippi starts recordings on its UI thread (an STA); WGC hands frames over on a free-threaded one.
        Direct3D? d3d = null;
        var ui = new Thread(() => d3d = Direct3D.Create());
        ui.SetApartmentState(ApartmentState.STA);
        ui.Start();
        ui.Join();
        var texture = Upload(d3d!, Pixels(0));
        var pixels = d3d!.Read(texture, W, H);
        Assert.Equal(200, pixels[((10 * W) + 10) * 4]);
        Assert.Equal(220, pixels[((80 * W) + 40) * 4 + 1]);
        System.Runtime.InteropServices.Marshal.ReleaseComObject(texture);
        var dispose = new Thread(d3d.Dispose);
        dispose.SetApartmentState(ApartmentState.STA);
        dispose.Start();
        dispose.Join();
    }

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); }
        catch (IOException) { }
    }
}
