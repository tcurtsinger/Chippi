// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.Core.Diagnostics;

namespace Chippi.Features.TextSnippets;

/// <summary>
/// Turns a match into typed text, one at a time and in order, off the hub's thread: checks the focused field isn't a
/// password, fills the variables, then has the sender delete the trigger and type the text. A snippet edited while this
/// runs doesn't change it: the match holds the version from when the trigger completed.
/// </summary>
internal sealed class SnippetExpander(ISnippetSender sender, Func<Task<string?>>? clipboard, Func<bool> passwordFocused, Func<DateTimeOffset>? now = null, Action? expanded = null, Func<nint>? foreground = null)
{
    private readonly object _gate = new();
    private Task _tail = Task.CompletedTask;
    private int _generation;
    private readonly Func<DateTimeOffset> _now = now ?? (() => DateTimeOffset.Now);

    /// <summary>
    /// Drops every expansion queued or running that hasn't typed yet: the hand typed, clicked or moved on since its trigger,
    /// so deleting the trigger's characters would now delete other text. Called on the hub's thread for each such event.
    /// </summary>
    public void Cancel() => Interlocked.Increment(ref _generation);

    /// <summary>Queues the expansion; returns at once.</summary>
    /// <param name="startedAt">The hub clock when the last trigger key came (Stopwatch ticks), for the time to the finished text; 0 for none.</param>
    public void Enqueue(SnippetMatch match, Delimiter? after, long startedAt = 0)
    {
        int generation = Volatile.Read(ref _generation);
        nint window = foreground?.Invoke() ?? 0;
        lock (_gate)
            _tail = _tail.ContinueWith(_ => ExpandAsync(match, after, startedAt, generation, window), CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default).Unwrap();
    }

    /// <summary>Waits for what is queued, for tests.</summary>
    public Task IdleAsync()
    {
        lock (_gate)
            return _tail;
    }

    private async Task ExpandAsync(SnippetMatch match, Delimiter? after, long startedAt, int generation, nint window)
    {
        // Still the trigger's moment: no input since, and the same window in front.
        bool Valid() => Volatile.Read(ref _generation) == generation && (foreground is null || foreground() == window);
        try
        {
            if (!Valid())
            {
                Log.Info("Text snippets: an expansion was dropped because the typing moved on");
                return; // a key kept for the trigger is lost too: sending it now would land after what was typed since
            }
            if (SafePassword())
            {
                // Nothing expands in a password field. A key kept for the trigger goes back as typed.
                if (after is { } key)
                    await sender.SendAsync(0, "", key);
                return;
            }
            string text = await SnippetRenderer.RenderAsync(match.Snippet.Text, _now(), clipboard);
            if (!await sender.SendAsync(match.Typed, text, after, Valid))
            {
                Log.Info("Text snippets: an expansion was dropped because the typing moved on");
                return;
            }
            expanded?.Invoke();
            if (startedAt != 0) // lengths and times only: what was typed is never logged
                Log.Info($"Text snippets: {text.Length} characters typed {System.Diagnostics.Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds:F1} ms after the last trigger key");
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Log.Warn("Text snippets: typing a snippet failed", ex);
        }
    }

    private bool SafePassword()
    {
        try { return passwordFocused(); }
        catch (Exception ex) when (ex is not OutOfMemoryException) { return false; }
    }
}

/// <summary>Types with <c>SendInput</c>, once Ctrl, Alt and Win are up so the text doesn't turn into shortcuts.</summary>
internal sealed class WindowsSnippetSender : ISnippetSender
{
    private static readonly TimeSpan ModifierWait = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan BatchGap = TimeSpan.FromMilliseconds(8);

    public async Task<bool> SendAsync(int backspaces, string text, Delimiter? after, Func<bool>? valid = null, CancellationToken ct = default)
    {
        var until = DateTime.UtcNow + ModifierWait;
        while ((Native.IsDown(0x11) || Native.IsDown(0x12) || Native.IsDown(0x5B) || Native.IsDown(0x5C)) && DateTime.UtcNow < until)
            await Task.Delay(10, ct);
        // Ctrl, Alt or Win still down would turn Backspace and Enter into other commands: type nothing.
        if (Native.IsDown(0x11) || Native.IsDown(0x12) || Native.IsDown(0x5B) || Native.IsDown(0x5C))
            throw new InvalidOperationException(TextSnippetsModule.NotInserted);
        if (valid is not null && !valid())
            return false;
        var batches = Keystrokes.Plan(backspaces, text, after);
        for (int i = 0; i < batches.Count; i++)
        {
            if (valid is not null && !valid())
                return false;
            if (Native.Send(batches[i]) != batches[i].Length)
                throw new InvalidOperationException("Input blocked");
            if (i + 1 < batches.Count)
                await Task.Delay(BatchGap, ct);
        }
        return true;
    }
}
