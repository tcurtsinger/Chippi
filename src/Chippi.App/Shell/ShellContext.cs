// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.IO;
using Chippi.Core.Config;
using Chippi.Core.Island;
using Chippi.Core.Modules;
using Chippi.Core.SystemIntegration;
using Microsoft.Extensions.Logging;

namespace Chippi.App.Shell;

/// <summary>
/// Everything the shell windows share, built once by the composition root in <see cref="App"/>.
/// Tray and flyout (WS1), settings (WS2) and island (WS3) take this in their constructors rather
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

    /// <summary>
    /// Module data folders, activation verbs and notices. P2-D sets <see cref="ModuleServices.Toasts"/>
    /// when the Windows notification backend exists.
    /// </summary>
    public ModuleServices Services { get; }

    /// <summary>
    /// A later launch, an Explorer menu click or a notice click: a registered verb goes to its module
    /// (<c>--organize "C:\Downloads"</c>); anything else, including a plain launch, shows the flyout.
    /// Call on the UI thread.
    /// </summary>
    public void HandleActivation(IReadOnlyList<string> arguments)
    {
        if (!Services.Activation.TryRoute(arguments))
            Commands.ShowFlyout();
    }

    /// <summary>
    /// Debug surfaces: placeholder flyout tabs (brief §4.4) and the island's fake-activity menu
    /// (brief §4.6). On with <c>--debug</c> or <c>CHIPPI_DEBUG=1</c>, in any build.
    /// </summary>
    public bool DebugMode { get; }
}
