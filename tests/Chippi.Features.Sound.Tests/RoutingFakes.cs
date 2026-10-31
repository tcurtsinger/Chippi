// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Collections.Concurrent;
using Chippi.Audio;
using Chippi.Audio.Tests;
using Chippi.Core.Island;
using Chippi.Core.Modules;
using Chippi.Core.Notifications;
using Chippi.Features.Sound.Routing;
using Microsoft.Extensions.Logging.Abstractions;

namespace Chippi.Features.Sound.Tests;

public sealed class RoutingFixture : IAsyncDisposable
{
    public FakeDeviceSystem System { get; } = new();
    public AudioService Audio { get; }
    public AudioRoutingModule Module { get; }
    public RoutingMemorySettings Settings { get; } = new();
    public RoutingNotifier Notices { get; } = new();
    public RoutingUiFake Ui { get; } = new();
    public FakeHotkey Hotkey { get; } = new();
    public ModuleContext Context { get; }

    public RoutingFixture(Func<AudioEndpoint, bool>? exclude = null)
    {
        Audio = new AudioService(() => System, null, options: new AudioServiceOptions { Debounce = TimeSpan.FromMilliseconds(20) });
        Module = new AudioRoutingModule(Audio, exclude, () => Hotkey);
        Context = new ModuleContext("audio-routing", Settings, NullLogger.Instance, new IslandActivityHub().CreatePublisher("audio-routing"), Ui) { Notifier = Notices };
    }

    public Task Start() => Module.StartAsync(Context, CancellationToken.None);
    public ValueTask DisposeAsync() => new(Module.StopAsync(CancellationToken.None));

    public static async Task Until(Func<bool> condition)
    {
        var end = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            if (DateTime.UtcNow > end) throw new TimeoutException("Routing condition not met");
            await Task.Delay(10);
        }
    }
}

public sealed class RoutingMemorySettings : IModuleSettings
{
    public RoutingSettings Value { get; set; } = new();
    public int Saves { get; private set; }
    public T Get<T>() where T : class, new() => (T)(object)Value;
    public void Save<T>(T value) where T : class { Value = (RoutingSettings)(object)value; Saves++; }
}

public sealed class RoutingNotifier : INotifier
{
    public ConcurrentQueue<Notice> Sent { get; } = new();
    public IReadOnlyList<NoticeKind> Kinds { get; private set; } = [];
    public void Declare(IReadOnlyList<NoticeKind> kinds) => Kinds = kinds;
    public NoticePlaces PlacesFor(string id) => Kinds.Single(k => k.Id == id).Default;
    public void SetPlaces(string id, NoticePlaces places) { }
    public void Show(Notice notice) => Sent.Enqueue(notice);
}

public sealed class RoutingUiFake : IModuleUi
{
    public List<IslandTileContribution> Tiles { get; } = [];
    public Func<string?, object>? Page { get; private set; }
    public IIslandTile AddIslandTile(IslandTileSpec spec)
    {
        var tile = new IslandTileContribution("audio-routing", spec);
        Tiles.Add(tile);
        return tile;
    }
    public void SetSettingsPage(Func<string?, object> createView) => Page = createView;
}