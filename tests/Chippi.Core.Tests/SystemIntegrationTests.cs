// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.Core.Diagnostics;
using Chippi.Core.SystemIntegration;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace Chippi.Core.Tests;

public sealed class SingleInstanceTests
{
    [Fact]
    public void Second_launch_signals_the_first_and_is_refused()
    {
        var name = "ChippiTest-" + Guid.NewGuid().ToString("N");
        using var first = SingleInstance.TryAcquire(name);
        Assert.NotNull(first);
        using var signalled = new ManualResetEventSlim();
        first!.ActivationRequested += (_, _) => signalled.Set();

        var second = SingleInstance.TryAcquire(name);

        Assert.Null(second);
        Assert.True(signalled.Wait(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task A_request_that_arrives_before_anyone_subscribes_is_delivered_on_subscribe()
    {
        var name = "ChippiTest-" + Guid.NewGuid().ToString("N");
        using var first = SingleInstance.TryAcquire(name);
        Assert.Null(SingleInstance.TryAcquire(name));
        await Task.Delay(200); // let the listener consume the signal with no subscriber

        using var signalled = new ManualResetEventSlim();
        first!.ActivationRequested += (_, _) => signalled.Set();

        Assert.True(signalled.Wait(TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public void The_owner_publishes_its_process_id_for_the_foreground_handoff()
    {
        var name = "ChippiTest-" + Guid.NewGuid().ToString("N");
        using var first = SingleInstance.TryAcquire(name);
        Assert.Equal(Environment.ProcessId, SingleInstance.ReadOwner(name));

        // The second launch grants the foreground and still signals.
        using var signalled = new ManualResetEventSlim();
        first!.ActivationRequested += (_, _) => signalled.Set();
        Assert.Null(SingleInstance.TryAcquire(name));
        Assert.True(signalled.Wait(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void The_owner_record_goes_away_with_the_owner()
    {
        var name = "ChippiTest-" + Guid.NewGuid().ToString("N");
        SingleInstance.TryAcquire(name)!.Dispose();

        Assert.Null(SingleInstance.ReadOwner(name));
    }
    [Fact]
    public void Name_is_free_again_after_dispose()
    {
        var name = "ChippiTest-" + Guid.NewGuid().ToString("N");
        SingleInstance.TryAcquire(name)!.Dispose();

        using var again = SingleInstance.TryAcquire(name);

        Assert.NotNull(again);
    }
}

public sealed class StartupRegistrationTests : IDisposable
{
    private readonly string _keyPath = @"Software\Chippi.Tests\" + Guid.NewGuid().ToString("N");

    public void Dispose() => Registry.CurrentUser.DeleteSubKeyTree(@"Software\Chippi.Tests", throwOnMissingSubKey: false);

    [Fact]
    public void Enable_writes_a_quoted_command_with_the_startup_flag_and_disable_removes_it()
    {
        var startup = new StartupRegistration("Chippi", _keyPath);
        Assert.False(startup.IsEnabled());

        startup.SetEnabled(true, @"C:\Users\me\AppData\Local\Chippi\current\Chippi.exe");
        Assert.True(startup.IsEnabled());
        Assert.Equal(@"""C:\Users\me\AppData\Local\Chippi\current\Chippi.exe"" --startup", startup.GetCommand());

        startup.SetEnabled(false);
        Assert.False(startup.IsEnabled());
        startup.SetEnabled(false); // idempotent
    }
}

public sealed class RollingFileLogTests : IDisposable
{
    private readonly TempDir _dir = new();

    public void Dispose() => _dir.Dispose();

    [Fact]
    public void Rolls_at_the_size_cap_and_keeps_a_bounded_number_of_files()
    {
        var log = new RollingFileLog(_dir.Path, "chippi", maxFileBytes: 2_000, maxFiles: 3);
        for (int i = 0; i < 500; i++)
            log.Write(LogLevel.Information, "Test", $"line {i:D4} " + new string('x', 40));

        var files = Directory.GetFiles(_dir.Path).Select(f => Path.GetFileName(f)!).Order().ToArray();
        Assert.Equal(["chippi.1.log", "chippi.2.log", "chippi.log"], files);
        Assert.All(Directory.GetFiles(_dir.Path), f => Assert.True(new FileInfo(f).Length <= 2_000));
        Assert.Contains("line 0499", File.ReadAllText(log.CurrentFile), StringComparison.Ordinal);
    }

    [Fact]
    public void An_entry_larger_than_the_cap_is_truncated_to_fit()
    {
        var log = new RollingFileLog(_dir.Path, "chippi", maxFileBytes: 2_000, maxFiles: 3);
        log.Write(LogLevel.Error, "Test", new string('x', 10_000), new InvalidOperationException(new string('y', 5_000)));
        log.Write(LogLevel.Information, "Test", "after");

        Assert.All(Directory.GetFiles(_dir.Path), f => Assert.True(new FileInfo(f).Length <= 2_000));
        Assert.Contains("[truncated]", string.Concat(Directory.GetFiles(_dir.Path).Select(File.ReadAllText)), StringComparison.Ordinal);
        Assert.Contains("after", File.ReadAllText(log.CurrentFile), StringComparison.Ordinal);
    }

    [Fact]
    public void Logger_provider_writes_level_category_and_exception()
    {
        var log = new RollingFileLog(_dir.Path);
        using var provider = new RollingFileLoggerProvider(log, LogLevel.Information);
        var logger = provider.CreateLogger("Modules");

        logger.LogDebug("hidden");
        logger.LogError(new InvalidOperationException("kaboom"), "Start failed for {Id}", "keep-awake");

        var text = File.ReadAllText(log.CurrentFile);
        Assert.DoesNotContain("hidden", text, StringComparison.Ordinal);
        Assert.Contains("ERR [Modules] Start failed for keep-awake", text, StringComparison.Ordinal);
        Assert.Contains("kaboom", text, StringComparison.Ordinal);
    }
}
