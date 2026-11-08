// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.Core.Input;

namespace Chippi.Features.TextSnippets.Tests;

public sealed class MatcherTests
{
    private static Snippet S(string trigger, string text = "x", ExpandMode mode = ExpandMode.RightAway, bool ignoreCase = false, bool enabled = true) =>
        new() { Trigger = trigger, Text = text, Expand = mode, IgnoreCase = ignoreCase, Enabled = enabled };

    private static SnippetMatcher With(params Snippet[] snippets)
    {
        var m = new SnippetMatcher();
        m.SetSnippets(snippets);
        return m;
    }

    private static SnippetMatch? Feed(SnippetMatcher m, string text)
    {
        SnippetMatch? last = null;
        foreach (char c in text)
            last = m.Type(c) ?? last;
        return last;
    }

    [Fact]
    public void RightAwayFiresOnTheLastCharacter()
    {
        var m = With(S(";em"));
        Assert.Null(m.Type(';'));
        Assert.Null(m.Type('e'));
        var hit = m.Type('m');
        Assert.Equal(0, m.Length); // the buffer cleared on the match
        Assert.Equal(";em", hit!.Value.Snippet.Trigger);
        Assert.Equal(3, hit.Value.Typed);
    }

    [Fact]
    public void TheLongestTriggerWins()
    {
        var m = With(S(";mail", mode: ExpandMode.AfterDelimiter), S("il", mode: ExpandMode.AfterDelimiter));
        Feed(m, ";mail");
        Assert.Equal(";mail", m.Delimiter()!.Value.Snippet.Trigger);
    }

    [Fact]
    public void AnotherTriggerEndingTheBufferFiresWithTextBefore()
    {
        var m = With(S(";em"));
        Assert.Equal(";em", Feed(m, "hello ;em")!.Value.Snippet.Trigger);
    }

    [Fact]
    public void DelimiterSnippetsWaitForTheKey()
    {
        var m = With(S(";addr", mode: ExpandMode.AfterDelimiter));
        Assert.Null(Feed(m, ";addr"));
        Assert.Equal(5, m.Delimiter()!.Value.Typed);
        Assert.Equal(0, m.Length);
    }

    [Fact]
    public void ADelimiterWithoutAMatchResets()
    {
        var m = With(S(";addr", mode: ExpandMode.AfterDelimiter));
        Feed(m, ";add");
        Assert.Null(m.Delimiter());
        Assert.Equal(0, m.Length);
    }

    [Fact]
    public void ASpaceBreaksATrigger()
    {
        var m = With(S(";ab"));
        Feed(m, ";a");
        m.Type(' ');
        Assert.Null(m.Type('b'));
    }

    [Fact]
    public void BackspaceRemovesTheLastCharacter()
    {
        var m = With(S(";ab"));
        Feed(m, ";ax");
        m.Backspace();
        Assert.NotNull(m.Type('b'));
    }

    [Fact]
    public void CaseRule()
    {
        Assert.Null(Feed(With(S(";Em")), ";em"));
        Assert.NotNull(Feed(With(S(";Em", ignoreCase: true)), ";eM"));
    }

    [Fact]
    public void DisabledAndShortTriggersAreIgnored()
    {
        Assert.Null(Feed(With(S(";em", enabled: false)), ";em"));
        Assert.Null(Feed(With(S("e")), "e"));
    }

    [Fact]
    public void TheBufferKeepsOnlyTheLast64()
    {
        var m = With(S(";em"));
        for (int i = 0; i < 200; i++)
            m.Type('a');
        Assert.Equal(SnippetMatcher.Capacity, m.Length);
        Assert.NotNull(Feed(m, ";em"));
    }
}

public sealed class RulesTests
{
    private static Snippet S(string trigger, string id = "") => new() { Id = id, Trigger = trigger };

    [Fact]
    public void TriggerErrors()
    {
        Assert.Equal(SnippetRules.TooShort, SnippetRules.Check(";", [], null));
        Assert.Equal(SnippetRules.NoSpaces, SnippetRules.Check(";a b", [], null));
        Assert.Equal(SnippetRules.TooLong, SnippetRules.Check(new string('a', 41), [], null));
        Assert.Null(SnippetRules.Check(new string('a', 40), [], null));
    }

