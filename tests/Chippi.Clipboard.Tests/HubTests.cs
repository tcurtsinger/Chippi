// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Collections.Concurrent;
using Chippi.Core.Clipboard;

namespace Chippi.Clipboard.Tests;

public sealed class HubTests : IDisposable
{
    private readonly FakeClipboard _clipboard = new();
    private readonly FakeForeground _foreground = new();
    private readonly ManualTime _time = new();
    private readonly ClipboardHub _hub;
    private readonly ConcurrentQueue<ClipCopy> _copies = new();

    public HubTests() => _hub = new ClipboardHub(_clipboard, _foreground, _time);

    public void Dispose() => _hub.Dispose();

    private static readonly byte[] Zero = ClipCodec.Dword(0), One = ClipCodec.Dword(1);

    private async Task SettleAsync()
    {
        _time.Advance(ClipboardHub.Settle);
        await _hub.Processing;
    }

    private IDisposable Listen(Func<string, bool>? skip = null) => _hub.Listen(_copies.Enqueue, skip);

    // ---- Listening ----

    [Fact]
    public async Task Nothing_listens_without_a_lease_and_the_last_lease_stops_it()
    {
        Assert.False(_clipboard.Listening);
        var first = Listen();
        var second = Listen();
        Assert.True(_clipboard.Listening);
        first.Dispose();
        Assert.True(_clipboard.Listening);
        second.Dispose();
        Assert.False(_clipboard.Listening);
        _clipboard.CopyText("after");
        await SettleAsync();
        Assert.Empty(_copies);
    }

    [Fact]
    public async Task A_copy_is_read_once_it_settles()
    {
        using var lease = Listen();
        _clipboard.CopyText("first");
        _time.Advance(ClipboardHub.Settle / 2);
        _clipboard.CopyText("second");
        await SettleAsync();
        var copy = Assert.Single(_copies);
        Assert.Equal("second", copy.Content!.Text);
        Assert.Equal(_clipboard.Sequence, copy.Sequence);
        Assert.Equal(ClipPrivacy.None, copy.Privacy);
    }

    [Theory]
    [InlineData(ClipFormat.Exclude)]
    [InlineData(ClipFormat.ViewerIgnore)]
    public async Task A_private_copy_is_never_read(string marker)
    {
        using var lease = Listen();
        _clipboard.CopyText("hunter2", extra: (marker, Zero));
        await SettleAsync();
        Assert.Empty(_copies);
        Assert.DoesNotContain(ClipFormat.UnicodeText, _clipboard.Reads);
    }

    [Fact]
    public async Task A_private_copy_that_lands_after_the_early_check_is_still_never_read()
    {
        using var lease = Listen();
        _clipboard.CopyText("ordinary");
        // A password manager replaces the copy between the update and Chippi opening the clipboard.
        _clipboard.OnOpen = () => { _clipboard.OnOpen = null; _clipboard.CopyText("hunter2", extra: (ClipFormat.Exclude, Zero)); };
        _time.Advance(ClipboardHub.Settle);
        await _hub.Processing;
        await SettleAsync();
        Assert.Empty(_copies);
        Assert.DoesNotContain(ClipFormat.UnicodeText, _clipboard.Reads);
    }

    [Fact]
    public async Task A_copy_that_changes_during_a_delayed_render_is_dropped()
    {
        using var lease = Listen();
        // The source renders on request and, while doing so, the copy moves on.
        _clipboard.Copy(@"C:\Apps\Slow.exe");
        await _clipboard.TransactAsync(s =>
        {
            s.SetDelayed(ClipFormat.UnicodeText, () =>
            {
                _clipboard.CopyText("newer", extra: (ClipFormat.Exclude, Zero));
                return ClipCodec.EncodeText("older");
            });
            return true;
        });
        await SettleAsync();
        Assert.DoesNotContain(_copies, c => c.Content?.Text is "older" or "newer");
    }

