// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace BoostSpike;

internal static class Experiment
{
    private const int Rate = 48000;

    public static async Task<int> Run(string outDir)
    {
        Directory.CreateDirectory(outDir);
        var log = new StringBuilder();
        var self = Environment.ProcessPath!;
        Process? a = null, b = null, replay = null;
        try
        {
            using var devices = new MMDeviceEnumerator();
            var endpoint = devices.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            // Nothing may reach the host's speakers: mute this machine's output before any tone starts.
            endpoint.AudioEndpointVolume.Mute = true;
            endpoint.AudioEndpointVolume.MasterVolumeLevelScalar = 0;
            Program.Line(log, $"Endpoint '{endpoint.FriendlyName}' muted={endpoint.AudioEndpointVolume.Mute} volume={endpoint.AudioEndpointVolume.MasterVolumeLevelScalar:F2}");
            Program.Line(log, $"Windows {Environment.OSVersion.Version}, {System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture}");

            a = Process.Start(self, "tone 440 1 600");
            b = Process.Start(self, "tone 1000 0 600");
            await Task.Delay(2000);
            Program.Line(log, $"App A pid {a.Id} (440 Hz + 3 kHz marker each second), app B pid {b.Id} (1000 Hz)");

            var isolation = await Measure(a.Id, 4);
            Program.Line(log, "1. Capture of A's tree: " + isolation);
            var other = await Measure(b.Id, 4);
            Program.Line(log, "   Capture of B's tree: " + other);

            var session = SessionOf(endpoint, a.Id);
            Program.Line(log, session is null ? "A has no session on the default output" : $"A's session found, volume {session.SimpleAudioVolume.Volume:F2}");
            Levels? muted = null, zero = null;
            if (session is not null)
            {
                session.SimpleAudioVolume.Mute = true;
                await Task.Delay(300);
                muted = await Measure(a.Id, 4);
                Program.Line(log, $"2a. A's session muted (meter {session.AudioMeterInformation.MasterPeakValue:F3}): " + muted);
                session.SimpleAudioVolume.Mute = false;
                session.SimpleAudioVolume.Volume = 0;
                await Task.Delay(300);
                zero = await Measure(a.Id, 4);
                Program.Line(log, $"2b. A's session volume 0 (meter {session.AudioMeterInformation.MasterPeakValue:F3}): " + zero);
                session.SimpleAudioVolume.Volume = 1;
            }

            // Quiet A's own path the way that kept the capture, if either did.
            var keeps = (Levels? l) => l is not null && l.Tone440 > isolation.Tone440 * 0.9;
            var method = keeps(muted) ? "mute" : keeps(zero) ? "volume 0" : "none (neither kept the capture)";
            if (session is not null && method == "mute") session.SimpleAudioVolume.Mute = true;
            if (session is not null && method == "volume 0") session.SimpleAudioVolume.Volume = 0;
            Program.Line(log, "3. Original path quieted by: " + method);

            const int seconds = 60;
            var stats = Path.Combine(outDir, "replay-stats.txt");
            var source = new Analyzer();
            var copy = new Analyzer();
            using (var captureA = await ProcessCapture.Create(a.Id, true))
            {
                captureA.Data += source.Add;
                captureA.Start();
                replay = Process.Start(self, $"replay {a.Id} 2 {seconds + 4} {stats}");
                using var captureReplay = await ProcessCapture.Create(replay.Id, true);
                captureReplay.Data += copy.Add;
                captureReplay.Start();
                await Task.Delay(TimeSpan.FromSeconds(seconds));
                Program.Line(log, $"4. Replay at gain 2 for {seconds} s. A: {source.Summary()}, discontinuities {captureA.Discontinuities}");
                Program.Line(log, $"   Replay: {copy.Summary()}, discontinuities {captureReplay.Discontinuities}");
            }
            await replay.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
            Program.Line(log, "   Replay process: " + (File.Exists(stats) ? File.ReadAllText(stats).Trim() : "no stats"));
            Program.Line(log, "   Level ratio replay/A at 440 Hz: " + (source.Levels.Tone440 > 0 ? (copy.Levels.Tone440 / source.Levels.Tone440).ToString("F2") : "n/a"));
            Program.Line(log, "   A's 440 Hz during replay vs alone: " + (isolation.Tone440 > 0 ? (source.Levels.Tone440 / isolation.Tone440).ToString("F2") : "n/a"));
            Program.Line(log, "   " + Latency(source.Onsets, copy.Onsets));
            Program.Line(log, $"Endpoint still muted={endpoint.AudioEndpointVolume.Mute} volume={endpoint.AudioEndpointVolume.MasterVolumeLevelScalar:F2}");
            return 0;
        }
        catch (Exception ex)
        {
            Program.Line(log, "FAILED " + ex);
            return 1;
        }
        finally
        {
            foreach (var p in new[] { replay, a, b })
                try { if (p is { HasExited: false }) p.Kill(); } catch (InvalidOperationException) { }
            File.WriteAllText(Path.Combine(outDir, "results.txt"), log.ToString());
        }
    }

