// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Globalization;
using Chippi.Core.Modules;
using Chippi.Core.SystemIntegration;
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

    /// <summary>Show the rules editor.</summary>
    Rules,

    /// <summary>Show a run in History with its inline Undo confirmation (a notification's Undo button). Nothing is undone until confirmed.</summary>
    Undo,
}

/// <summary>Something asked to show part of the organizer (a notice click, the Explorer menu, the Tidy tab).</summary>
/// <param name="FromExplorer">The Explorer menu asked: the run is recorded as Explorer, not Organize now.</param>
public sealed record DownloadsRequest(DownloadsRequestKind Kind, string? Folder = null, long? RunId = null, bool FromExplorer = false);

/// <summary>
/// The File organizer. Owns the engine and the history database while installed; the views
/// (P2-C) read <see cref="Engine"/> and save settings through <see cref="SaveSettings"/>.
/// </summary>
public sealed class DownloadsModule : IFeatureModule
{
    private ExplorerRegistration? _explorer;

    public DownloadsModule() { }

    public DownloadsModule(ExplorerRegistration explorer)
    {
        _explorer = explorer;
    }

    private ModuleContext? _context;
    private SqliteHistoryStore? _history;

    public FeatureDescriptor Descriptor { get; } = new("downloads-organizer", "File organizer", "Sorts new files into folders by your rules", FeatureGroup.Files)
    {
        Icon = nameof(LucideIcons.Folder),
        Presets = PresetMembership.Essentials | PresetMembership.PowerUser,
        Requires = [Capability.ShellIntegration],
    };

    /// <summary>The running engine; null while uninstalled.</summary>
    public DownloadsEngine? Engine { get; private set; }

    public DownloadsSettings Settings { get; private set; } = new();

    /// <summary>Raised on the UI thread after a notice click, the Explorer menu or the Tidy tab opened a view in Settings.</summary>
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
        try { GetExplorer()?.SetEnabled(Settings.OrganizeInExplorer); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        { context.Logger.LogWarning(ex, "Explorer registration could not be refreshed"); }

        context.Activation.Register(DownloadsNotices.OrganizeVerb, args =>
            Raise(new DownloadsRequest(DownloadsRequestKind.Organize, Folder: args.FirstOrDefault(), FromExplorer: true)));
        context.Activation.Register(DownloadsNotices.RunVerb, args =>
            Raise(new DownloadsRequest(DownloadsRequestKind.Run, RunId: long.TryParse(args.FirstOrDefault(), NumberStyles.None, CultureInfo.InvariantCulture, out long id) ? id : null)));
        context.Activation.Register(DownloadsNotices.ReviewVerb, _ => Raise(new DownloadsRequest(DownloadsRequestKind.Review)));
        context.Activation.Register(DownloadsNotices.UndoVerb, args =>
            Raise(new DownloadsRequest(DownloadsRequestKind.Undo, RunId: long.TryParse(args.FirstOrDefault(), NumberStyles.None, CultureInfo.InvariantCulture, out long id) ? id : null)));

        context.Ui.SetSettingsPage(section => new Views.Settings.DownloadsPage(this, section));
        context.Ui.AddFlyoutPanel("Tidy", nameof(LucideIcons.Folder), () => new Views.Tidy.TidyView(this, RequestView, () => RequestView(new DownloadsRequest(DownloadsRequestKind.Rules))),
            () => new Views.Tidy.TidyIslandView(this, RequestView));
        return Task.CompletedTask;
    }

    /// <summary>
    /// The Settings deep link for a request, for the shell to open (<c>ShellCommands.OpenSettings</c>):
    /// <c>module:downloads-organizer/preview/C:\Users\…\Downloads</c>, <c>…/history/42</c>, <c>…/rules</c>, <c>…/review</c>.
    /// </summary>
    public static string SettingsLink(DownloadsRequest request) => $"module:downloads-organizer/{Section(request)}";

