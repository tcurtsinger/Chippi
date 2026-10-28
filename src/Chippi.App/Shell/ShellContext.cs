// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.IO;
using Chippi.Clipboard;
using Chippi.Clipboard.Windows;
using Chippi.Core.Config;
using Chippi.Core.Island;
using Chippi.Core.Modules;
using Chippi.Core.SystemIntegration;
using Microsoft.Extensions.Logging;

namespace Chippi.App.Shell;

/// <summary>
/// Everything the shell windows share, built once by the composition root in <see cref="App"/>.
/// Tray (WS1), settings (WS2) and island (WS3) take this in their constructors rather
/// than reaching into <see cref="App.Current"/>.
/// </summary>
public sealed class ShellContext
{
    /// <param name="devProfile">CHIPPI_DEV_PROFILE, or null for a normal run.</param>
    public ShellContext(ConfigStore config, ILoggerFactory loggerFactory, bool debugMode, string? devProfile = null)
    {
        Config = config;
        LoggerFactory = loggerFactory;
        DebugMode = debugMode;
        Island = new IslandActivityHub();
        Services = new ModuleServices
        {
            DataRoot = string.IsNullOrWhiteSpace(devProfile)
                ? ModuleServices.DefaultDataRoot
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Chippi-dev", devProfile, "data"),
        };
        Services.OpenSettings = page => Commands.OpenSettings(page);
        Services.OpenIslandPage = key => Commands.ShowPage(key);
        Services.HoldIslandOpen = () => Commands.HoldIslandOpen();
        // Its thread starts on first use; nothing listens until History records or Clean URL cleans automatically.
        Services.Clipboard = Clipboard = new ClipboardHub(new WindowsClipboardSystem(), new WindowsForeground(), log: loggerFactory.CreateLogger("Clipboard"));
        Registry = new ModuleRegistry(config, Island, loggerFactory, Services);
        Startup = string.IsNullOrWhiteSpace(devProfile)
            ? new StartupRegistration()
            : new StartupRegistration($"Chippi-dev-{devProfile}");
    }

    /// <summary>Run at startup. A dev profile gets its own Run value, so it never overwrites the real one.</summary>
    public StartupRegistration Startup { get; }

    public ConfigStore Config { get; }

    public ILoggerFactory LoggerFactory { get; }

    public IslandActivityHub Island { get; }

    public ModuleRegistry Registry { get; }

    public ShellCommands Commands { get; } = new();

    /// <summary>The shared clipboard service. Disposed at exit, after the modules stop.</summary>
    public ClipboardHub Clipboard { get; }

    /// <summary>
    /// Module data folders, activation verbs and notices. P2-D sets <see cref="ModuleServices.Toasts"/>
    /// when the Windows notification backend exists.
    /// </summary>
    public ModuleServices Services { get; }

    /// <summary>
    /// A later launch, an Explorer menu click or a notice click: a registered verb goes to its module
    /// (<c>--organize "C:\Downloads"</c>); anything else, including a plain launch, shows the island's pages.
    /// Until <see cref="ModulesStarted"/> runs, requests wait in order, so a verb is never lost to a
    /// module that hasn't registered it yet. Call on the UI thread.
    /// </summary>
    public void HandleActivation(IReadOnlyList<string> arguments)
    {
        if (!_modulesStarted)
        {
            _waitingActivations.Enqueue(arguments);
            return;
        }
        if (!Services.Activation.TryRoute(arguments))
            Commands.ShowPages();
    }

    /// <summary>
    /// Installed modules have started (their verbs are registered): route the first launch's own
    /// verb, if it has one, then every request that waited. Call once, on the UI thread.
    /// </summary>
    public void ModulesStarted(IReadOnlyList<string> launchArguments)
    {
        if (_modulesStarted)
            return;
        _modulesStarted = true;
        _modulesReady.TrySetResult();
        Services.Activation.TryRoute(launchArguments); // app flags such as --startup are not verbs
        while (_waitingActivations.TryDequeue(out var waiting))
            HandleActivation(waiting);
    }

    private readonly Queue<IReadOnlyList<string>> _waitingActivations = new();
    private bool _modulesStarted;
    private readonly TaskCompletionSource _modulesReady = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Completes once the installed modules have started, so their tiles and pages exist (debug snapshots wait on it).</summary>
    public Task ModulesReady => _modulesReady.Task;

    /// <summary>
    /// Debug surfaces: the island's fake-activity menu (brief §4.6). On with <c>--debug</c> or
    /// <c>CHIPPI_DEBUG=1</c>, in any build.
    /// </summary>
    public bool DebugMode { get; }
}