    /// <summary>The replaying "Chippi" process: capture the app, apply gain, play it back.</summary>
    public static async Task<int> Replay(int pid, float gain, int seconds, string statsFile)
    {
        var queue = new ConcurrentQueue<float[]>();
        using var capture = await ProcessCapture.Create(pid, true);
        capture.Data += (samples, frames, _, _) => queue.Enqueue(samples);
        var provider = new ReplayProvider(queue, gain);
        using var output = new WasapiOut(AudioClientShareMode.Shared, 20);
        output.Init(provider);
        capture.Start();
        output.Play();
        await Task.Delay(TimeSpan.FromSeconds(seconds));
        output.Stop();
        File.WriteAllText(statsFile, $"reads {provider.Reads}, short reads {provider.ShortReads}, missing frames {provider.MissingFrames}, clipped samples {provider.Clipped}, capture discontinuities {capture.Discontinuities}");
        return 0;
    }

    private static AudioSessionControl? SessionOf(MMDevice endpoint, int pid)
    {
        var sessions = endpoint.AudioSessionManager.Sessions;
        for (var i = 0; i < sessions.Count; i++)
            if (sessions[i].GetProcessID == (uint)pid)
                return sessions[i];
        return null;
    }

    private static async Task<Levels> Measure(int pid, int seconds)
    {
        var analyzer = new Analyzer();
        using var capture = await ProcessCapture.Create(pid, true);
        capture.Data += analyzer.Add;
        capture.Start();
        await Task.Delay(TimeSpan.FromSeconds(seconds));
        return analyzer.Levels with { Discontinuities = capture.Discontinuities, Frames = capture.Frames };
    }

    private static string Latency(List<double> source, List<double> copy)
    {
        var delays = new List<double>();
        foreach (var t in source)
            if (copy.FirstOrDefault(c => c > t && c - t < 0.5) is var match and > 0)
                delays.Add((match - t) * 1000);
        if (delays.Count == 0)
            return $"Latency: no marker pairs (A markers {source.Count}, replay markers {copy.Count})";
        delays.Sort();
        var mean = delays.Average();
        var sd = Math.Sqrt(delays.Average(d => (d - mean) * (d - mean)));
        return $"Latency A to replay (loopback tap to loopback tap), {delays.Count} of {source.Count} markers paired: min {delays[0]:F1} ms, median {delays[delays.Count / 2]:F1}, p95 {delays[(int)(delays.Count * 0.95)]:F1}, max {delays[^1]:F1}, sd {sd:F1}";
    }
}

internal sealed record Levels(double Tone440, double Tone1000, double Marker3k, double Rms)
{
    public int Discontinuities { get; init; }
    public long Frames { get; init; }

