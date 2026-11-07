// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.IO;
using Chippi.Capture;
using Chippi.Capture.Tests;
using Chippi.Media;
using Chippi.UI.Capture;
using CaptureMode = Chippi.Capture.CaptureMode;

namespace Chippi.Features.Recording.Tests;

/// <summary>
/// A recording from the starter to the saved file (screen-recording.md, Behavior), over a fake capture layer, fake
/// sources and encoder, and a clock the test moves.
/// </summary>
public sealed class RecordingControllerTests : IDisposable
{
    private readonly ManualTime _time = new();
    private readonly FakeLayer _layer = new();
    private readonly FakePc _pc = new();
    private readonly FakeShell _shell = new();
    private RecordingSettings _settings = new();
    private readonly RecordingController _r;

    public RecordingControllerTests() => _r = new RecordingController(_layer, _pc, _shell, () => _settings, _time);

    public void Dispose()
    {
        _r.Dispose();
        _pc.Dispose();
    }

    private FakeSession Session => _layer.Session!;

    private async Task RecordAsync(RecordingSource source = RecordingSource.Region, PixelRect? rect = null, int display = 0, bool gif = false,
        Action? beforeStart = null)
    {
        Assert.True(await _r.BeginAsync(source, gif, CaptureDoor.Shortcut));
        Session.Select(rect ?? new PixelRect(100, 100, 640, 360), display);
        beforeStart?.Invoke();
        _r.Start();
        _time.Advance(TimeSpan.FromSeconds(RecordingController.CountSeconds));
    }

    /// <summary>Frames at 30 fps for <paramref name="seconds"/>, each stamped with the clock.</summary>
    private void Run(double seconds)
    {
        long start = _time.GetTimestamp();
        int frames = (int)(seconds * 30);
        for (int i = 1; i <= frames; i++)
        {
            _time.Advance(TimeSpan.FromTicks(start + (long)(seconds * TimeSpan.TicksPerSecond) * i / frames - _time.GetTimestamp()));
            _pc.Video!.Send(TimeSpan.FromTicks(_time.GetTimestamp()));
        }
    }

    [Fact]
    public async Task A_region_records_its_part_of_one_display_after_a_three_second_count()
    {
        Assert.True(await _r.BeginAsync(RecordingSource.Region, gif: false, CaptureDoor.Island));
        var request = Assert.Single(_layer.Requests);
        Assert.Equal(CaptureMode.Region, request.Mode);
        Assert.True(request.OneDisplay);
        Assert.Equal(CaptureDoor.Island, request.Door);
        Assert.Equal(RecordingPhase.Choosing, _r.Phase);

        int bars = 0;
        _r.BarsWanted += _ => bars++;
        Session.Select(new PixelRect(100, 100, 640, 360));
        Assert.Equal(RecordingPhase.Ready, _r.Phase);
        Assert.Equal(1, bars);
        Assert.Null(_pc.DisplayAsked); // nothing records until Start

        _r.Start();
        Assert.Equal(RecordingPhase.Counting, _r.Phase);
        Assert.Equal(3, _r.CountLeft);
        _time.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(2, _r.CountLeft);
        Assert.False(Session.Closed);
        _time.Advance(TimeSpan.FromSeconds(2));

        Assert.Equal(RecordingPhase.Recording, _r.Phase);
        Assert.Equal(CaptureEnd.Finished, Session.End);
        Assert.Equal((nint)1, _pc.DisplayAsked!.Value.Monitor);
        Assert.Equal(new Chippi.Media.Capture.PixelRect(100, 100, 640, 360), _pc.DisplayAsked.Value.Region);
        Assert.True(_pc.DisplayAsked.Value.Cursor);
        Assert.Empty(_pc.Audio);
        Assert.Equal([true], _shell.Recording);
        var (target, overlay) = Assert.Single(_pc.Overlays);
        Assert.Equal(new PixelRect(100, 100, 640, 360), target.Rect);
        Assert.False(overlay.Disposed);
        Assert.Equal(("0:00", "Recording · Region"), _shell.Live);
    }

    [Fact]
    public async Task A_region_on_a_display_left_of_the_main_one_is_cut_in_that_display_s_pixels()
    {
        await RecordAsync(rect: new PixelRect(-1800, 400, 200, 100), display: 1);
        Assert.Equal((nint)2, _pc.DisplayAsked!.Value.Monitor);
        Assert.Equal(new Chippi.Media.Capture.PixelRect(120, 40, 200, 100), _pc.DisplayAsked.Value.Region);
        Assert.Equal(144, _pc.Overlays[0].Target.Dpi);
    }