    [Theory]
    [InlineData(new byte[] { 0, 0, 0, 0 }, ClipPrivacy.NoHistory)]
    [InlineData(new byte[] { 1, 0 }, ClipPrivacy.NoHistory)]
    [InlineData(new byte[] { 1, 0, 0, 0 }, ClipPrivacy.None)]
    public async Task The_history_marker_is_read_as_a_dword_and_fails_closed(byte[] value, ClipPrivacy expected)
    {
        using var lease = Listen();
        _clipboard.CopyText("text", extra: (ClipFormat.History, value));
        await SettleAsync();
        Assert.Equal(expected, Assert.Single(_copies).Privacy);
    }

    [Fact]
    public async Task An_allowed_history_marker_never_overrides_an_exclusion()
    {
        using var lease = Listen();
        _clipboard.CopyText("hunter2", extra: [(ClipFormat.History, One), (ClipFormat.Exclude, Zero)]);
        await SettleAsync();
        Assert.Empty(_copies);
    }

    [Fact]
    public async Task Chippis_own_writes_are_not_delivered()
    {
        using var lease = Listen();
        Assert.Equal(ClipStatus.Ok, await _hub.WriteAsync(ClipContent.FromText("from History")));
        await SettleAsync();
        Assert.Empty(_copies);
    }

    [Fact]
    public async Task An_app_to_skip_is_not_read_for_that_lease_only()
    {
        var history = new ConcurrentQueue<ClipCopy>();
        using var skipping = _hub.Listen(history.Enqueue, path => path.EndsWith("Vault.exe", StringComparison.OrdinalIgnoreCase));
        _clipboard.CopyText("secret", owner: @"C:\Apps\Vault.exe");
        await SettleAsync();
        Assert.Empty(history);
        Assert.DoesNotContain(ClipFormat.UnicodeText, _clipboard.Reads);

        using var other = Listen();
        _clipboard.CopyText("again", owner: @"C:\Apps\Vault.exe");
        await SettleAsync();
        Assert.Empty(history);
        Assert.Equal("again", Assert.Single(_copies).Content!.Text);
    }

    // ---- What is read ----

    [Fact]
    public async Task Files_win_over_a_picture_and_text()
    {
        using var lease = Listen();
        _clipboard.Copy(@"C:\Windows\explorer.exe",
            (ClipFormat.UnicodeText, ClipCodec.EncodeText("a.txt")),
            (ClipFormat.Dib, ClipCodec.EncodeDib(Picture(2, 2))),
            (ClipFormat.Files, ClipCodec.EncodeFiles([@"C:\a.txt", @"C:\b.txt"])));
        await SettleAsync();
        Assert.Equal([@"C:\a.txt", @"C:\b.txt"], Assert.Single(_copies).Content!.Files);
    }

    [Fact]
    public async Task A_picture_wins_over_its_text()
    {
        using var lease = Listen();
        _clipboard.Copy(null, (ClipFormat.UnicodeText, ClipCodec.EncodeText("alt")), (ClipFormat.Dib, ClipCodec.EncodeDib(Picture(3, 2))));
        await SettleAsync();
        var image = Assert.Single(_copies).Content!.Image!;
        Assert.Equal((3, 2), (image.Width, image.Height));
    }

    [Fact]
    public async Task Limits_are_reported_without_content()
    {
        using var lease = Listen();
        _clipboard.Copy(null, (ClipFormat.Files, ClipCodec.EncodeFiles([.. Enumerable.Range(0, 101).Select(i => $@"C:\f{i}")])));
        await SettleAsync();
        // 4,100 x 4,100 is over 16.6 MP; only the header is checked before the pixels.
        var header = ClipCodec.EncodeDib(Picture(1, 1));
        BitConverter.GetBytes(4100).CopyTo(header, 4);
        BitConverter.GetBytes(4100).CopyTo(header, 8);
        _clipboard.Copy(null, (ClipFormat.Dib, header));
        await SettleAsync();
        _clipboard.CopyText(new string('中', 400_000));
        await SettleAsync();
        Assert.Equal([ClipLimit.Files, ClipLimit.Image, ClipLimit.Text], _copies.Select(c => c.Limit));
        Assert.All(_copies, c => Assert.Null(c.Content));
    }

