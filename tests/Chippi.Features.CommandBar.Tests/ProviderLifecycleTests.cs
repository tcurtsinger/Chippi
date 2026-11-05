// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Reflection;
using System.Windows.Media;
using Chippi.Features.CommandBar.Search;
using Chippi.Features.CommandBar.Windows;
using Xunit.Abstractions;

namespace Chippi.Features.CommandBar.Tests;

public sealed class ProviderLifecycleTests(ITestOutputHelper output)
{
    [Fact]
    public async Task Repeated_pending_icon_requests_share_one_queued_read()
    {
        using var started = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        int reads = 0;
        var image = new DrawingImage();
        image.Freeze();
        using var menu = new StartMenu(() =>
        {
            started.Set();
            if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("Fixture was not released");
            return [];
        }, (_, _) => { Interlocked.Increment(ref reads); return image; }, (_, _) => null);
        var listing = menu.ReadAsync();
        Assert.True(started.Wait(TimeSpan.FromSeconds(10)));
        Task<ImageSource?>[] requests;
        int queued;
        try
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            requests = Enumerable.Range(0, 1000).Select(_ => menu.IconAsync(new StartApp("Chrome", "Chrome"), 20)).ToArray();
            long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
            object queue = typeof(StartMenu).GetField("_work", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(menu)!;
            queued = (int)queue.GetType().GetProperty("Count")!.GetValue(queue)!;
            output.WriteLine($"1,000 pending requests: queued={queued}; distinct tasks={requests.Distinct().Count()}; caller allocation={bytes:N0} bytes");
        }
        finally { release.Set(); }
        await listing;
        await Task.WhenAll(requests);
        Assert.Equal(1, reads);
        Assert.Equal(1, queued);
        _ = Assert.Single(requests.Distinct());
    }

    [Fact]
    public async Task Window_title_changes_share_the_app_icon_but_new_scale_reads_again()
    {
        int reads = 0;
        var image = FrozenImage();
        using var menu = new StartMenu(() => [], (_, _) => null, (_, _) => { reads++; return image; });
        var window = new OpenWindow(42, "First document", "Editor") { IconPath = @"C:\Editor.exe" };
        var first = menu.WindowIconAsync(window, 20);
        Assert.Same(image, await first);
        Assert.Same(first, menu.WindowIconAsync(window with { Title = "Second document" }, 20));
        Assert.Same(image, await menu.WindowIconAsync(window, 30));
        Assert.Equal(2, reads);
    }