    [Fact]
    public async Task Record_screen_takes_the_whole_display()
    {
        Assert.True(await _r.BeginAsync(RecordingSource.Screen, false, CaptureDoor.Shortcut));
        Assert.Equal(CaptureMode.Screen, _layer.Requests[0].Mode);
        Session.Select(new PixelRect(0, 0, 3440, 1440));
        _r.Start();
        _time.Advance(TimeSpan.FromSeconds(3));
        Assert.Null(_pc.DisplayAsked!.Value.Region);
        Assert.Equal(("0:00", "Recording · Screen"), _shell.Live);
    }

    [Fact]
    public async Task Sound_and_mic_go_in_only_when_switched_on_for_that_recording()
    {
        await RecordAsync(beforeStart: () =>
        {
            Assert.False(_r.SystemSound);
            Assert.False(_r.Mic);
            _r.SetSystemSound(true);
            _r.SetMic(true);
        });
        Assert.Equal(["system", "mic"], _pc.Audio.Select(a => a.Name));
        Assert.True(_pc.Writer.Format!.HasAudio);
        Run(1);
        await _r.StopAsync();
        _r.Close();

        // The next recording starts with both off again.
        await RecordAsync();
        Assert.Equal(2, _pc.Audio.Count);
        Assert.False(_pc.Writer.Format!.HasAudio);
    }

    [Fact]
    public async Task Cancel_during_the_count_records_nothing()
    {
        Assert.True(await _r.BeginAsync(RecordingSource.Region, false, CaptureDoor.Shortcut));
        Session.Select(new PixelRect(100, 100, 640, 360));
        _r.Start();
        _time.Advance(TimeSpan.FromSeconds(1));
        _r.Cancel();
        _time.Advance(TimeSpan.FromSeconds(5));
        Assert.Equal(RecordingPhase.Idle, _r.Phase);
        Assert.Equal(CaptureEnd.Cancelled, Session.End);
        Assert.Null(_pc.DisplayAsked);
        Assert.Empty(_pc.Temps);
    }

    [Fact]
    public async Task The_overlay_closing_before_Start_ends_it()
    {
        Assert.True(await _r.BeginAsync(RecordingSource.Region, false, CaptureDoor.Shortcut));
        Session.Select(new PixelRect(100, 100, 640, 360));
        Session.Close(CaptureEnd.Interrupted); // a lock, a UAC prompt
        Assert.Equal(RecordingPhase.Idle, _r.Phase);
        _r.Start();
        Assert.Equal(RecordingPhase.Idle, _r.Phase);
    }

    [Fact]
    public async Task A_second_starter_says_Already_recording_and_does_nothing_else()
    {
        await RecordAsync();
        Assert.False(await _r.BeginAsync(RecordingSource.Window, false, CaptureDoor.Shortcut));
        Assert.Single(_layer.Requests);
        Assert.Equal(("Already recording", "Region", false), _shell.Lines[^1]);
        Assert.Equal(RecordingPhase.Recording, _r.Phase);
    }

    [Fact]
    public async Task Stop_with_nothing_recording_says_Not_recording()
    {
        await _r.StopAsync();
        Assert.Equal(("Not recording", "Recording", false), Assert.Single(_shell.Lines));
        Assert.Equal(RecordingPhase.Idle, _r.Phase);
    }

    [Fact]
    public async Task Pause_holds_the_clock_and_the_video_has_no_gap()
    {
        await RecordAsync();
        Run(2);
        _r.Pause();
        Assert.Equal(RecordingPhase.Paused, _r.Phase);
        Assert.Equal("Paused · Region", _shell.Live.Source);
        var held = _r.Elapsed;
        _time.Advance(TimeSpan.FromSeconds(10));
        Assert.Equal(held, _r.Elapsed);
        _r.Resume();
        Assert.Equal("Recording · Region", _shell.Live.Source);
        Run(1);
        await _r.StopAsync();
        Assert.InRange(_r.Duration.TotalSeconds, 2.9, 3.1);
    }

