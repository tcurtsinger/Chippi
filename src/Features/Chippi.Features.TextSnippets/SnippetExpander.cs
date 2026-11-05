// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.Core.Diagnostics;

namespace Chippi.Features.TextSnippets;

/// <summary>
/// Turns a match into typed text, one at a time and in order, off the hub's thread: checks the focused field isn't a
/// password, fills the variables, then has the sender delete the trigger and type the text. A snippet edited while this
/// runs doesn't change it: the match holds the version from when the trigger completed.
/// </summary>
internal sealed class SnippetExpander(ISnippetSender sender, Func<Task<string?>>? clipboard, Func<bool> passwordFocused, Func<DateTimeOffset>? now = null, Action? expanded = null)
{
    private readonly object _gate = new();
    private Task _tail = Task.CompletedTask;
    private readonly Func<DateTimeOffset> _now = now ?? (() => DateTimeOffset.Now);

    /// <summary>Queues the expansion; returns at once.</summary>
    /// <param name="startedAt">The hub clock when the last trigger key came (Stopwatch ticks), for the time to the finished text; 0 for none.</param>
    public void Enqueue(SnippetMatch match, Delimiter? after, long startedAt = 0)
    {
        lock (_gate)
            _tail = _tail.ContinueWith(_ => ExpandAsync(match, after, startedAt), CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default).Unwrap();
    }

    /// <summary>Waits for what is queued, for tests.</summary>
    public Task IdleAsync()
    {
        lock (_gate)
            return _tail;
    }

    private async Task ExpandAsync(SnippetMatch match, Delimiter? after, long startedAt)
    {
        try
        {
            if (SafePassword())
            {
                // Nothing expands in a password field. A key kept for the trigger goes back as typed.
                if (after is { } key)
                    await sender.SendAsync(0, "", key);
                return;
            }
            string text = await SnippetRenderer.RenderAsync(match.Snippet.Text, _now(), clipboard);
            await sender.SendAsync(match.Typed, text, after);
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

    public async Task SendAsync(int backspaces, string text, Delimiter? after, CancellationToken ct = default)
    {
        var until = DateTime.UtcNow + ModifierWait;
        while ((Native.IsDown(0x11) || Native.IsDown(0x12) || Native.IsDown(0x5B) || Native.IsDown(0x5C)) && DateTime.UtcNow < until)
            await Task.Delay(10, ct);
        var batches = Keystrokes.Plan(backspaces, text, after);
        for (int i = 0; i < batches.Count; i++)
        {
            if (Native.Send(batches[i]) != batches[i].Length)
                throw new InvalidOperationException("Input blocked");
            if (i + 1 < batches.Count)
                await Task.Delay(BatchGap, ct);
        }
    }
}
