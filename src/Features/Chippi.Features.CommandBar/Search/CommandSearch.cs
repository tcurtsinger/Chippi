// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.IO;
using Chippi.Core.Modules;

namespace Chippi.Features.CommandBar.Search;

/// <summary>
/// What the field's text finds, in the order the rows show it (docs/phase-5.5/command-bar.md, What it finds): values and
/// answers first, then matches, the ones you pick most for the same letters higher, then names that start with what you
/// typed, then by kind (Chippi, apps, windows, Settings, files, clipboard, emoji); the web row always last. Hidden results
/// and the kinds switched off never show.
/// </summary>
internal sealed class CommandSearch(
    IPc pc,
    Func<IReadOnlyList<ChippiCommand>> commands,
    Func<string, IReadOnlyList<ChippiCommand>> searchCommands,
    Func<CommandBarSettings> settings,
    Uses uses,
    Func<string, Task> copy)
{
    /// <summary>Whole rows only, never scrolling: 8 at most.</summary>
    public const int Rows = 8;

    private IReadOnlyList<StartApp>? _appsFrom;
    private List<Candidate> _apps = [];
    private static readonly Lazy<List<Candidate>> Fixed = new(() =>
    [
        .. SettingsPages.All.Select(p => new Candidate(new Result("settings:" + p.Uri, ResultKind.Settings, p.Name, "Windows Settings", () => Task.CompletedTask) { Words = p.Words, Icon = "Settings" })),
        .. EmojiList.All.Select(e => new Candidate(new Result("emoji:" + e.Text, ResultKind.Emoji, e.Name, "Emoji", () => Task.CompletedTask) { Words = e.Words, Emoji = e.Text })),
    ]);

    /// <summary>
    /// The rows for <paramref name="text"/>, given the windows open when the page did and the files Windows Search has
    /// found for this text so far.
    /// </summary>
    public IReadOnlyList<Result> Find(string text, IReadOnlyList<OpenWindow> windows, IReadOnlyList<FoundFile> files)
    {
        text = text.Trim();
        if (text.Length == 0)
            return MostUsed(windows);
        var now = settings();
        var hidden = now.Hidden.Select(h => h.Key).ToHashSet(StringComparer.Ordinal);
        bool On(ResultSwitch kind) => !now.Off.Contains(kind);
        string query = text.ToLowerInvariant();
        string[] words = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        var first = new List<Result>();
        var matches = new List<(Result Result, int Picks, int Tier)>();
        void Add(Result result, int tier)
        {
            if (tier >= 0 && On(result.Switch) && !hidden.Contains(result.Key))
                matches.Add((result, uses.For(text, result.Key), tier));
        }

        // Values typed in come first, then a sum or a conversion.
        if (On(ResultSwitch.Chippi) || On(ResultSwitch.Clipboard))
        {
            foreach (var command in searchCommands(text))
            {
                var result = From(command);
                if (result.Kind == ResultKind.Value)
                {
                    if (On(ResultSwitch.Chippi))
                        first.Add(result);
                }
                else
                    Add(result, Tier(new Candidate(result), query, words) is 0 ? 0 : 1); // the feature matched it already
            }
        }
        if (On(ResultSwitch.Math) && Calculator.Solve(text) is { } answer)
            first.Add(new Result("answer", ResultKind.Answer, answer.Name, answer.Detail, () => copy(answer.Copy)) { Icon = "Equal" });

        if (On(ResultSwitch.Chippi))
            foreach (var command in commands())
            {
                var candidate = new Candidate(From(command));
                Add(candidate.Result, Tier(candidate, query, words));
            }
        if (On(ResultSwitch.Apps))
            foreach (var app in Apps())
                Add(app.Result, Tier(app, query, words));
        if (On(ResultSwitch.Windows))
            foreach (var window in windows)
            {
                var candidate = new Candidate(Window(window));
                Add(candidate.Result, Tier(candidate, query, words));
            }
        foreach (var candidate in Fixed.Value)
        {
            if (On(candidate.Result.Switch))
                Add(candidate.Result, Tier(candidate, query, words));
        }
        if (On(ResultSwitch.Files))
            foreach (var file in files)
            {
                var result = File(file);
                Add(result, Tier(new Candidate(result), query, words) is 0 ? 0 : 1); // Windows Search matched it already
            }

        bool web = On(ResultSwitch.Web);
        var rows = first.Take(web ? Rows - 1 : Rows).ToList();
        rows.AddRange(matches
            .OrderByDescending(m => m.Picks)
            .ThenBy(m => m.Tier)
            .ThenBy(m => m.Result.Kind)
            .ThenBy(m => m.Result.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(m => m.Result)
            .DistinctBy(r => r.Key)
            .Take((web ? Rows - 1 : Rows) - rows.Count));
        if (web)
            rows.Add(Web(text, now.WebSearch));
        return Live(rows);
    }

    /// <summary>The empty field: your most used, then Chippi's pages to fill the rows.</summary>
    public IReadOnlyList<Result> MostUsed(IReadOnlyList<OpenWindow> windows)
    {
        var now = settings();
        var hidden = now.Hidden.Select(h => h.Key).ToHashSet(StringComparer.Ordinal);
        bool Shows(Result r) => !now.Off.Contains(r.Switch) && !hidden.Contains(r.Key);
        var chippi = commands().Select(From).Where(r => r.Kind == ResultKind.Chippi).ToList();
        var known = new Dictionary<string, Result>(StringComparer.Ordinal);
        foreach (var result in chippi
            .Concat(Apps().Select(a => a.Result))
            .Concat(windows.Select(Window))
            .Concat(Fixed.Value.Select(c => c.Result)))
            known.TryAdd(result.Key, result);
        var rows = new List<Result>();
        foreach (var key in uses.MostUsed())
        {
            if (rows.Count == Rows)
                break;
            var result = known.GetValueOrDefault(key) ?? FileFromKey(key);
            if (result is not null && Shows(result))
                rows.Add(result);
        }
        foreach (var page in chippi.Where(r => r.Opens))
        {
            if (rows.Count == Rows)
                break;
            if (Shows(page) && !rows.Exists(r => r.Key == page.Key))
                rows.Add(page);
        }
        return Live(rows);
    }

    /// <summary>Each row's Run as called: the pick counts for the letters typed.</summary>
    public void Picked(string text, Result result) => uses.Picked(text, result.Key);

    // The fixed lists' rows run nothing until here, where the PC is at hand.
    private IReadOnlyList<Result> Live(List<Result> rows) => [.. rows.Select(r => r.Kind switch
    {
        ResultKind.Settings => r with { Run = Open(r.Key["settings:".Length..]) },
        ResultKind.Emoji => r with { Run = () => copy(r.Emoji!) },
        _ => r,
    })];

    private List<Candidate> Apps()
    {
        var apps = pc.Apps;
        if (!ReferenceEquals(apps, _appsFrom))
        {
            _apps = [.. apps.Select(a => new Candidate(new Result("app:" + a.ParsingName, ResultKind.App, a.Name, "App", Open(@"shell:AppsFolder\" + a.ParsingName)) { App = a.ParsingName }))];
            _appsFrom = apps;
        }
        return _apps;
    }

    private Func<Task> Open(string target) => () =>
    {
        pc.Open(target);
        return Task.CompletedTask;
    };

    private static Result From(ChippiCommand command) => new("chippi:" + command.Key, command.Kind switch
    {
        ChippiCommandKind.Value => ResultKind.Value,
        ChippiCommandKind.Paste => ResultKind.Clipboard,
        _ => ResultKind.Chippi,
    }, command.Name, command.Detail, command.Run)
    {
        Words = command.Words,
        Icon = command.Kind == ChippiCommandKind.Paste ? "Clipboard" : command.Icon,
        Level = command.Level,
        Opens = command.Kind == ChippiCommandKind.Page,
    };

    private Result Window(OpenWindow window) =>
        new($"window:{window.App}|{window.Title}", ResultKind.Window, window.Title, window.App, () =>
        {
            pc.Switch(window.Handle);
            return Task.CompletedTask;
        })
        { Words = window.App, Icon = "AppWindow" };

    private Result File(FoundFile file) =>
        new("file:" + file.Path, ResultKind.File, file.Name, file.Folder, Open(file.Path)) { Icon = file.IsFolder ? "Folder" : "File" };

    /// <summary>A file from your most used, while it's still there.</summary>
    private Result? FileFromKey(string key)
    {
        if (!key.StartsWith("file:", StringComparison.Ordinal))
            return null;
        string path = key["file:".Length..];
        bool isFolder = Directory.Exists(path);
        if (!isFolder && !System.IO.File.Exists(path))
            return null;
        var folder = Path.GetFileName(Path.GetDirectoryName(path.TrimEnd('\\'))) ?? "";
        return File(new FoundFile(path, Path.GetFileName(path.TrimEnd('\\')), folder, isFolder));
    }

    private Result Web(string text, WebEngine engine)
    {
        string url = engine.SearchUrl(text);
        return new Result("web", ResultKind.Web, $"Search the web for “{text}”", engine switch
        {
            WebEngine.Bing => "Bing",
            WebEngine.DuckDuckGo => "DuckDuckGo",
            _ => "Google",
        }, Open(url)) { Icon = "Globe" };
    }

    /// <summary>
    /// 0 when the name starts with the text, or its initials do ("vsc" for Visual Studio Code); 1 when every word typed
    /// starts a word of the name or its other words, or sits inside the name; -1 for no match.
    /// </summary>
    internal static int Tier(Candidate candidate, string query, string[] words)
    {
        if (candidate.Name.StartsWith(query, StringComparison.Ordinal))
            return 0;
        if (words.Length == 1 && query.Length >= 2 && candidate.Initials.StartsWith(query, StringComparison.Ordinal))
            return 0;
        foreach (var word in words)
        {
            if (!candidate.Words.Any(w => w.StartsWith(word, StringComparison.Ordinal))
                && !(word.Length >= 3 && candidate.Name.Contains(word, StringComparison.Ordinal)))
                return -1;
        }
        return 1;
    }

    /// <summary>A result with its name and words lowered and split once, for matching as you type.</summary>
    internal sealed class Candidate
    {
        private static readonly char[] Breaks = [' ', '-', '_', '.', '&', '(', ')', ',', ':', '/', '\\', '|', '·'];

        public Candidate(Result result)
        {
            Result = result;
            Name = result.Name.ToLowerInvariant();
            Words = [.. (Name + " " + result.Words.ToLowerInvariant()).Split(Breaks, StringSplitOptions.RemoveEmptyEntries)];
            Initials = new string([.. Name.Split(Breaks, StringSplitOptions.RemoveEmptyEntries).Select(w => w[0])]);
        }

        public Result Result { get; }

        public string Name { get; }

        public string[] Words { get; }

        public string Initials { get; }
    }
}
