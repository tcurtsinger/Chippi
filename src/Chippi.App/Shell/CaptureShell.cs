// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows.Threading;
using Chippi.Core.Island;
using Chippi.UI.Capture;

namespace Chippi.App.Shell;

/// <summary>
/// The shell's side of the capture layer (docs/phase-7/capture-layer.md): the window to give the keyboard back to after a
/// starter on the island, the island's close, and the layer's line on the bar.
/// </summary>
internal sealed class CaptureShell(ShellContext shell) : ICaptureShell
{
    public const string ModuleId = "capture";
    private const string Key = "capture/bar";

    private readonly IIslandPublisher _bar = shell.Island.CreatePublisher(ModuleId);
    private DispatcherTimer? _clear;

    /// <summary>Closes the island and completes once it's off the screen; set by the island host.</summary>
    public Func<Task>? CloseIsland { get; set; }

    public nint IslandReturnWindow() => shell.Services.IslandPasteTarget?.Invoke()?.Window ?? 0;

    public Task CloseIslandAsync() => CloseIsland?.Invoke() ?? Task.CompletedTask;

    public void Bar(string? line, Action? click = null)
    {
        _clear?.Stop();
        if (line is null)
        {
            _bar.Clear(Key);
            return;
        }
        // A line with a click (the countdown) stays until the layer clears it; a failure goes after 6 seconds.
        _bar.Publish(new IslandActivity(Key, IslandActivityKind.Status, line, "")
        {
            IsFeedback = true,
            Icon = "Camera",
            NeedsAttention = click is null,
            Clicked = click,
        });
        if (click is not null)
            return;
        _clear = new DispatcherTimer { Interval = shell.Services.IslandNoticeDuration };
        _clear.Tick += (_, _) =>
        {
            _clear?.Stop();
            _bar.Clear(Key);
        };
        _clear.Start();
    }
}