    private static string Section(DownloadsRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return request switch
        {
            { Kind: DownloadsRequestKind.Organize, Folder: { Length: > 0 } folder, FromExplorer: true } => $"explorer/{folder}",
            { Kind: DownloadsRequestKind.Organize, Folder: { Length: > 0 } folder } => $"preview/{folder}",
            { Kind: DownloadsRequestKind.Organize or DownloadsRequestKind.Review } => "review",
            { Kind: DownloadsRequestKind.Undo, RunId: { } run } => $"history/{run}/undo",
            { Kind: DownloadsRequestKind.Run, RunId: { } run } => $"history/{run}",
            { Kind: DownloadsRequestKind.Run or DownloadsRequestKind.Undo } => "history",
            _ => "rules",
        };
    }

    /// <summary>
    /// Saves and applies settings. A folder that is new, or newly set to Silent, gets a fresh baseline
    /// so the files already in it are never moved silently.
    /// </summary>
    public void SaveSettings(DownloadsSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (_context is null || Engine is null)
            throw new InvalidOperationException("The File organizer is uninstalled.");
        var now = DateTimeOffset.UtcNow;
        foreach (var folder in settings.Folders)
        {
            var before = Settings.Folders.FirstOrDefault(f => string.Equals(f.Path, folder.Path, StringComparison.OrdinalIgnoreCase));
            if (before is null || folder.Mode == FolderMode.Silent && before.Mode != FolderMode.Silent)
                folder.BaselineUtc = now;
        }
        bool explorerChanged = settings.OrganizeInExplorer != Settings.OrganizeInExplorer;
        if (explorerChanged) _explorer?.SetEnabled(settings.OrganizeInExplorer);
        try { _context.Settings.Save(settings); }
        catch
        {
            if (explorerChanged)
            {
                try { GetExplorer()?.SetEnabled(Settings.OrganizeInExplorer); }
                catch (Exception ex) { _context.Logger.LogWarning(ex, "Explorer registration rollback failed"); }
            }
            throw;
        }
        Settings = settings;
        Engine.Apply(settings);
    }

    /// <summary>Asks the Settings views to show <paramref name="request"/>. Call on the UI thread (the Tidy tab does).</summary>
    public void RequestView(DownloadsRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        Raise(request);
    }

    public async Task StopAsync(CancellationToken ct)
    {
        // Views see null first, so none starts reading history while it closes. Not cancellable:
        // history closes only after the file being moved right now is settled.
        var engine = Engine;
        Engine = null;
        if (engine is not null)
            await Task.Run(engine.Dispose, CancellationToken.None).ConfigureAwait(false);
        _history?.Dispose();
        _history = null;
        _context = null;
    }

    public Task UninstallAsync(CancellationToken ct)
    {
        GetExplorer()?.SetEnabled(false);
        return Task.CompletedTask;
    }

    private ExplorerRegistration? GetExplorer()
    {
        if (_explorer is not null) return _explorer;
        try { return _explorer = new ExplorerRegistration(Environment.GetEnvironmentVariable("CHIPPI_DEV_PROFILE")); }
        catch (ArgumentException ex)
        {
            _context?.Logger.LogWarning(ex, "Skipping Explorer registration for an invalid dev profile");
            return null;
        }
    }

    /// <summary>Opens Settings at the request's section, then tells anyone listening.</summary>
    private void Raise(DownloadsRequest request)
    {
        _context?.OpenSettings(Section(request));
        Requested?.Invoke(request);
    }

    /// <summary>The Downloads known folder (it can be moved), else the profile default.</summary>
    public static string? DownloadsFolder()
    {
        const string key = @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Explorer\User Shell Folders";
        if (Registry.GetValue(key, "{374DE290-123F-4565-9164-39C4925E467B}", null) is string path && path.Length > 0)
            return Environment.ExpandEnvironmentVariables(path);
        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return profile.Length == 0 ? null : Path.Combine(profile, "Downloads");
    }
}