    [Fact]
    public void DuplicatesIgnoreTheSnippetItself()
    {
        var all = new[] { S(";em", "1"), S(";x", "2") };
        Assert.Equal(SnippetRules.Duplicate, SnippetRules.Check(";em", all, "2"));
        Assert.Null(SnippetRules.Check(";em", all, "1"));
        Assert.Null(SnippetRules.Check(";EM", all, "2"));
        Assert.Equal(SnippetRules.Duplicate, SnippetRules.Check(";EM", all, "2", ignoreCase: true));
    }

    [Fact]
    public void WarnsWhenOneTriggerStartsAnother()
    {
        var all = new[] { S(";add", "1") };
        Assert.Equal(SnippetRules.StartsAnother, SnippetRules.Warn(";ad", all, null));
        Assert.Null(SnippetRules.Warn(";zz", all, null));
    }
}

public sealed class RendererTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 5, 14, 7, 9, TimeSpan.Zero);

    [Fact]
    public async Task FormatsDatesAndTimes()
    {
        Assert.Equal("2026-11-05 14:07", await SnippetRenderer.RenderAsync("{{date:yyyy-MM-dd}} {{time:HH:mm}}", Now));
        Assert.Equal("2026-11-05T14:07", await SnippetRenderer.RenderAsync("{{datetime:yyyy-MM-ddTHH:mm}}", Now));
    }

    [Fact]
    public async Task TimeZones()
    {
        Assert.Equal("09:07", await SnippetRenderer.RenderAsync("{{time-tz(America/New_York):HH:mm}}", Now));
    }

    [Fact]
    public async Task UnknownTagsAndBadFormatsStayAsTyped()
    {
        Assert.Equal("{{dat}} {{date-tz(Nowhere/Land):yyyy}}", await SnippetRenderer.RenderAsync("{{dat}} {{date-tz(Nowhere/Land):yyyy}}", Now));
    }

    [Fact]
    public async Task ClipboardIsReadOnlyWhenAsked()
    {
        int reads = 0;
        Task<string?> Read() { reads++; return Task.FromResult<string?>("copied"); }
        Assert.Equal("a copied b", await SnippetRenderer.RenderAsync("a {{clipboard}} b", Now, Read));
        Assert.Equal("plain", await SnippetRenderer.RenderAsync("plain", Now, Read));
        Assert.Equal(1, reads);
    }

    [Fact]
    public async Task AClipboardThatNeverAnswersTimesOut()
    {
        var never = new TaskCompletionSource<string?>();
        var timer = System.Diagnostics.Stopwatch.StartNew();
        Assert.Equal("a  b", await SnippetRenderer.RenderAsync("a {{clipboard}} b", Now, () => never.Task, TimeSpan.FromMilliseconds(80)));
        Assert.True(timer.ElapsedMilliseconds < 2000);
    }
}

public sealed class KeystrokeTests
{
    [Fact]
    public void BackspacesThenUnicodeThenTheDelimiter()
    {
        var batches = Keystrokes.Plan(2, "hé", Delimiter.Space);
        Assert.Single(batches); // the deletions and the text go in one SendInput
        var all = batches.SelectMany(b => b).ToArray();
        Assert.Equal(4 + 4 + 2, all.Length);
        Assert.All(all.Take(4), s => Assert.Equal(Keystrokes.Back, s.Vk));
        Assert.Equal('h', all[4].Unit);
        Assert.Equal('é', all[6].Unit);
        Assert.Equal(' ', all[8].Unit);
        Assert.True(all[9].Up);
    }

    [Fact]
    public void NewLinesAreShiftEnter()
    {
        var all = Keystrokes.Plan(0, "a\r\nb", null).SelectMany(b => b).ToArray();
        Assert.Equal(new ushort[] { 0, 0, Keystrokes.Shift, Keystrokes.Enter, Keystrokes.Enter, Keystrokes.Shift, 0, 0 }, all.Select(s => s.Vk).ToArray());
    }