    [Fact]
    public async Task App_icons_keep_packaged_shell_identity_and_display_scale()
    {
        const string appId = "Publisher.App_family!App";
        var image = FrozenImage();
        var reads = new List<(string, int)>();
        using var menu = new StartMenu(() => [], (parsing, pixels) => { reads.Add((parsing, pixels)); return image; }, (_, _) => null);

        Assert.Same(image, await menu.IconAsync(new StartApp("Packaged app", appId), 30));

        Assert.Equal((@"shell:AppsFolder\" + appId, 30), Assert.Single(reads));
    }

    [Fact]
    public async Task Window_icons_prefer_the_executable_source_over_small_window_artwork()
    {
        var executable = FrozenImage();
        var windowIcon = FrozenImage();
        int windowReads = 0;
        var reads = new List<(string, int)>();
        using var menu = new StartMenu(() => [], (path, pixels) => { reads.Add((path, pixels)); return executable; }, (_, _) => { windowReads++; return windowIcon; });
        var window = new OpenWindow(42, "Document", "Editor") { IconPath = @"C:\Apps\Editor.exe" };

        Assert.Same(executable, await menu.WindowIconAsync(window, 40));

        Assert.Equal((window.IconPath, 40), Assert.Single(reads));
        Assert.Equal(0, windowReads);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData(@"C:\Windows\System32\ApplicationFrameHost.exe", false)]
    [InlineData(@"C:\Windows\System32\APPLICATIONFRAMEHOST.EXE", false)]
    [InlineData(@"C:\Apps\Unavailable.exe", true)]
    public async Task Window_icons_fall_back_without_using_the_packaged_app_host(string? path, bool readsExecutable)
    {
        var image = FrozenImage();
        var reads = new List<string>();
        using var menu = new StartMenu(() => [], (source, _) => { reads.Add(source); return null; }, (handle, pixels) =>
        {
            Assert.Equal((nint)42, handle);
            Assert.Equal(30, pixels);
            reads.Add("window");
            return image;
        });

        Assert.Same(image, await menu.WindowIconAsync(new OpenWindow(42, "Document", "Editor") { IconPath = path }, 30));

        Assert.Equal(readsExecutable ? new[] { path!, "window" } : ["window"], reads);
    }

    [Fact]
    public async Task Missing_icons_retry_after_apps_refresh()
    {
        int reads = 0;
        var image = FrozenImage();
        using var menu = new StartMenu(() => [], (_, _) => ++reads == 1 ? null : image, (_, _) => null);
        var app = new StartApp("Editor", "Editor");
        Assert.Null(await menu.IconAsync(app, 20));
        Assert.Null(await menu.IconAsync(app, 20));
        Assert.Equal(1, reads);
        await menu.ReadAsync();
        Assert.Same(image, await menu.IconAsync(app, 20));
        Assert.Equal(2, reads);
    }

    [Fact]
    public async Task Failed_icon_reads_can_retry_after_apps_refresh()
    {
        int reads = 0;
        var image = FrozenImage();
        using var menu = new StartMenu(() => [], (_, _) => ++reads == 1 ? throw new InvalidOperationException("Fixture failure") : image, (_, _) => null);
        var app = new StartApp("Editor", "Editor");
        await Assert.ThrowsAsync<InvalidOperationException>(() => menu.IconAsync(app, 20));
        await menu.ReadAsync();
        Assert.Same(image, await menu.IconAsync(app, 20));
        Assert.Equal(2, reads);
    }

    [Fact]
    public async Task Stop_settles_queued_icon_work_without_reading_the_shell()
    {
        using var started = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        int reads = 0;
        using var menu = new StartMenu(() =>
        {
            started.Set();
            if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("Fixture was not released");
            return [];
        }, (_, _) => { reads++; return null; }, (_, _) => null);
        var listing = menu.ReadAsync();
        Assert.True(started.Wait(TimeSpan.FromSeconds(10)));
        var icon = menu.IconAsync(new StartApp("Editor", "Editor"), 20);
        try
        {
            menu.Dispose();
            await Assert.ThrowsAsync<ObjectDisposedException>(() => icon.WaitAsync(TimeSpan.FromSeconds(2)));
            Assert.Equal(0, reads);
        }
        finally { release.Set(); }
        await Assert.ThrowsAsync<ObjectDisposedException>(() => listing);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => menu.IconAsync(new StartApp("Editor", "Editor"), 20));
    }

    [Fact]
    public async Task Canceled_native_file_work_keeps_its_gate_until_it_really_finishes()
    {
        using var started = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var firstCancel = new CancellationTokenSource();
        using var nextCancel = new CancellationTokenSource();
        int calls = 0;
        using var pc = new WindowsPc((_, _, _) =>
        {
            if (Interlocked.Increment(ref calls) == 1)
            {
                started.Set();
                if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("Fixture was not released");
            }
            return [];
        });
        var first = pc.FindFilesAsync("first", 8, firstCancel.Token);
        try
        {
            Assert.True(started.Wait(TimeSpan.FromSeconds(10)));
            firstCancel.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
            var next = pc.FindFilesAsync("next", 8, nextCancel.Token);
            nextCancel.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => next);
            Assert.Equal(1, calls);
        }
        finally { release.Set(); }
        Assert.Empty(await pc.FindFilesAsync("last", 8, default));
        Assert.Equal(2, calls);
    }

