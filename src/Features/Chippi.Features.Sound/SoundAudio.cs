// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Threading;
using Chippi.Audio;
using Microsoft.Extensions.Logging;

namespace Chippi.Features.Sound;

/// <summary>The Sound modules share one service; each running module holds its own lease.</summary>
public static class SoundAudio
{
    private static readonly Lock Gate = new();
    private static AudioService? s_service;

    public static AudioService Get(ILogger logger)
    {
        lock (Gate)
            return s_service ??= new AudioService(() => new CoreAudioDeviceSystem(logger), Application.Current is { } app ? new DispatcherSynchronizationContext(app.Dispatcher) : SynchronizationContext.Current, logger);
    }
}