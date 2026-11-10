// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Chippi.Media;
using Windows.Media.Editing;
using Windows.Storage;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.Imaging;
using System.Runtime.InteropServices.WindowsRuntime;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Chippi.RecordingProbe;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length is not (1 or 2)) return 2;
        using var log = new StreamWriter(args[0]) { AutoFlush = true };
        var desktop = new StringBuilder(256);
        GetUserObjectInformation(GetThreadDesktop(GetCurrentThreadId()), 2, desktop, desktop.Capacity * 2, out _);
        bool sandbox = Environment.UserName == "WDAGUtilityAccount";
        bool ownedOffscreen = args.Length == 2 && args[1] == "--owned-offscreen";
        if (!sandbox && !ownedOffscreen && !desktop.ToString().StartsWith("ChippiFlash-", StringComparison.Ordinal))
        { log.WriteLine("REFUSED: probe requires Sandbox or its inactive private desktop."); return 2; }
        if (!sandbox && !ownedOffscreen) return Synthetic(log, args[0]);
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        int exit = 1;
        var window = new Window
        {
            Width = 320, Height = 180, Left = ownedOffscreen ? -32000 : 0, Top = ownedOffscreen ? -32000 : 0, WindowStyle = WindowStyle.None,
            ShowActivated = false, ShowInTaskbar = false, Background = Brushes.Blue,
            Title = "Chippi recording fixture",
        };
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(40) };
        int tick = 0;
        timer.Tick += (_, _) => window.Background = (++tick % 2 == 0) ? Brushes.Blue : Brushes.Red;
        window.Loaded += async (_, _) =>
        {
            string path = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(args[0]))!, "fixture-" + Guid.NewGuid().ToString("N") + ".mp4");
            IRecordingSession? session = null;
            try
            {
                var source = new RecordingSource(RecordingTarget.Window, new WindowInteropHelper(window).Handle, "Owned fixture", 320, 180);
                if (args.Length == 2 && args[1] == "--capture-only")
                {
                    await CaptureOnly(source, log, args[0]); exit = 0; return;
                }
                var backend = new WindowsRecordingBackend { Trace = log.WriteLine, SkipBorderAccessForFixture = ownedOffscreen };
                session = await backend.StartAsync(source, new RecordingOptions { SystemAudio = false, MicAudio = false, AllowSoftwareForFixture = sandbox }, path, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(25));
                log.WriteLine("Encoder started on owned fixture");
                timer.Start();
                await Task.Delay(2000);
                session.Pause(); var paused = session.Elapsed;
                await Task.Delay(600);
                if ((session.Elapsed - paused).Duration() > TimeSpan.FromMilliseconds(50)) throw new InvalidOperationException("Pause clock advanced");
                session.Resume(); await Task.Delay(2000);
                var result = await session.StopAsync();
                timer.Stop();
                var clip = await MediaClip.CreateFromFileAsync(await StorageFile.GetFileFromPathAsync(path));
                log.WriteLine($"PASS encoder={result.Encoder}; frames={result.Frames}; dropped={result.DroppedFrames}; encoded={clip.OriginalDuration}; elapsed={result.Duration}; size={result.Width}x{result.Height}; bytes={new FileInfo(path).Length}; file={path}");
                if (clip.OriginalDuration < TimeSpan.FromSeconds(3)) throw new InvalidOperationException("Video too short");
                exit = 0;
            }
            catch (Exception ex) { log.WriteLine(ex); }
            finally
            {
                timer.Stop();
                if (session != null) { try { await session.DisposeAsync(); } catch (Exception ex) { log.WriteLine("Dispose: " + ex.Message); } }
                window.Close(); app.Shutdown();
            }
        };
        window.Show(); app.Run();
        return exit;
    }

    private static async Task CaptureOnly(RecordingSource source, StreamWriter log, string report)
    {
        bool borderless = await CaptureInterop.BorderlessAsync(default);
        var item = CaptureInterop.Item(source);
        log.WriteLine($"Owned WGC fixture size={item.Size.Width}x{item.Size.Height}; borderless access={borderless}");
        using var graphics = new RecordingGraphics(item.Size.Width, item.Size.Height);
        using var pool = Direct3D11CaptureFramePool.CreateFreeThreaded(graphics.Projection, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, item.Size);
        using var capture = pool.CreateCaptureSession(item);
        if (borderless) CaptureInterop.DisableBorder(capture);
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int copying = 0;
        pool.FrameArrived += async (sender, _) =>
        {
            using var frame = sender.TryGetNextFrame();
            if (frame == null || Interlocked.Exchange(ref copying, 1) != 0) return;
            try
            {
                using var bitmap = await SoftwareBitmap.CreateCopyFromSurfaceAsync(frame.Surface);
                byte[] pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4]; bitmap.CopyToBuffer(pixels.AsBuffer());
                int blueOrRed = 0;
                for (int i = 0; i < pixels.Length; i += 4) if ((pixels[i] > 200 || pixels[i + 2] > 200) && pixels[i + 1] < 20) blueOrRed++;
                if (blueOrRed < bitmap.PixelWidth * bitmap.PixelHeight / 2) throw new InvalidDataException("Owned fixture pixels did not arrive");
                string png = Path.ChangeExtension(report, ".png");
                using var stream = new FileStream(png, FileMode.Create);
                var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream.AsRandomAccessStream()); encoder.SetSoftwareBitmap(bitmap); await encoder.FlushAsync();
                log.WriteLine($"PASS Windows Graphics Capture owned-window pixels={blueOrRed}; dimensions={bitmap.PixelWidth}x{bitmap.PixelHeight}; image={png}");
                ready.TrySetResult();
            }
            catch (Exception ex) { ready.TrySetException(ex); }
        };
        capture.StartCapture(); await ready.Task.WaitAsync(TimeSpan.FromSeconds(15));
    }

    private static int Synthetic(StreamWriter log, string report)
    {
        try
        {
            return Task.Run(() =>
            {
                Marshal.ThrowExceptionForHR(MFStartup(0x20070, 0));
                try
                {
                    string file = Path.Combine(Path.GetDirectoryName(report)!, "gpu-fixture-" + Guid.NewGuid().ToString("N") + ".mp4");
                    int written = 0;
                    string encoder;
                    using (var graphics = new RecordingGraphics(640, 360))
                    using (var writer = new RecordingWriter(file, graphics, new RecordingOptions(), audio: true))
                    {
                    var sound = new byte[1600 * 4]; var mix = new float[1600 * 2];
                    var micTimeline = new AudioTimeline(); var systemTimeline = new AudioTimeline();
                    var mic = new RecordingAudioPackets(new WaveFormat(44_100, 16, 1), 480, micTimeline);
                    var system = new RecordingAudioPackets(WaveFormat.CreateIeeeFloatWaveFormat(48_000, 2), 480, systemTimeline);
                    int micPosition = 0, systemPosition = 0, packet = 0;
                    void Feed(RecordingAudioPackets input, bool microphone, ref int position, int target)
                    {
                        int rate = microphone ? 44_100 : 48_000;
                        while (position < target)
                        {
                            int count = Math.Min(new[] { 127, 480, 311 }[packet % 3], target - position);
                            byte[] bytes = new byte[count * (microphone ? 2 : 8)];
                            for (int frame = 0; frame < count; frame++)
                            {
                                double value = .9 * Math.Sin(2 * Math.PI * 440 * (position + frame) / rate);
                                if (microphone) { short sample = (short)(value * 32767); bytes[frame * 2] = (byte)sample; bytes[frame * 2 + 1] = (byte)(sample >> 8); }
                                else { BitConverter.TryWriteBytes(bytes.AsSpan(frame * 8), (float)value); BitConverter.TryWriteBytes(bytes.AsSpan(frame * 8 + 4), (float)value); }
                            }
                            var timestamp = TimeSpan.FromSeconds(position / (double)rate + (packet % 2 == 0 ? 1 : -1) / 48_000d);
                            bool uncertain = packet % 11 == 5;
                            var pin = GCHandle.Alloc(bytes, GCHandleType.Pinned);
                            try { input.Write(pin.AddrOfPinnedObject(), count, uncertain ? AudioClientBufferFlags.TimestampError : 0, uncertain ? 0 : position, uncertain ? null : timestamp, timestamp + TimeSpan.FromSeconds(count / (double)rate)); }
                            finally { pin.Free(); }
                            position += count; packet++;
                        }
                        if (target == rate * 3) input.Flush();
                    }
                    for (int i = 0; i < 90; i++)
                    {
                        var surface = graphics.FixtureFrame(i);
                        if (surface != null) { writer.Video(surface, TimeSpan.FromSeconds(i / 30d)); written++; }
                        Feed(mic, true, ref micPosition, Math.Min(3 * 44_100, (i + 2) * 1470));
                        Feed(system, false, ref systemPosition, Math.Min(3 * 48_000, (i + 2) * 1600));
                        Array.Clear(mix); micTimeline.Mix(i * 1600L, mix); systemTimeline.Mix(i * 1600L, mix);
                        RecordingAudio.ToPcm(mix, sound, 2);
                        writer.Audio(sound, i * 1600L);
                        Thread.Sleep(33);
                    }
                    writer.Finish(); encoder = writer.Encoder;
                    log.WriteLine($"Synthetic inputs: mic {mic.Description}; system {system.Description}");
                    }
                    var clip = MediaClip.CreateFromFileAsync(StorageFile.GetFileFromPathAsync(file).GetAwaiter().GetResult()).GetAwaiter().GetResult();
                    if (clip.OriginalDuration < TimeSpan.FromSeconds(2.9)) throw new InvalidOperationException("Synthetic file too short");
                    log.WriteLine($"PASS synthetic GPU frames={written}; encoder={encoder}; duration={clip.OriginalDuration}; bytes={new FileInfo(file).Length}; file={file}");
                    using (var reader = new MediaFoundationReader(file))
                    {
                        if (reader.WaveFormat.SampleRate != 48_000 || reader.WaveFormat.Channels != 2 || reader.WaveFormat.BitsPerSample != 16) throw new InvalidDataException("Unexpected decoded audio format");
                        using var decoded = new MemoryStream(); byte[] pcm = new byte[48000 * 4]; int count;
                        while ((count = reader.Read(pcm, 0, pcm.Length)) > 0) decoded.Write(pcm, 0, count);
                        byte[] data = decoded.ToArray();
                        double power = 0, sine = 0, cosine = 0, ss = 0, cc = 0, sc = 0, maximumStep = 0, peak = 0;
                        for (int frame = 4800; frame < 124800; frame++)
                        {
                            if (frame * 4 + 1 >= data.Length) throw new InvalidDataException("Decoded audio too short");
                            double value = BitConverter.ToInt16(data, frame * 4) / 32768d, angle = 2 * Math.PI * 440 * frame / 48000;
                            maximumStep = Math.Max(maximumStep, Math.Abs(value - BitConverter.ToInt16(data, (frame - 1) * 4) / 32768d)); peak = Math.Max(peak, Math.Abs(value));
                            double s = Math.Sin(angle), c = Math.Cos(angle); power += value * value; sine += value * s; cosine += value * c; ss += s * s; cc += c * c; sc += s * c;
                        }
                        double tone = ((sine * cc - cosine * sc) * sine + (cosine * ss - sine * sc) * cosine) / (ss * cc - sc * sc);
                        double residual = Math.Sqrt(Math.Max(0, power - tone) / power);
                        log.WriteLine($"Decoded loud mic + system AAC: residual={residual:P3}; maximum sample step={maximumStep:F4}; peak={peak:F4}; samples={data.Length / 4}");
                        if (!double.IsFinite(residual) || residual > .02 || maximumStep > .1 || peak > .99) throw new InvalidDataException("Decoded audio contains packet gaps or clipping distortion");
                        log.WriteLine("PASS continuous resampling and two-source headroom through AAC encode/decode; no playback");
                    }
                    var info = RecordingExports.InspectAsync(file).GetAwaiter().GetResult();
                    if (info.Width != 640 || info.Poster.Length == 0) throw new InvalidDataException("Poster or dimensions missing");
                    string trimmed = file + ".trim.mp4", gif = file + ".gif";
                    RecordingExports.TrimAsync(file, trimmed, TimeSpan.FromSeconds(.5), TimeSpan.FromSeconds(1.5), null, default).GetAwaiter().GetResult();
                    var trim = RecordingExports.InspectAsync(trimmed).GetAwaiter().GetResult();
                    if (Math.Abs(trim.Duration.TotalSeconds - 1) > .1) throw new InvalidDataException("Trim duration incorrect");
                    RecordingExports.GifAsync(file, gif, TimeSpan.Zero, TimeSpan.FromSeconds(.4), null, default).GetAwaiter().GetResult();
                    using var gifStream = File.OpenRead(gif);
                    var decoder = new System.Windows.Media.Imaging.GifBitmapDecoder(gifStream, System.Windows.Media.Imaging.BitmapCreateOptions.None, System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);
                    if (decoder.Frames.Count != 6) throw new InvalidDataException("GIF frame count incorrect");
                    log.WriteLine($"PASS inspect poster={info.Poster.Length}; trim={trim.Duration}; GIF frames={decoder.Frames.Count}; original preserved={File.Exists(file)}");
                    return 0;
                }
                finally { Marshal.ThrowExceptionForHR(MFShutdown()); }
            }).GetAwaiter().GetResult();
        }
        catch (Exception ex) { log.WriteLine(ex); return 1; }
    }
    [DllImport("mfplat.dll")] private static extern int MFStartup(uint version, uint flags);
    [DllImport("mfplat.dll")] private static extern int MFShutdown();
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] private static extern nint GetThreadDesktop(uint thread);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool GetUserObjectInformation(nint handle, int index, StringBuilder value, int length, out int needed);
}
