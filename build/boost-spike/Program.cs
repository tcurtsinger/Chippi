// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Diagnostics;
using System.Globalization;
using System.Text;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace BoostSpike;

/// <summary>
/// Volume boost route 1 (boost-gate findings): capture one app with process loopback, quiet its own
/// path, replay it with gain. Modes:
///   tone &lt;hz&gt; &lt;pulse 0|1&gt; &lt;seconds&gt;   a synthetic app (sine, optional 3 kHz marker every second)
///   experiment &lt;outDir&gt;               the whole run; mutes this machine's default output first
///   hostmeter &lt;seconds&gt; &lt;outFile&gt;      read-only: peak of every render session on the host
/// </summary>
internal static class Program
{
    private const int Rate = 48000;

    private static async Task<int> Main(string[] args)
    {
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        switch (args.FirstOrDefault())
        {
            case "tone":
                Tone(double.Parse(args[1], CultureInfo.InvariantCulture), args[2] == "1", int.Parse(args[3], CultureInfo.InvariantCulture));
                return 0;
            case "experiment":
                return await Experiment.Run(args[1]);
            case "replay":
                return await Experiment.Replay(int.Parse(args[1], CultureInfo.InvariantCulture), float.Parse(args[2], CultureInfo.InvariantCulture),
                    int.Parse(args[3], CultureInfo.InvariantCulture), args[4]);
            case "hostmeter":
                HostMeter(int.Parse(args[1], CultureInfo.InvariantCulture), args[2]);
                return 0;
            default:
                Console.Error.WriteLine("tone | experiment | hostmeter");
                return 2;
        }
    }

    private static void Tone(double hz, bool pulse, int seconds)
    {
        using var output = new WasapiOut(AudioClientShareMode.Shared, 40);
        output.Init(new ToneProvider(hz, pulse));
        output.Play();
        Thread.Sleep(TimeSpan.FromSeconds(seconds));
        output.Stop();
    }

    private static void HostMeter(int seconds, string file)
    {
        using var devices = new MMDeviceEnumerator();
        var peaks = new Dictionary<string, float>();
        var end = DateTime.UtcNow.AddSeconds(seconds);
        while (DateTime.UtcNow < end)
        {
            foreach (var device in devices.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
            {
                var sessions = device.AudioSessionManager.Sessions;
                for (var i = 0; i < sessions.Count; i++)
                {
                    var session = sessions[i];
                    string name;
                    try { name = Process.GetProcessById((int)session.GetProcessID).ProcessName; }
                    catch (ArgumentException) { name = "pid " + session.GetProcessID; }
                    var key = device.FriendlyName + " | " + name;
                    peaks[key] = Math.Max(peaks.GetValueOrDefault(key), session.AudioMeterInformation.MasterPeakValue);
                }
            }
            Thread.Sleep(50);
        }
        File.WriteAllLines(file, peaks.OrderBy(p => p.Key).Select(p => $"{p.Key}\tmax peak {p.Value:F4}"));
    }

    /// <summary>Sine at 0.15 with a 20 ms 3 kHz marker at 0.25 at the start of every second.</summary>
    internal sealed class ToneProvider(double hz, bool pulse) : ISampleProvider
    {
        private long _n;

        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(Rate, 2);

        public int Read(float[] buffer, int offset, int count)
        {
            for (var i = 0; i < count; i += 2, _n++)
            {
                var t = (double)_n / Rate;
                var value = 0.15 * Math.Sin(2 * Math.PI * hz * t);
                if (pulse && _n % Rate < Rate / 50)
                    value += 0.25 * Math.Sin(2 * Math.PI * 3000 * t);
                buffer[offset + i] = buffer[offset + i + 1] = (float)value;
            }
            return count;
        }
    }

    internal static string Line(StringBuilder log, string text)
    {
        var line = $"{DateTime.UtcNow:HH:mm:ss.fff} {text}";
        Console.WriteLine(line);
        log.AppendLine(line);
        return line;
    }
}
