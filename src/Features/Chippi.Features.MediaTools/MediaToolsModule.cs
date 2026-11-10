// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.IO;
using Chippi.Core.Island;
using Chippi.Core.Modules;
using Chippi.Core.Shortcuts;
using Chippi.Media;
using Microsoft.Win32;

namespace Chippi.Features.MediaTools;

public sealed record MediaToolsSettings
{
    public MediaToolOptions Options { get; init; } = new();
    public Dictionary<string, KeyShortcut?> Shortcuts { get; init; } = [];
}

public sealed class MediaToolsModule : IFeatureModule
{
    public const string Id = "media-tools";
    public const string Page = "media-tools";
    public static readonly string[] ShortcutNames = ["Convert media", "Resize images", "Compress media"];
    private readonly IMediaToolsCodec _codec;
    private readonly List<IHotkey> _shortcuts = [];
    private ModuleContext? _context;
    private MediaToolsView? _view;
    private CancellationTokenSource? _lifetime;
    private bool _wasRunning;
    public MediaToolsModule() : this(new MediaToolsCodec()) { }
    public MediaToolsModule(IMediaToolsCodec codec) => _codec = codec;
    public MediaBatch? Batch { get; private set; }
    public MediaToolsSettings Settings { get; private set; } = new();
    public event Action? Changed;
    public FeatureDescriptor Descriptor { get; } = new(Id, "Media tools", "Convert, resize and compress images and videos", FeatureGroup.Capture) { Icon = "Video" };

    public Task StartAsync(ModuleContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested(); _context = context; _lifetime = new();
        Settings = context.Settings.Get<MediaToolsSettings>();
        Settings = Settings with { Options = Settings.Options ?? new(), Shortcuts = Settings.Shortcuts ?? [] };
        try { Settings.Options.Validate(); } catch (ArgumentException) { Settings = Settings with { Options = new() }; }
        var batch = Batch = new MediaBatch(_codec); batch.Changed += Publish;
        context.Ui.SetSettingsPage(_ => new MediaToolsSettingsView(this, context));
        context.Ui.AddIslandTile(new IslandTileSpec(Page, "Convert", "Video", 79, 0, View)
        { OnHome = false, CreateHeader = () => View().Header, InitialFocus = () => View().InitialFocus, HandleBack = () => View().Back() || context.TryGoBackIslandPage("capture") });
        Action[] actions = [() => Open(MediaTool.Convert, true), () => Open(MediaTool.Resize, true), () => Open(MediaTool.Compress, true)];
        for (int i = 0; i < actions.Length; i++) AddShortcut(ShortcutNames[i], actions[i]);
        context.Ui.AddCommands(() => actions.Select((action, i) => new ChippiCommand(Id + "/" + new[] { "convert", "resize", "compress" }[i], ShortcutNames[i], "Video", () => { action(); return Task.CompletedTask; }) { Words = "media image video convert resize compress" }).ToArray());
        Changed?.Invoke(); return Task.CompletedTask;
    }
    private MediaToolsView View() => _view ??= new(this, _context!);
    public void Open(MediaTool? tool = null, bool picker = false)
    {
        if (_context == null) return;
        if (tool is { } selected && Batch?.IsRunning != true && Settings.Options.Tool != selected) SaveOptions(Settings.Options with { Tool = selected });
        if (!_context.TryOpenIslandPage(Page)) _context.SummonIslandPage(Page);
        if (picker) _ = AddFilesAsync();
    }
    internal async Task AddFilesAsync()
    {
        if (_context == null || Batch == null || Batch.IsRunning || Batch.IsInspecting) return;
        using var hold = _context.HoldIslandOpen();
        var dialog = new OpenFileDialog { Title = "Add media files", Multiselect = true, Filter = "Media files|*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.tif;*.tiff;*.heic;*.heif;*.avif;*.webp;*.mp4;*.mov;*.m4v;*.avi;*.wmv;*.mkv;*.webm|All files|*.*" };
        if (dialog.ShowDialog() == true) await AddAsync(dialog.FileNames);
    }
    public async Task AddAsync(IEnumerable<string> paths)
    {
        var batch = Batch; var lifetime = _lifetime;
        if (batch == null || lifetime == null) return;
        try { await batch.AddAsync(paths, lifetime.Token); }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
    }
    internal bool SaveOptions(MediaToolOptions options)
    {
        if (_context == null || Batch?.IsRunning == true) return false;
        if (options == Settings.Options) return true;
        try { options.Validate(); _context.Settings.Save(Settings with { Options = options }); Settings = Settings with { Options = options }; Batch?.OptionsChanged(); Changed?.Invoke(); return true; }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException) { _view?.Report("Save failed · " + ex.Message); return false; }
    }
    private void Publish()
    {
        if (Batch is not { } batch || _context is not { } context) return;
        if (batch.IsRunning)
        {
            int complete = batch.Jobs.Count(j => j.State is MediaJobState.Done or MediaJobState.Failed);
            context.Island.Publish(new IslandActivity(Id, IslandActivityKind.Download, $"{complete} of {batch.Jobs.Count} converted", "Media tools")
            { OpensTile = Page, Icon = "Video" });
        }
        else
        {
            context.Island.Clear(Id);
            if (_wasRunning)
            {
                int done = batch.Jobs.Count(j => j.State == MediaJobState.Done), failed = batch.Jobs.Count(j => j.State == MediaJobState.Failed);
                long saving = batch.Jobs.Where(j => j.Output != null).Sum(j => j.Source!.Bytes - j.Output!.Bytes);
                string message = batch.Error ?? $"Converted {done}" + (failed > 0 ? $" · {failed} failed" : saving > 0 ? $" · {MediaToolsView.Bytes(saving)} saved" : "");
                context.Island.Publish(new IslandActivity(Id + "/done", IslandActivityKind.Status, message, "Media tools") { OpensTile = Page, Icon = "Video", IsFeedback = true });
            }
        }
        _wasRunning = batch.IsRunning; Changed?.Invoke();
    }
    private void AddShortcut(string name, Action action)
    {
        var context = _context!; IHotkey shortcut = null!;
        string? Save(KeyShortcut? keys)
        {
            var old = Settings.Shortcuts.GetValueOrDefault(name);
            if (keys != null && !shortcut.Register(keys, action)) return shortcut.Error;
            var next = Settings with { Shortcuts = new(Settings.Shortcuts) { [name] = keys } };
            try { context.Settings.Save(next); Settings = next; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { if (old != null) shortcut.Register(old, action); else shortcut.Unregister(); return "Save failed"; }
            if (keys == null) shortcut.Unregister(); return null;
        }
        shortcut = context.Shortcuts.Add(Id, name, new ShortcutEdits(() => Settings.Shortcuts.GetValueOrDefault(name), Save, () => Save(null)) { Icon = "Video" });
        _shortcuts.Add(shortcut); if (Settings.Shortcuts.GetValueOrDefault(name) is { } key) shortcut.Register(key, action);
    }
    public async Task StopAsync(CancellationToken ct)
    {
        _lifetime?.Cancel();
        if (Batch is { } batch) { await batch.StopAsync(); batch.Changed -= Publish; }
        _view?.Dispose(); _view = null;
        foreach (var key in _shortcuts) key.Dispose(); _shortcuts.Clear();
        _context?.Island.Clear(Id); _context?.Island.Clear(Id + "/done");
        Batch = null; _context = null; _lifetime?.Dispose(); _lifetime = null; _wasRunning = false; Changed?.Invoke();
    }
}