    [Fact]
    public void LongTextGoesInChunksAndNeverSplitsAPair()
    {
        string text = new string('a', 47) + "😀" + new string('b', 100);
        var batches = Keystrokes.Plan(0, text, null);
        Assert.True(batches.Count >= 3);
        Assert.All(batches, b => Assert.True(b.Length <= (Keystrokes.ChunkChars + 1) * 2));
        foreach (var b in batches)
        {
            var units = b.Where(s => !s.Up).Select(s => s.Unit).ToArray();
            if (units.Length > 0)
                Assert.False(char.IsHighSurrogate(units[^1]));
        }
        Assert.Equal(text, string.Concat(batches.SelectMany(b => b).Where(s => !s.Up).Select(s => s.Unit)));
    }
}

public sealed class FilterTests
{
    private sealed class FakeHub : IInputHub
    {
        public bool Paused => false;

        public bool FullscreenInFront => false;

        public string? ForegroundApp { get; set; }

        public bool ForegroundElevated { get; set; }

        public event Action? PauseChanged { add { } remove { } }

        public IInputRegistration Add(IInputFilter filter) => throw new NotSupportedException();
    }

    private sealed class FakeReader : IKeyReader
    {
        public nint Window = 1;

        public bool Command;

        public nint Foreground() => Window;

        public bool CommandHeld() => Command;

        public char CharOf(uint vk, uint scan) => vk is >= 0x41 and <= 0x5A ? (char)(vk + 32) : vk == 0xBA ? ';' : vk == 0xFF ? '\0' : '?';
    }

    private readonly FakeHub _hub = new();
    private readonly FakeReader _reader = new();
    private readonly List<(SnippetMatch Match, Delimiter? After)> _expanded = [];
    private readonly List<string> _events = [];
    private readonly SnippetFilter _filter;

    public FilterTests()
    {
        var matcher = new SnippetMatcher();
        matcher.SetSnippets([
            new Snippet { Trigger = ";em", Text = "a@b.c", Expand = ExpandMode.RightAway },
            new Snippet { Trigger = ";go", Text = "going", Expand = ExpandMode.AfterDelimiter },
        ]);
        _filter = new SnippetFilter(_hub, matcher, (m, d, _) => { _expanded.Add((m, d)); _events.Add("expand"); }, _reader, () => _events.Add("cancel"));
    }

    private bool Down(uint vk, bool wasDown = false) => _filter.Key(new KeyInput(vk, 0, true, wasDown, false, false, 0));

    private void Up(uint vk) => _filter.Key(new KeyInput(vk, 0, false, false, false, false, 0));

    private void Type(string keys)
    {
        foreach (char c in keys)
        {
            uint vk = c == ';' ? 0xBAu : char.ToUpperInvariant(c);
            Assert.False(Down(vk));
            Up(vk);
        }
    }

    [Fact]
    public void EveryKeyCancelsWhatIsPendingBeforeItsOwnExpansionIsQueued()
    {
        Type(";em");
        Assert.Equal("cancel", _events[^2]);
        Assert.Equal("expand", _events[^1]);
        Down(0x41);
        Assert.Equal("cancel", _events[^1]);
    }

    [Fact]
    public void AClickCancelsWhatIsPending()
    {
        _filter.Mouse(new MouseInput(MouseKind.Down, default, 0, 0, 0, false, 0));
        Assert.Contains("cancel", _events);
    }

    [Fact]
    public void AModifierAloneAndAKeptRepeatCancelNothing()
    {
        Type(";go");
        Assert.True(Down(0x09));
        _events.Clear();
        Down(0x11);
        Assert.True(Down(0x09, wasDown: true));
        Assert.Empty(_events);
    }

    [Fact]
    public void ARightAwayTriggerExpandsAndNoKeyIsKept()
    {
        Type(";em");
        var (match, after) = Assert.Single(_expanded);
        Assert.Equal(3, match.Typed);
        Assert.Null(after);
    }

    [Fact]
    public void ASpaceDelimiterPassesThroughAndOnlyCompletesOneExpansion()
    {
        Type(";go");
        Assert.Empty(_expanded);
        Assert.False(Down(0x20));
        Assert.False(Down(0x20, wasDown: true));
        Up(0x20);
        Assert.False(Down(0x20));
        var (_, after) = Assert.Single(_expanded);
        Assert.Equal(Delimiter.Space, after);
    }