    [Fact]
    public void Stopped_app_refresh_does_not_publish_a_late_list()
    {
        using var started = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var menu = new StartMenu(() =>
        {
            started.Set();
            if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("Fixture was not released");
            return [new StartApp("Late", "Late")];
        }, (_, _) => null, (_, _) => null);
        using var pc = new WindowsPc((_, _, _) => [], menu);
        int published = 0;
        pc.AppsChanged += () => Interlocked.Increment(ref published);
        pc.RefreshApps();
        try
        {
            Assert.True(started.Wait(TimeSpan.FromSeconds(10)));
            pc.Dispose();
        }
        finally { release.Set(); }
        var reading = typeof(WindowsPc).GetField("_reading", BindingFlags.NonPublic | BindingFlags.Instance)!;
        Assert.True(SpinWait.SpinUntil(() => (int)reading.GetValue(pc)! == 0, TimeSpan.FromSeconds(2)));
        Assert.Empty(pc.Apps);
        Assert.Equal(0, published);
    }

    private sealed class TimeoutClock : TimeProvider
    {
        private readonly List<TimeoutTimer> _timers = [];
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            Assert.Equal(TimeSpan.FromMilliseconds(200), dueTime);
            var timer = new TimeoutTimer(callback, state);
            _timers.Add(timer);
            return timer;
        }
        public void Expire()
        {
            Assert.Contains(_timers, timer => timer.Active);
            foreach (var timer in _timers.ToArray()) timer.Fire();
        }
        private sealed class TimeoutTimer(TimerCallback callback, object? state) : ITimer
        {
            public bool Active { get; private set; } = true;
            public void Fire() { if (Active) { Active = false; callback(state); } }
            public bool Change(TimeSpan dueTime, TimeSpan period) => throw new NotSupportedException();
            public void Dispose() => Active = false;
            public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        }
    }
    [Fact]
    public async Task Native_timeout_reports_unavailable_without_starting_overlapping_retries()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var clock = new TimeoutClock();
        using var release = new ManualResetEventSlim();
        int calls = 0;
        using var pc = new WindowsPc((_, _, _) =>
        {
            if (Interlocked.Increment(ref calls) == 1)
            {
                started.SetResult();
                if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("Fixture was not released");
            }
            return [];
        }, fileTimeout: TimeSpan.FromMilliseconds(200), timeProvider: clock);
        try
        {
            var first = pc.FindFilesAsync("first", 8, default);
            await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            clock.Expire();
            var failure = await Assert.ThrowsAsync<FileSearchUnavailableException>(() => first);
            Assert.IsType<TimeoutException>(failure.InnerException);
            var next = pc.FindFilesAsync("next", 8, default);
            Assert.False(next.IsCompleted); // still waiting for the first native call to leave
            clock.Expire();
            await Assert.ThrowsAsync<FileSearchUnavailableException>(() => next);
            Assert.Equal(1, calls);
        }
        finally { release.Set(); }
        Assert.Empty(await pc.FindFilesAsync("recovered", 8, default));
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task Native_cancel_runs_off_the_caller_and_cannot_overlap_disposal()
    {
        using var token = new CancellationTokenSource();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        int callerThread = Environment.CurrentManagedThreadId, cancelThread = 0, cleaned = 0;
        var cancellation = new WindowsPc.QueryCancellation(token.Token, () =>
        {
            cancelThread = Environment.CurrentManagedThreadId;
            entered.Set();
            if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("Fixture was not released");
        }, () => Interlocked.Increment(ref cleaned));
        token.Cancel(); // must return while native cancellation is still blocked
        Assert.True(entered.Wait(TimeSpan.FromSeconds(10)));
        Assert.NotEqual(callerThread, cancelThread);
        var disposing = Task.Run(cancellation.Dispose);
        try { Assert.Equal(0, Volatile.Read(ref cleaned)); }
        finally { release.Set(); }
        await disposing;
        Assert.Equal(1, cleaned);
        cancellation.Dispose();
        Assert.Equal(1, cleaned);
    }

    [Fact]
    public void A_finished_native_query_is_not_canceled_after_cleanup()
    {
        using var token = new CancellationTokenSource();
        int canceled = 0, cleaned = 0;
        using var cancellation = new WindowsPc.QueryCancellation(token.Token, () => canceled++, () => cleaned++);
        cancellation.Dispose();
        token.Cancel();
        Assert.Equal(0, canceled);
        Assert.Equal(1, cleaned);
    }

    private static ImageSource FrozenImage()
    {
        var image = new DrawingImage();
        image.Freeze();
        return image;
    }
}