    [Fact]
    public async Task The_text_cap_counts_utf8_not_utf16()
    {
        using var lease = Listen();
        _clipboard.CopyText(new string('a', 600_000));
        await SettleAsync();
        Assert.Equal(600_000, Assert.Single(_copies).Content!.Text!.Length);
    }

    [Fact]
    public async Task A_busy_clipboard_is_reported_on_demand()
    {
        _clipboard.CopyText("text");
        _clipboard.BusyOpens = int.MaxValue;
        Assert.Equal(ClipStatus.Busy, (await _hub.ReadAsync()).Status);
        Assert.Equal(ClipStatus.Busy, await _hub.WriteAsync(ClipContent.FromText("x")));
    }

    [Fact]
    public async Task On_demand_reads_never_start_the_listener()
    {
        _clipboard.CopyText("https://example.org/?utm_source=a");
        var read = await _hub.ReadAsync();
        Assert.Equal("https://example.org/?utm_source=a", read.Content!.Text);
        Assert.False(_clipboard.Listening);
        _clipboard.CopyText("secret", extra: (ClipFormat.Exclude, Zero));
        Assert.Equal(ClipStatus.Private, (await _hub.ReadAsync()).Status);
    }

    [Fact]
    public async Task Writing_files_asks_for_a_copy_not_a_move()
    {
        await _hub.WriteAsync(ClipContent.FromFiles([@"C:\a.txt"]));
        var data = _clipboard.Snapshot();
        Assert.Equal(ClipCodec.CopyEffect, data[ClipFormat.DropEffect]);
        Assert.Equal([@"C:\a.txt"], ClipCodec.DecodeFiles(data[ClipFormat.Files]));
    }

    // ---- Pasting ----

    private void CopyRich(string text, params (string, byte[])[] extra) =>
        _clipboard.Copy(@"C:\Apps\Word.exe", [(ClipFormat.UnicodeText, ClipCodec.EncodeText(text)), (ClipFormat.Html, [1, 2, 3]), .. extra]);

    private async Task<PasteOutcome> PasteAndFinishAsync(Task<PasteOutcome> paste, TimeSpan wait)
    {
        _time.Advance(wait);
        return await paste;
    }

    [Fact]
    public async Task Plain_paste_sends_marked_text_then_puts_the_rich_copy_back()
    {
        CopyRich("Rich\tline\r\nnext");
        string? pasted = null;
        bool markedWhileRead = false;
        _foreground.OnPaste = () => { pasted = _clipboard.Text; markedWhileRead = _clipboard.IsAvailable(ClipFormat.Exclude); };
        var outcome = await PasteAndFinishAsync(_hub.PastePlainTextAsync(), ClipboardHub.RestoreAfterRequest);
        Assert.Equal(PasteOutcome.Sent, outcome);
        Assert.Equal("Rich\tline\r\nnext", pasted);
        Assert.True(markedWhileRead);
        var restored = _clipboard.Snapshot();
        Assert.Equal([1, 2, 3], restored[ClipFormat.Html]);
        Assert.False(restored.ContainsKey(ClipFormat.Exclude));
    }

    [Fact]
    public async Task Without_a_data_request_the_restore_waits_the_full_time()
    {
        CopyRich("text");
        var paste = _hub.PastePlainTextAsync();
        _time.Advance(ClipboardHub.RestoreWithoutRequest - TimeSpan.FromMilliseconds(1));
        Assert.False(paste.IsCompleted);
        _time.Advance(TimeSpan.FromMilliseconds(1));
        Assert.Equal(PasteOutcome.Sent, await paste);
        Assert.True(_clipboard.Snapshot().ContainsKey(ClipFormat.Html));
    }

    [Fact]
    public async Task A_newer_copy_is_never_overwritten_by_the_restore()
    {
        CopyRich("old");
        _foreground.OnPaste = () => { _ = _clipboard.Text; _clipboard.CopyText("newer"); };
        await PasteAndFinishAsync(_hub.PastePlainTextAsync(), ClipboardHub.RestoreAfterRequest);
        Assert.Equal("newer", _clipboard.Text);
    }