    [Fact]
    public async Task Stop_finishes_the_file_and_opens_the_page_without_saving()
    {
        await RecordAsync();
        Run(5);
        await _r.StopAsync();

        Assert.Equal(RecordingPhase.Review, _r.Phase);
        Assert.Equal(1, _shell.PagesOpened);
        Assert.True(_pc.Overlays[0].Overlay.Disposed);
        Assert.Equal([true, false], _shell.Recording);
        Assert.Equal((null, null), _shell.Live);
        Assert.True(File.Exists(_r.File));
        Assert.Equal(TrimRange.Whole(_r.Duration), _r.Trim);
        Assert.Empty(_shell.Recent);
        Assert.False(Directory.Exists(_pc.DefaultSaveFolder));

        Assert.True(await _r.SaveAsync());
        var saved = Assert.Single(Directory.GetFiles(_pc.DefaultSaveFolder));
        Assert.Equal(".mp4", Path.GetExtension(saved));
        Assert.StartsWith("Recording ", Path.GetFileName(saved));
        Assert.Equal(saved, _r.SavedTo);
        Assert.Equal(saved, Assert.Single(_shell.Recent).File);
        Assert.Empty(_pc.Work); // the whole clip is copied, not re-encoded

        string temp = _r.File!;
        _r.Close();
        Assert.False(File.Exists(temp));
        Assert.True(File.Exists(saved));
        Assert.Equal(RecordingPhase.Idle, _r.Phase);
    }

    [Fact]
    public async Task Stop_over_a_fullscreen_app_saves_adds_to_Recent_and_says_so()
    {
        _shell.CanOpenPage = false;
        await RecordAsync();
        Run(5);
        await _r.StopAsync();

        var saved = Assert.Single(Directory.GetFiles(_pc.DefaultSaveFolder));
        Assert.Equal(saved, Assert.Single(_shell.Recent).File);
        Assert.Equal(("Recording saved", "0:05 · Region", false), _shell.Lines[^1]);
        // The page still has it, for Trim, Export GIF and Discard.
        Assert.Equal(RecordingPhase.Review, _r.Phase);
    }

    [Fact]
    public async Task A_trimmed_save_re_encodes_only_the_span()
    {
        _settings = _settings with { SaveTo = Path.Combine(_pc.Folder, "Mine") };
        await RecordAsync();
        Run(10);
        await _r.StopAsync();
        _r.SetTrim(new TrimRange(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(6)));
        Assert.True(await _r.SaveAsync());
        var (kind, target, span) = Assert.Single(_pc.Work);
        Assert.Equal("trim", kind);
        Assert.StartsWith(Path.Combine(_pc.Folder, "Mine"), target);
        Assert.Equal(TimeSpan.FromSeconds(4), span.Length);
        Assert.Equal(TimeSpan.FromSeconds(4), _shell.Recent[^1].Length);
    }

    [Fact]
    public async Task Record_GIF_saves_a_GIF_and_says_when_the_limit_cuts_it()
    {
        Assert.True(await _r.BeginAsync(RecordingSource.Region, gif: true, CaptureDoor.Shortcut));
        Assert.Equal(RecordingFormat.Gif, _r.Format);
        Session.Select(new PixelRect(100, 100, 640, 360));
        _r.Start();
        _time.Advance(TimeSpan.FromSeconds(3));
        Run(70);
        await _r.StopAsync();
        Assert.True(_r.GifCut);
        Assert.True(await _r.SaveAsync());
        Assert.Equal("gif", _pc.Work[^1].Kind);
        Assert.EndsWith(".gif", _pc.Work[^1].Target);
        Assert.Equal(TimeSpan.FromSeconds(60), _shell.Recent[^1].Length);
    }

    [Fact]
    public async Task Discard_while_recording_deletes_only_the_temporary_file()
    {
        await RecordAsync();
        Run(2);
        string temp = Assert.Single(_pc.Temps);
        Assert.True(File.Exists(temp));
        await _r.DiscardAsync();
        Assert.False(File.Exists(temp));
        Assert.Equal(RecordingPhase.Idle, _r.Phase);
        Assert.True(_pc.Overlays[0].Overlay.Disposed);
        Assert.Equal([true, false], _shell.Recording);
        Assert.Empty(_shell.Recent);
    }

    [Fact]
    public async Task Discard_on_the_page_keeps_a_copy_Save_already_wrote()
    {
        await RecordAsync();
        Run(2);
        await _r.StopAsync();
        await _r.SaveAsync();
        string temp = _r.File!, saved = _r.SavedTo!;
        await _r.DiscardAsync();
        Assert.False(File.Exists(temp));
        Assert.True(File.Exists(saved));
    }