    [Fact]
    public void ASpaceStillReachesTheAppWhenTypingCancelsTheExpansion()
    {
        Type(";go");
        bool swallowed = Down(0x20);
        Up(0x20);
        Type("thanks");

        Assert.False(swallowed);
        Assert.Single(_expanded);
        Assert.Equal("cancel", _events[^1]);
    }

    [Fact]
    public void TabAndEnterCompleteToo()
    {
        Type(";go");
        Assert.True(Down(0x0D));
        Assert.Equal(Delimiter.Enter, _expanded[0].After);
    }

    [Theory]
    [InlineData(0x25u)] // an arrow
    [InlineData(0x1Bu)] // Esc
    [InlineData(0x24u)] // Home
    [InlineData(0x5Bu)] // Win
    [InlineData(0xE5u)] // an IME composing
    public void ResetKeysClearWhatWasTyped(uint vk)
    {
        Type(";e");
        Down(vk);
        Up(vk);
        Type("m");
        Assert.Empty(_expanded);
    }

    [Fact]
    public void ACommandChordResets()
    {
        Type(";e");
        _reader.Command = true;
        Down(0x43);
        _reader.Command = false;
        Type("m");
        Assert.Empty(_expanded);
    }

    [Fact]
    public void ModifiersAloneAndDeadKeysKeepTheBuffer()
    {
        Type(";e");
        Down(0xA0); // Shift
        Up(0xA0);
        Down(0xFF); // a key that types nothing
        Up(0xFF);
        Type("m");
        Assert.Single(_expanded);
    }

    [Fact]
    public void AClickOrANewWindowResets()
    {
        Type(";e");
        _filter.Mouse(new MouseInput(MouseKind.Down, MouseButton.Left, 0, 0, 0, false, 0));
        Type("m");
        Assert.Empty(_expanded);
        Type(";e");
        _reader.Window = 2;
        Type("m");
        Assert.Empty(_expanded);
    }

    [Fact]
    public void BackspaceUndoesACharacter()
    {
        Type(";ex");
        Down(0x08);
        Up(0x08);
        Type("m");
        Assert.Single(_expanded);
    }

    [Fact]
    public void ElevatedWindowsAndIgnoredAppsExpandNothing()
    {
        _hub.ForegroundElevated = true;
        Type(";em");
        _hub.ForegroundElevated = false;
        _hub.ForegroundApp = @"C:\Games\Game.exe";
        _filter.SetIgnoredApps([@"D:\x\game.EXE"]);
        Type(";em");
        Assert.Empty(_expanded);
        _filter.SetIgnoredApps([]);
        Type(";em");
        Assert.Single(_expanded);
    }
}

public sealed class ExpanderTests
{
    private sealed class RecordingSender : ISnippetSender
    {
        public readonly List<(int Backspaces, string Text, Delimiter? After)> Sent = [];

        public bool FailFirst;

        public Task<bool> SendAsync(int backspaces, string text, Delimiter? after, Func<bool>? valid = null, CancellationToken ct = default)
        {
            if (valid is not null && !valid())
                return Task.FromResult(false);
            if (FailFirst)
            {
                FailFirst = false;
                throw new InvalidOperationException("Input blocked");
            }
            Sent.Add((backspaces, text, after));
            return Task.FromResult(true);
        }
    }

    private static SnippetMatch Match(string text) => new(new Snippet { Trigger = ";em", Text = text }, 3);

    [Fact]
    public async Task DeletesTheTriggerAndTypesTheRenderedText()
    {
        var sender = new RecordingSender();
        var expander = new SnippetExpander(sender, () => Task.FromResult<string?>("COPY"), () => false, () => new DateTimeOffset(2026, 11, 5, 0, 0, 0, TimeSpan.Zero));
        expander.Enqueue(Match("on {{date:yyyy}} {{clipboard}}"), Delimiter.Tab);
        await expander.IdleAsync();
        Assert.Equal((3, "on 2026 COPY", (Delimiter?)Delimiter.Tab), Assert.Single(sender.Sent));
    }

    [Fact]
    public async Task ASpaceExpansionDeletesTheAlreadyDeliveredSpace()
    {
        var sender = new RecordingSender();
        var expander = new SnippetExpander(sender, null, () => false);
        expander.Enqueue(Match("expanded"), Delimiter.Space);
        await expander.IdleAsync();

        Assert.Equal((4, "expanded", (Delimiter?)Delimiter.Space), Assert.Single(sender.Sent));
    }

