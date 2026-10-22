// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Globalization;
using Chippi.Core.Modules;
using Chippi.Features.Downloads.Engine;
using Chippi.Features.Downloads.Import;
using Chippi.Features.Downloads.Model;
using Chippi.Features.Downloads.Storage;
using Chippi.UI.Controls;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace Chippi.Features.Downloads;

public enum DownloadsRequestKind
{
    /// <summary>Show a preview for <see cref="DownloadsRequest.Folder"/> (the Explorer menu).</summary>
    Organize,

    /// <summary>Show a run in History.</summary>
    Run,

    /// <summary>Show the pending list.</summary>
    Review,
}

/// <summary>Something outside the views asked to show part of the organizer (a notice click, the Explorer menu).</summary>
public sealed record DownloadsRequest(DownloadsRequestKind Kind, string? Folder = null, long? RunId = null);

/// <summary>
/// The Downloads organizer. Owns the engine and the history database while installed; the views
/// (P2-C) read <see cref="Engine"/> and save settings through <see cref="SaveSettings"/>.
/// </summary>
public sealed class DownloadsModule : IFeatureModule
{
    private ModuleContext? _context;
    private SqliteHistoryStore? _history;

    public FeatureDescriptor Descriptor { get; } = new("downloads-organizer", "Downloads organizer", "Sorts new downloads into folders by your rules, with preview and undo", FeatureGroup.Files)
    {
        Icon = nameof(LucideIcons.Folder),
        Presets = PresetMembership.Essentials | PresetMembership.PowerUser,
        Requires = [Capability.ShellIntegration],
    };

    /// <summary>The running engine; null while uninstalled.</summary>
    public DownloadsEngine? Engine { get; private set; }

    public DownloadsSettings Settings { get; private set; } = new();

    /// <summary>Raised on the UI thread when a notice click or the Explorer menu asks for a view.</summary>
    public event Action<DownloadsRequest>? Requested;

    /// <summary>Where imports are extracted while they wait for preview.</summary>
    public string? ImportStaging => _context is null ? null : Path.Combine(_context.DataDirectory, "imports");

    public Task StartAsync(ModuleContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        _context = context;
        context.Notifier.Declare(DownloadsNotices.Kinds);

        Settings = context.Settings.Get<DownloadsSettings>();
        if (!Settings.Initialized)
        {
            // First start: the user's Downloads folder, in Manual, so its first contact is a preview.
            if (DownloadsFolder() is { } downloads && Directory.Exists(downloads))
                Settings.Folders.Add(new WatchedFolder { Path = downloads, Mode = FolderMode.Manual, BaselineUtc = DateTimeOffset.UtcNow });
            Settings.Initialized = true;
            context.Settings.Save(Settings);
        }

        // Extracted archives whose preview never finished (the app closed) are not kept.
        ArchiveImporter.DeleteStaging(ImportStaging!, context.Logger);

        _history = new SqliteHistoryStore(Path.Combine(context.DataDirectory, "history.db"));
        Engine = new DownloadsEngine(_history, context.Notifier, context.Logger);
        Engine.Start(Settings);

        context.Activation.Register(DownloadsNotices.OrganizeVerb, args =>
            Raise(new DownloadsRequest(DownloadsRequestKind.Organize, Folder: args.FirstOrDefault())));
        context.Activation.Register(DownloadsNotices.RunVerb, args =>
            Raise(new DownloadsRequest(DownloadsRequestKind.Run, RunId: long.TryParse(args.FirstOrDefault(), NumberStyles.None, CultureInfo.InvariantCulture, out long id) ? id : null)));
        context.Activation.Register(DownloadsNotices.ReviewVerb, _ => Raise(new DownloadsRequest(DownloadsRequestKind.Review)));
        return Task.CompletedTask;
    }

    /// <summary>
    /// Saves and applies settings. A folder that is new, or newly set to Silent, gets a fresh baseline
    /// so the files already in it are never moved silently.
    /// </summary>
    public void SaveSettings(DownloadsSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (_context is null || Engine is null)
            throw new InvalidOperationException("The Downloads organizer is uninstalled.");
        var now = DateTimeOffset.UtcNow;
        foreach (var folder in settings.Folders)
        {
            var before = Settings.Folders.FirstOrDefault(f => string.Equals(f.Path, folder.Path, StringComparison.OrdinalIgnoreCase));
            if (before is null || folder.Mode == FolderMode.Silent && before.Mode != FolderMode.Silent)
                folder.BaselineUtc = now;
        }
        _context.Settings.Save(settings);
        Settings = settings;
        Engine.Apply(settings);
    }

    public Task StopAsync(CancellationToken ct)
    {
        Engine?.Dispose();
        Engine = null;
        _history?.Dispose();
        _history = null;
        _context = null;
        return Task.CompletedTask;
    }

    private void Raise(DownloadsRequest request)
    {
        if (Requested is null)
            _context?.Logger.LogInformation("No view is listening for {Request}", request);
        Requested?.Invoke(request);
    }

    /// <summary>The Downloads known folder (it can be moved), else the profile default.</summary>
    private static string? DownloadsFolder()
    {
        const string key = @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Explorer\User Shell Folders";
        if (Registry.GetValue(key, "{374DE290-123F-4565-9164-39C4925E467B}", null) is string path && path.Length > 0)
            return Environment.ExpandEnvironmentVariables(path);
        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return profile.Length == 0 ? null : Path.Combine(profile, "Downloads");
    }
}