    [Fact]
    public async Task A_window_Windows_refuses_says_Can_t_record_this_window()
    {
        _pc.RefuseWindow = true;
        Assert.True(await _r.BeginAsync(RecordingSource.Window, false, CaptureDoor.Shortcut));
        Assert.Equal(CaptureMode.Window, _layer.Requests[0].Mode);
        Session.Select(new PixelRect(100, 100, 800, 600), window: new FrozenWindow(77, "Locked", @"C:\x.exe", new PixelRect(100, 100, 800, 600)));
        _r.Start();
        _time.Advance(TimeSpan.FromSeconds(3));
        Assert.Equal((nint)77, _pc.WindowAsked);
        Assert.Equal(("Can't record this window", "Window", true), _shell.Lines[^1]);
        Assert.Equal(RecordingPhase.Idle, _r.Phase);
        Assert.NotNull(_r.LastError);
    }

    [Fact]
    public async Task A_window_closing_stops_the_recording_and_keeps_it()
    {
        _shell.CanOpenPage = false;
        Assert.True(await _r.BeginAsync(RecordingSource.Window, false, CaptureDoor.Shortcut));
        Session.Select(new PixelRect(100, 100, 800, 600), window: new FrozenWindow(78, "Notes", @"C:\n.exe", new PixelRect(100, 100, 800, 600)));
        _r.Start();
        _time.Advance(TimeSpan.FromSeconds(3));
        Run(3);
        _pc.Video!.End("The window closed.");
        await Task.Delay(50); // the recorder finishes the file off the source's thread

        Assert.Contains(("Recording stopped", "0:03 · Window", true), _shell.Lines);
        Assert.Single(Directory.GetFiles(_pc.DefaultSaveFolder));
        Assert.Single(_shell.Recent);
        Assert.Equal([true, false], _shell.Recording);
    }

    [Fact]
    public async Task A_PC_without_an_encoder_says_Recording_failed_before_the_selector()
    {
        _pc.Missing = "No H.264 encoder (install the Media Feature Pack).";
        Assert.False(await _r.BeginAsync(RecordingSource.Region, false, CaptureDoor.Shortcut));
        Assert.Empty(_layer.Requests);
        Assert.Equal(("Recording failed", "Region", true), Assert.Single(_shell.Lines));
        Assert.Equal(_pc.Missing, _r.LastError);
    }

    [Fact]
    public async Task A_new_recording_saves_one_left_unsaved_on_the_page()
    {
        await RecordAsync();
        Run(2);
        await _r.StopAsync();
        string first = _r.File!;
        Assert.True(await _r.BeginAsync(RecordingSource.Region, false, CaptureDoor.Shortcut));
        Assert.False(File.Exists(first));
        Assert.Single(Directory.GetFiles(_pc.DefaultSaveFolder));
        Assert.Equal(RecordingPhase.Choosing, _r.Phase);
    }

    [Fact]
    public async Task Quit_asks_only_while_a_recording_runs_and_Stop_saves_it()
    {
        Assert.Null(_r.QuitQuestion);
        Assert.True(await _r.BeginAsync(RecordingSource.Region, false, CaptureDoor.Shortcut));
        Session.Select(new PixelRect(100, 100, 640, 360));
        Assert.Null(_r.QuitQuestion); // nothing recording yet
        _r.Start();
        Assert.Equal("Stop recording and quit?", _r.QuitQuestion);
        _time.Advance(TimeSpan.FromSeconds(3));
        Run(2);
        _r.Pause();
        Assert.Equal("Stop recording and quit?", _r.QuitQuestion);

        // Stop: the feature stops as Chippi quits, and the file is finished and saved.
        await _r.ShutDownAsync();
        var saved = Assert.Single(Directory.GetFiles(_pc.DefaultSaveFolder));
        Assert.Equal(".mp4", Path.GetExtension(saved));
        Assert.All(_pc.Temps, t => Assert.False(File.Exists(t)));
        Assert.Null(_r.QuitQuestion);
    }

    [Theory]
    [InlineData(0, "0:00")]
    [InlineData(42, "0:42")]
    [InlineData(605, "10:05")]
    [InlineData(3725, "1:02:05")]
    public void The_clock_reads_like_the_bar(int seconds, string text) =>
        Assert.Equal(text, RecordingController.Clock(TimeSpan.FromSeconds(seconds)));
}