    [Fact]
    public async Task APasswordFieldDoesNotGetASecondSpace()
    {
        var sender = new RecordingSender();
        var expander = new SnippetExpander(sender, null, () => true);
        expander.Enqueue(Match("secret"), Delimiter.Space);
        await expander.IdleAsync();

        Assert.Empty(sender.Sent);
    }

    [Fact]
    public async Task MoreTypingWhileASpaceExpansionWaitsNeverDeletesTheLaterText()
    {
        var sender = new RecordingSender();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var read = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var expander = new SnippetExpander(sender, () => { entered.SetResult(); return read.Task; }, () => false);
        expander.Enqueue(Match("{{clipboard}}"), Delimiter.Space);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        expander.Cancel();
        read.SetResult("fixture");
        await expander.IdleAsync();

        Assert.Empty(sender.Sent);
    }

    [Fact]
    public async Task InputWhileTheClipboardIsBeingReadDropsTheExpansion()
    {
        var sender = new RecordingSender();
        var read = new TaskCompletionSource<string?>();
        SnippetExpander? expander = null;
        expander = new SnippetExpander(sender, () => read.Task, () => false);
        expander.Enqueue(Match("{{clipboard}}"), null);
        expander.Cancel(); // the hand typed "abc"
        read.SetResult("COPY");
        await expander.IdleAsync();
        Assert.Empty(sender.Sent);
    }

    [Fact]
    public async Task AnExpansionQueuedAfterTheCancelStillTypes()
    {
        var sender = new RecordingSender();
        var expander = new SnippetExpander(sender, null, () => false);
        expander.Cancel();
        expander.Enqueue(Match("ok"), null);
        await expander.IdleAsync();
        Assert.Equal("ok", Assert.Single(sender.Sent).Text);
    }

    [Fact]
    public async Task AnotherWindowInFrontDropsTheExpansion()
    {
        var sender = new RecordingSender();
        nint window = 1;
        var read = new TaskCompletionSource<string?>();
        var expander = new SnippetExpander(sender, () => read.Task, () => false, foreground: () => window);
        expander.Enqueue(Match("{{clipboard}}"), Delimiter.Enter);
        window = 2;
        read.SetResult("COPY");
        await expander.IdleAsync();
        Assert.Empty(sender.Sent);
    }

    [Fact]
    public async Task TheSameWindowStillTypes()
    {
        var sender = new RecordingSender();
        var expander = new SnippetExpander(sender, null, () => false, foreground: () => 1);
        expander.Enqueue(Match("ok"), null);
        await expander.IdleAsync();
        Assert.Single(sender.Sent);
    }

    [Theory]
    [InlineData(Delimiter.Tab)]
    [InlineData(Delimiter.Enter)]
    public async Task APasswordFieldGetsNothingButItsKeyBack(Delimiter delimiter)
    {
        var sender = new RecordingSender();
        var expander = new SnippetExpander(sender, null, () => true);
        expander.Enqueue(Match("secret"), null);
        expander.Enqueue(Match("secret"), delimiter);
        await expander.IdleAsync();
        Assert.Equal((0, "", (Delimiter?)delimiter), Assert.Single(sender.Sent));
    }

    [Fact]
    public async Task ExpansionsGoInOrderAndOneFailureDoesNotStopTheNext()
    {
        var sender = new RecordingSender { FailFirst = true };
        var expander = new SnippetExpander(sender, null, () => false);
        expander.Enqueue(Match("one"), null);
        expander.Enqueue(Match("two"), null);
        await expander.IdleAsync();
        Assert.Equal("two", Assert.Single(sender.Sent).Text);
    }

    [Fact]
    public async Task AnEditAfterTheTriggerDoesNotChangeTheExpansion()
    {
        var sender = new RecordingSender();
        var expander = new SnippetExpander(sender, null, () => false);
        var original = new Snippet { Trigger = ";em", Text = "old" };
        var match = new SnippetMatch(original, 3);
        _ = original with { Text = "new" };
        expander.Enqueue(match, null);
        await expander.IdleAsync();
        Assert.Equal("old", sender.Sent[0].Text);
    }
}