    [Fact]
    public async Task An_elevated_target_gets_no_keys_and_the_plain_text_becomes_the_copy()
    {
        CopyRich("for admin");
        _foreground.CanSend = false;
        Assert.Equal(PasteOutcome.Copied, await _hub.PastePlainTextAsync());
        Assert.Equal(0, _foreground.Pastes);
        Assert.Equal("for admin", _clipboard.Text);
        Assert.False(_clipboard.IsAvailable(ClipFormat.Html));
        Assert.False(_clipboard.IsAvailable(ClipFormat.Exclude));
    }

    [Fact]
    public async Task A_private_copy_pastes_and_stays_private_throughout()
    {
        CopyRich("hunter2", (ClipFormat.Exclude, Zero));
        bool marked = false;
        _foreground.OnPaste = () => { _ = _clipboard.Text; marked = _clipboard.IsAvailable(ClipFormat.Exclude); };
        Assert.Equal(PasteOutcome.Sent, await PasteAndFinishAsync(_hub.PastePlainTextAsync(), ClipboardHub.RestoreAfterRequest));
        Assert.True(marked);
        Assert.True(_clipboard.IsAvailable(ClipFormat.Exclude));

        _foreground.CanSend = false;
        Assert.Equal(PasteOutcome.Copied, await _hub.PastePlainTextAsync());
        Assert.True(_clipboard.IsAvailable(ClipFormat.Exclude));
    }

    [Fact]
    public async Task Pictures_or_files_only_leave_the_clipboard_alone()
    {
        _clipboard.Copy(null, (ClipFormat.Files, ClipCodec.EncodeFiles([@"C:\a.png"])));
        uint before = _clipboard.Sequence;
        Assert.Equal(PasteOutcome.NoText, await _hub.PastePlainTextAsync());
        Assert.Equal(before, _clipboard.Sequence);
        Assert.Equal(0, _foreground.Pastes);
    }

    [Fact]
    public async Task A_busy_clipboard_fails_the_paste()
    {
        CopyRich("text");
        _clipboard.BusyOpens = int.MaxValue;
        Assert.Equal(PasteOutcome.Busy, await _hub.PastePlainTextAsync());
    }

    [Fact]
    public async Task A_target_that_wont_come_forward_keeps_the_content_as_an_ordinary_copy()
    {
        CopyRich("rich");
        var target = _hub.CaptureTarget()!;
        _foreground.ComesToFront = false;
        Assert.Equal(PasteOutcome.Copied, await _hub.PasteAsync(ClipContent.FromText("from History"), target));
        Assert.Equal("from History", _clipboard.Text);
        Assert.False(_clipboard.IsAvailable(ClipFormat.Exclude));
        Assert.Equal(0, _foreground.Pastes);
    }

    [Fact]
    public async Task Pasting_a_history_entry_restores_the_copy_before_it()
    {
        CopyRich("current");
        var target = _hub.CaptureTarget()!;
        string? pasted = null;
        _foreground.OnPaste = () => pasted = _clipboard.Text;
        var paste = _hub.PasteAsync(ClipContent.FromText("older entry"), target);
        Assert.Equal(PasteOutcome.Sent, await PasteAndFinishAsync(paste, ClipboardHub.RestoreAfterRequest));
        Assert.Equal("older entry", pasted);
        Assert.Equal("current", _clipboard.Text);
    }

    [Fact]
    public async Task The_temporary_copy_and_the_restore_are_not_recorded()
    {
        using var lease = Listen();
        CopyRich("rich");
        await SettleAsync();
        _copies.Clear();
        _foreground.OnPaste = () => _ = _clipboard.Text;
        await PasteAndFinishAsync(_hub.PastePlainTextAsync(), ClipboardHub.RestoreAfterRequest);
        await SettleAsync();
        Assert.Empty(_copies);
    }

    private static ClipImage Picture(int width, int height) =>
        new(width, height, [.. Enumerable.Range(0, width * height).SelectMany(i => new byte[] { (byte)i, 20, 30, 255 })]);
}
