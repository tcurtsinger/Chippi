// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

#if DEBUG
using System.IO;
using System.Windows.Media;
using System.Windows.Threading;
using Chippi.App.Island;
using Chippi.App.Island.Media;
using Chippi.Core.Island;
using static Chippi.Island.Tests.IslandWindowTests;

namespace Chippi.Island.Tests;

/// <summary>
/// Times the shell's motion on the primary display with the home the mock shows: the open, a page, back and the
/// close (the first of each in the process, then warm), and the bar's unfold and fold. While it runs, a Rendering
/// handler keeps WPF drawing every frame and records each one; a gap of more than a frame inside an animation counts
/// the frames it skipped, from the moment it was asked for. Writes a report to the file CHIPPI_MOTION_TIMINGS names,
/// and runs only then.
/// </summary>
public sealed class IslandMotionTimings
{
    private const int Warm = 8;

    [Fact]
    public void Measure()
    {
        if (Environment.GetEnvironmentVariable("CHIPPI_MOTION_TIMINGS") is not { Length: > 0 } report)
            return;
        var lines = new List<string>();
        WpfTestThread.Run(() => WithShownIsland(window =>
        {
            var frames = new List<TimeSpan>();
            EventHandler record = (_, e) =>
            {
                var at = ((RenderingEventArgs)e).RenderingTime;
                if (frames.Count == 0 || frames[^1] != at)
                    frames.Add(at);
            };
            CompositionTarget.Rendering += record;
            try
            {
                var song = new MediaTrack("spotify", "Spotify", "Weekend Mix", "Various Artists", true, true, true, true, true, true,
                    TimeSpan.FromSeconds(72), TimeSpan.FromSeconds(232), DateTimeOffset.Now, null);
                var media = new IslandPanelTests.FakeMedia();
                window.SetMedia(media);
                window.SetTiles(IslandPanelShots.HomeTiles());
                window.Prewarm(); // as the app does once it's idle after start
                Wait(600);

                var runs = new List<(string Name, TimeSpan From, TimeSpan Length)>();
                var calls = new Dictionary<string, List<double>>();
                void Time(string name, int ms, Action act)
                {
                    runs.Add((name, frames[^1], TimeSpan.FromMilliseconds(ms)));
                    long start = System.Diagnostics.Stopwatch.GetTimestamp();
                    act();
                    (calls.TryGetValue(name, out var list) ? list : calls[name] = []).Add(System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds);
                    Wait(ms + 350);
                }
                // Each round opens on the home (no page remembered), goes into a page, across to Now Playing, back, and closes.
                void Cycle(string suffix)
                {
                    window.SetLastPage(null);
                    Time("open" + suffix, 220, () => window.ForceOpen(true));
                    Time("home to page" + suffix, 150, () => window.Panel.ShowPage("keep-awake", remember: false));
                    Time("page to Now Playing" + suffix, 150, () => window.Panel.ShowPage(IslandPanelWindow.NowPlayingKey, remember: false));
                    Time("back to home" + suffix, 150, () => window.Panel.ShowPage(null, remember: false));
                    Time("close" + suffix, 160, () => window.ForceOpen(false));
                }
                Cycle(" (first)");
                for (int i = 0; i < Warm; i++)
                    Cycle("");
                for (int i = 0; i < Warm; i++)
                {
                    Time("unfold", 240, () => { media.Sessions = [song]; media.Raise(); window.SetActivities([new IslandActivity("track", IslandActivityKind.Media, "Weekend Mix", "Spotify")], Chippi.UI.Pet.PetMood.Idle); });
                    Time("fold", 180, () => { media.Sessions = []; media.Raise(); window.SetActivities([], Chippi.UI.Pet.PetMood.Sleepy); });
                }
                // Where the time goes: the same open and close without the panel's shadow effect.
                var shadow = window.Panel.ShapeShadow.Effect;
                window.Panel.ShapeShadow.Effect = null;
                for (int i = 0; i < Warm; i++)
                {
                    Time("open, no shadow effect", 220, () => window.ForceOpen(true));
                    Time("close, no shadow effect", 160, () => window.ForceOpen(false));
                }
                window.Panel.ShapeShadow.Effect = shadow;

                var deltas = frames.Zip(frames.Skip(1), (a, b) => b - a).OrderBy(d => d).ToList();
                var frame = deltas[deltas.Count / 2];
                lines.Add($"frame {frame.TotalMilliseconds:0.00} ms ({1000 / frame.TotalMilliseconds:0} Hz), {frames.Count} frames recorded");
                lines.Add("motion\truns\tskipped (mean)\tskipped (max)\tlongest gap ms\tcall ms (mean)");
                foreach (var group in runs.GroupBy(r => r.Name))
                {
                    var each = group.Select(r => Skipped(frames, r.From, r.Length, frame)).ToList();
                    lines.Add($"{group.Key}\t{each.Count}\t{each.Average(s => s.Skipped):0.0}\t{each.Max(s => s.Skipped)}\t{each.Max(s => s.Longest.TotalMilliseconds):0.0}\t{calls[group.Key].Average():0.0}");
                }
                // Where in each motion the frames go: the gaps (ms) of its third and fourth runs, from the moment it was asked for.
                if (Environment.GetEnvironmentVariable("CHIPPI_MOTION_DETAIL") == "1")
                    foreach (var run in runs.GroupBy(r => r.Name).SelectMany(g => g.Skip(2).Take(2)))
                    {
                        int i = frames.FindIndex(t => t > run.From);
                        var gaps = frames.Skip(i).TakeWhile(t => t <= frames[i] + run.Length + frame).Prepend(run.From).ToList();
                        lines.Add($"{run.Name}: " + string.Join(" ", gaps.Zip(gaps.Skip(1), (a, b) => (b - a).TotalMilliseconds.ToString("0"))));
                    }
            }
            finally
            {
                CompositionTarget.Rendering -= record;
            }
        }));
        File.WriteAllLines(report, lines);
    }

    /// <summary>The frames skipped from <paramref name="from"/> (the last frame before the motion was asked for) to its end.</summary>
    private static (int Skipped, TimeSpan Longest) Skipped(List<TimeSpan> frames, TimeSpan from, TimeSpan length, TimeSpan frame)
    {
        int i = frames.FindIndex(t => t > from);
        if (i < 0)
            return (0, TimeSpan.Zero);
        var end = frames[i] + length + frame;
        int skipped = 0;
        var longest = TimeSpan.Zero;
        for (var previous = from; i < frames.Count && frames[i] <= end; previous = frames[i++])
        {
            var gap = frames[i] - previous;
            longest = gap > longest ? gap : longest;
            skipped += Math.Max(0, (int)Math.Round(gap / frame) - 1);
        }
        return (skipped, longest);
    }

    /// <summary>Lets the dispatcher run freely for <paramref name="ms"/> (no sleeping, which would skip frames itself).</summary>
    private static void Wait(int ms)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(ms) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            frame.Continue = false;
        };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }
}
#endif
