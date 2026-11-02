// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

namespace Chippi.Features.QuickActions.Tests;

/// <summary>A PC that only records what it was asked: nothing in a test locks, sleeps or changes the owner's PC.</summary>
internal sealed class FakePc : IQuickSystem
{
    public QuickState State = QuickState.Empty;
    public readonly List<string> Calls = [];
    public readonly List<uint> Fronts = [];
    public bool Fail { get; set; }

    public static OpenApp Chrome { get; } = new(@"C:\Program Files\Google\Chrome\Application\chrome.exe", "Google Chrome", Front: true);
    public static OpenApp Spotify { get; } = new(@"C:\Users\me\AppData\Roaming\Spotify\Spotify.exe", "Spotify", Front: false);
    public static OpenApp Discord { get; } = new(@"C:\Users\me\AppData\Local\Discord\app-1.0\Discord.exe", "Discord", Front: false);

    public Task<QuickState> ReadAsync(uint frontProcessId, CancellationToken ct = default)
    {
        Fronts.Add(frontProcessId);
        return Task.FromResult(State);
    }

    public Task SetAsync(QuickActionId id, bool on)
    {
        Record($"set {id} {on}");
        State = State with { On = new Dictionary<QuickActionId, bool>(State.On) { [id] = on } };
        return Task.CompletedTask;
    }

    public Task SetPowerPlanAsync(Guid plan)
    {
        Record($"plan {plan}");
        State = State with { Plans = [.. State.Plans.Select(p => p with { Active = p.Id == plan })] };
        return Task.CompletedTask;
    }

    public Task EjectAsync(RemovableDrive drive)
    {
        Record($"eject {drive.Name}");
        State = State with { Drives = [.. State.Drives.Where(d => d != drive)] };
        return Task.CompletedTask;
    }

    public Task QuitAsync(OpenApp app)
    {
        Record($"quit {app.Name}");
        State = State with { Apps = [.. State.Apps.Where(a => a != app)] };
        return Task.CompletedTask;
    }

    public Task EndFrozenAppsAsync()
    {
        Record("end frozen");
        return Task.CompletedTask;
    }

    public Task RunAsync(QuickActionId id)
    {
        Record($"run {id}");
        return Task.CompletedTask;
    }

    public Task PowerAsync(PowerChoice choice)
    {
        Record($"power {choice}");
        return Task.CompletedTask;
    }

    private void Record(string call)
    {
        if (Fail)
            throw new InvalidOperationException("Windows said no");
        Calls.Add(call);
    }
}
