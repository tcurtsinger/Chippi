// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using Chippi.Features.AppSwitcher.Windows;
using Chippi.Island.Tests;

namespace Chippi.Features.AppSwitcher.Tests;

/// <summary>
/// The engine's share of opening and typing with 50 windows: reading the windows (cold, then warm), ordering them,
/// the session, each search keystroke. Runs when <c>CHIPPI_SWITCHER_TIMINGS</c> names a report file. The windows are the
/// test's own, off screen and never activated; the report has counts and times, no titles.
/// </summary>
public sealed class TimingTests
{
    [Fact]
    public void Fifty_windows()
    {
        if (Environment.GetEnvironmentVariable("CHIPPI_SWITCHER_TIMINGS") is not { Length: > 0 } report)
            return;
        WpfTestThread.Run(() =>
        {
            var windows = new List<Window>();
            try
            {
                for (int i = 0; i < 50; i++)
                {
                    string title = i % 7 == 0 ? new string('x', 300) + $" long {i}" : i % 10 == 0 ? "Untitled" : $"Document {i} - draft";
                    var w = new Window { Title = title, Width = 300, Height = 200, Left = -3000, Top = -3000, ShowActivated = false };
                    w.Show();
                    if (i % 5 == 0)
                        w.WindowState = WindowState.Minimized;
                    windows.Add(w);
                }
                var recent = new RecentWindows();
                foreach (var w in windows.Take(20))
                    recent.Activated(new WindowInteropHelper(w).Handle);

                var text = new StringBuilder();
                var clock = Stopwatch.StartNew();
                var cold = new WindowsSwitcherPc(self: 0).Windows();
                double coldMs = clock.Elapsed.TotalMilliseconds;
                var pc = new WindowsSwitcherPc(self: 0);
                pc.Windows();
                var warm = new List<double>();
                IReadOnlyList<SwitcherWindow> list = cold;
                for (int i = 0; i < 20; i++)
                {
                    clock.Restart();
                    list = pc.Windows();
                    warm.Add(clock.Elapsed.TotalMilliseconds);
                }
                clock.Restart();
                var ordered = recent.Order(list);
                var session = new SwitcherSession(ordered, ordered[0].Handle, ordered[0].Monitor, false);
                double sessionMs = clock.Elapsed.TotalMilliseconds;
                var keys = new List<(string Query, double Ms, int Shown)>();
                foreach (string query in new[] { "d", "do", "doc", "docu", "document 4", "untitled", "long", "" })
                {
                    clock.Restart();
                    session.Type(query);
                    keys.Add((query, clock.Elapsed.TotalMilliseconds, session.Shown.Count));
                }
                warm.Sort();
                var c = CultureInfo.InvariantCulture;
                text.AppendLine(c, $"windows listed: {list.Count} (50 the test's own, {list.Count(w => w.Minimized)} minimized)");
                text.AppendLine(c, $"read windows, cold (new PC, app names from files): {coldMs:F2} ms");
                text.AppendLine(c, $"read windows, warm x20: median {warm[10]:F2} ms, max {warm[^1]:F2} ms");
                text.AppendLine(c, $"order + session: {sessionMs:F3} ms");
                foreach (var (query, ms, shown) in keys)
                    text.AppendLine(c, $"type \"{query}\": {ms:F3} ms, {shown} shown");
                File.WriteAllText(report, text.ToString());
            }
            finally
            {
                foreach (var w in windows)
                    w.Close();
            }
        });
    }
}