    public override string ToString() =>
        $"440 Hz {Tone440:F4}, 1000 Hz {Tone1000:F4}, 3 kHz {Marker3k:F4}, RMS {Rms:F4}, frames {Frames}, discontinuities {Discontinuities}";
}

/// <summary>Tone levels over 100 ms Goertzel blocks, and marker onsets timed from packet QPC.</summary>
internal sealed class Analyzer
{
    private const int Rate = 48000;
    private const int Block = 4800;
    private const int Small = 48;
    private readonly List<float> _block = new(Block);
    private readonly List<float> _small = new(Small);
    private double _sum440, _sum1000, _sum3k, _sumSquares;
    private long _blocks, _samples;
    private double _lastOnset = double.NegativeInfinity;
    private double _smallStart;

    public List<double> Onsets { get; } = [];

    public Levels Levels => _blocks == 0 ? new(0, 0, 0, 0) : new(_sum440 / _blocks, _sum1000 / _blocks, _sum3k / _blocks, Math.Sqrt(_sumSquares / Math.Max(1, _samples)));

    public string Summary() => $"{Levels}, markers {Onsets.Count}";

    public void Add(float[] samples, int frames, long ticks, bool discontinuity)
    {
        var start = (double)ticks / Stopwatch.Frequency;
        for (var i = 0; i < frames; i++)
        {
            var mono = (samples[2 * i] + samples[2 * i + 1]) / 2;
            _sumSquares += mono * mono;
            _samples++;
            if (_small.Count == 0)
                _smallStart = start + (double)i / Rate;
            _small.Add(mono);
            if (_small.Count == Small)
            {
                if (Amplitude(_small, 3000) > 0.05 && _smallStart - _lastOnset > 0.5)
                    Onsets.Add(_lastOnset = _smallStart);
                _small.Clear();
            }
            _block.Add(mono);
            if (_block.Count == Block)
            {
                _sum440 += Amplitude(_block, 440);
                _sum1000 += Amplitude(_block, 1000);
                _sum3k += Amplitude(_block, 3000);
                _blocks++;
                _block.Clear();
            }
        }
    }

    private static double Amplitude(List<float> x, double hz)
    {
        var w = 2 * Math.PI * hz / Rate;
        var c = 2 * Math.Cos(w);
        double s1 = 0, s2 = 0;
        foreach (var v in x)
        {
            var s = v + c * s1 - s2;
            s2 = s1;
            s1 = s;
        }
        var power = s1 * s1 + s2 * s2 - c * s1 * s2;
        return 2 * Math.Sqrt(Math.Max(0, power)) / x.Count;
    }
}

/// <summary>Plays queued capture packets with gain, counting reads it could not fill.</summary>
internal sealed class ReplayProvider(ConcurrentQueue<float[]> queue, float gain) : ISampleProvider
{
    private float[]? _current;
    private int _offset;
    private bool _started;

    public WaveFormat WaveFormat { get; } = ProcessCapture.Format;
    public long Reads { get; private set; }
    public long ShortReads { get; private set; }
    public long MissingFrames { get; private set; }
    public long Clipped { get; private set; }

    public int Read(float[] buffer, int offset, int count)
    {
        Reads++;
        var written = 0;
        while (written < count)
        {
            if (_current is null || _offset == _current.Length)
            {
                if (!queue.TryDequeue(out _current)) { _current = null; break; }
                _offset = 0;
                _started = true;
            }
            var take = Math.Min(count - written, _current.Length - _offset);
            for (var i = 0; i < take; i++)
            {
                var v = _current[_offset + i] * gain;
                if (v is > 1 or < -1) { Clipped++; v = Math.Clamp(v, -1, 1); }
                buffer[offset + written + i] = v;
            }
            written += take;
            _offset += take;
        }
        if (written < count)
        {
            Array.Clear(buffer, offset + written, count - written);
            if (_started) { ShortReads++; MissingFrames += (count - written) / 2; }
        }
        return count;
    }
}
