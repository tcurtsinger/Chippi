// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Diagnostics;
using System.Globalization;
using System.IO;

namespace Chippi.Features.ScreenCapture.Scrolling;

/// <summary>
/// The scrolling capture's timing spans, appended to the capture timings report (<c>CHIPPI_CAPTURE_TIMINGS</c>, the file the
/// capture layer's own lines go to). Lines read "scroll-start …", "scroll-done …", "scroll-shown …" and "auto-step …", each
/// with <c>name=milliseconds</c> pairs. Nothing is written unless the variable names a file.
/// </summary>
internal static class ScrollTimings
{
    private static readonly string? Path = Environment.GetEnvironmentVariable("CHIPPI_CAPTURE_TIMINGS");
    private static readonly object Gate = new();

    /// <summary>The writer, or null when no report is wanted (so the spans cost nothing).</summary>
    public static Action<string>? Sink => string.IsNullOrEmpty(Path) ? null : Report;

    public static void Report(string line)
    {
        if (string.IsNullOrEmpty(Path)) return;
        try
        {
            lock (Gate) File.AppendAllText(Path, line + Environment.NewLine);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    /// <summary>Milliseconds since a <see cref="Stopwatch.GetTimestamp"/> reading, as the report prints them.</summary>
    public static string Ms(long from) => Stopwatch.GetElapsedTime(from).TotalMilliseconds.ToString("F1", CultureInfo.InvariantCulture);
}
