// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows.Media;
using Chippi.Core.Clipboard;
using Chippi.Core.Island;
using Chippi.Core.Modules;
using Chippi.Features.CommandBar;
using Chippi.Features.CommandBar.Search;
using Chippi.Features.RadialMenu.Input;
using Chippi.Features.RadialMenu.Pc;
using Chippi.Features.RadialMenu.Slices;
using Chippi.Features.RadialMenu.Wheel;
using Chippi.Features.Sound.Mute;
using Chippi.Island.Tests;
using Chippi.UI.Pet;
using Microsoft.Extensions.Logging.Abstractions;

namespace Chippi.Features.RadialMenu.Tests;

/// <summary>Each slice on a fake PC, and the module end to end: opening, picking, failing, Cleaning mode, the shortcut, Settings' edits.</summary>
public sealed class RadialMenuTests
{
    // ----- Running each kind (What a slice can do) -----

    [Fact]
    public async Task Each_kind_acts_through_the_pc()
    {
        var f = new Fixture();
        var runner = f.Runner();
        await runner.RunAsync(new Slice { Kind = SliceKind.App, Value = "Spotify" }, 0);
        await runner.RunAsync(new Slice { Kind = SliceKind.App, Value = @"C:\Tools\tool.exe" }, 0);
        await runner.RunAsync(new Slice { Kind = SliceKind.File, Value = @"C:\Users\Travis\Downloads" }, 0);
        await runner.RunAsync(new Slice { Kind = SliceKind.Link, Value = "github.com/tcurtsinger/Chippi" }, 0);
        await runner.RunAsync(new Slice { Kind = SliceKind.SettingsPage, Value = "ms-settings:bluetooth" }, 0);
        Assert.Equal([@"shell:AppsFolder\Spotify", @"C:\Tools\tool.exe", @"C:\Users\Travis\Downloads", "https://github.com/tcurtsinger/Chippi", "ms-settings:bluetooth"], f.Pc.Opened);

        await runner.RunAsync(new Slice { Kind = SliceKind.Keys, Value = "Win+Shift+S" }, 0);
        await runner.RunAsync(new Slice { Kind = SliceKind.Text, Value = "Thanks,\nTravis" }, 0);
        await runner.RunAsync(new Slice { Kind = SliceKind.Command, Value = "ipconfig /flushdns", Shell = CommandShell.Cmd }, 0);
        await runner.RunAsync(new Slice { Kind = SliceKind.Media, Value = "Next" }, 0);
        await runner.RunAsync(new Slice { Kind = SliceKind.Window, Value = "SnapLeft" }, 42);
        await runner.RunAsync(new Slice { Kind = SliceKind.Chippi, Value = "keep-awake/toggle" }, 0);
        Assert.Equal([new KeyCombo(KeyModifiers.Win | KeyModifiers.Shift, 0x53)], f.Pc.Pressed);
        Assert.Equal(["Thanks,\nTravis"], f.Pc.Typed);
        Assert.Equal([(CommandShell.Cmd, "ipconfig /flushdns")], f.Pc.Ran);
        Assert.Equal([MediaAction.Next], f.Pc.Played);
        Assert.Equal([((nint)42, WindowAction.SnapLeft)], f.Pc.Windowed);
        Assert.Equal(["keep-awake/toggle"], f.RanCommands);
    }

    [Fact]
    public async Task Selected_text_is_searched_opened_or_copied()
    {
        var f = new Fixture();
        var runner = f.Runner();
        f.Hub.Selection = "Ashen Gate";
        await runner.RunAsync(new Slice { Kind = SliceKind.Selection, Value = "Search" }, 0);
        Assert.Equal("https://duckduckgo.com/?q=Ashen%20Gate", f.Pc.Opened[^1]);
        await runner.RunAsync(new Slice { Kind = SliceKind.Selection, Value = "CopyPlain" }, 0);
        Assert.Equal(["Ashen Gate"], f.Hub.Written);
        var notALink = await Assert.ThrowsAsync<SliceFailure>(() => runner.RunAsync(new Slice { Kind = SliceKind.Selection, Value = "OpenLink" }, 0));
        Assert.Equal("Not a link", notALink.Message);
        f.Hub.Selection = "github.com/tcurtsinger/Chippi/pull/183";
        await runner.RunAsync(new Slice { Kind = SliceKind.Selection, Value = "OpenLink" }, 0);
        Assert.Equal("https://github.com/tcurtsinger/Chippi/pull/183", f.Pc.Opened[^1]);
        f.Hub.Selection = null;
        var nothing = await Assert.ThrowsAsync<SliceFailure>(() => runner.RunAsync(new Slice { Kind = SliceKind.Selection, Value = "Search" }, 0));
        Assert.Equal("Nothing selected", nothing.Message);
    }

    [Fact]
    public async Task In_a_terminal_the_selection_is_read_without_Ctrl_C()
    {
        var f = new Fixture();
        f.Pc.Terminal = (true, "Get-Process");
        f.Hub.Selection = "should not be read";
        await f.Runner().RunAsync(new Slice { Kind = SliceKind.Selection, Value = "Search" }, 0);
        Assert.Equal(0, f.Hub.SelectionsCopied);
        Assert.EndsWith("Get-Process", f.Pc.Opened[^1], StringComparison.Ordinal);
    }

    [Fact]
    public void Gone_targets_are_found_out()
    {
        var f = new Fixture();
        var runner = f.Runner();
        Assert.False(runner.IsMissing(new Slice { Kind = SliceKind.App, Value = "spotify" }));
        Assert.True(runner.IsMissing(new Slice { Kind = SliceKind.App, Value = "Steam" }));
        Assert.True(runner.IsMissing(new Slice { Kind = SliceKind.File, Value = @"C:\gone.txt" }));
        Assert.False(runner.IsMissing(new Slice { Kind = SliceKind.File, Value = @"C:\Users\Travis\Downloads" }));
        Assert.True(runner.IsMissing(new Slice { Kind = SliceKind.Chippi, Value = "cleaning-mode/start" }));
        Assert.False(runner.IsMissing(new Slice { Kind = SliceKind.Chippi, Value = "keep-awake/toggle" }));
        // A clipboard copy isn't something a slice can hold.
        Assert.DoesNotContain(runner.ChippiCommands(), c => c.Kind == ChippiCommandKind.Paste);
        // Before the Start menu is read, no app counts as gone.
        f.Pc.AppList = [];
        Assert.False(runner.IsMissing(new Slice { Kind = SliceKind.App, Value = "Steam" }));
    }

    // ----- The module -----

    [Fact]
    public void Installed_nothing_opens_it_until_a_trigger_is_on() => WpfTestThread.Run(() =>
    {
        var f = new Fixture();
        f.StartAsync().GetAwaiter().GetResult();
        Assert.Equal("No trigger", f.Module.Status);
        Assert.Equal((false, false, false), f.Input.Watched);
        f.Module.SetHold(MouseButton.Middle, true);
        Assert.Equal((true, false, false), f.Input.Watched);
        Assert.Null(f.Module.Status);
        Assert.True(f.Settings.Value.HoldMiddle);
        Stop(f);
    });

    [Fact]
    public void Held_flicked_and_let_go_a_slice_runs_and_the_wheel_fades() => WpfTestThread.Run(() =>
    {
        var f = new Fixture { Slices = [Slice(MediaAction.PlayPause), Slice(MediaAction.Next), Slice(MediaAction.Previous), Slice(MediaAction.Mute)] };
        f.StartAsync().GetAwaiter().GetResult();
        IWheelInputSink sink = f.Module;
        sink.Opened(f.Pc.Middle, MouseButton.Middle);
        var wheel = f.Module.Open!;
        Assert.True(wheel.Holding);
        Assert.Equal(new System.Windows.Point(960, 540), wheel.Top.Middle);
        Assert.Equal(4, f.Module.View!.Slots.Count);
        Assert.Equal("", f.Module.View.HubText); // the first wheel's hub shows Chippi alone
        sink.Moved(Offset(f.Pc.Middle, 100, 0)); // right: Next
        Assert.Equal(1, wheel.Pointed);
        Assert.Equal(SlotLook.Pointed, f.Module.View.LookOf(1));
        Assert.Equal("Next", f.Module.View.HubText);
        sink.Released(Offset(f.Pc.Middle, 100, 0));
        Assert.Null(f.Module.Open);
        Assert.Equal(SlotLook.Picked, f.Module.View.LookOf(1));
        Assert.Equal([MediaAction.Next], f.Pc.Played);
        Assert.Equal(1, f.Input.ClosedCount);
        Stop(f);
    });

    [Fact]
    public void Over_a_game_that_hides_the_pointer_it_opens_in_the_middle_of_the_screen() => WpfTestThread.Run(() =>
    {
        var f = new Fixture { Slices = [Slice(MediaAction.PlayPause)] };
        f.StartAsync().GetAwaiter().GetResult();
        f.Pc.Shown = false;
        ((IWheelInputSink)f.Module).Opened(new PixelPoint(-20000 + 10, -20000 + 10), MouseButton.Middle);
        Assert.Equal(new System.Windows.Point(960, 540), f.Module.Open!.Top.Middle);
        Assert.Equal(f.Pc.Middle, f.Input.Virtual);
        Stop(f);
    });

    [Fact]
    public void A_failure_says_so_on_the_bar() => WpfTestThread.Run(() =>
    {
        var f = new Fixture { Slices = [new Slice { Kind = SliceKind.Window, Value = "Close", Name = "Close" }] };
        f.StartAsync().GetAwaiter().GetResult();
        f.Pc.Under = 0; // the desktop under the pointer
        IWheelInputSink sink = f.Module;
        sink.Opened(f.Pc.Middle, MouseButton.Middle);
        sink.Moved(Offset(f.Pc.Middle, 0, -100));
        sink.Released(Offset(f.Pc.Middle, 0, -100));
        var shown = f.Hub.Activities.Single();
        Assert.Equal(("Close failed", "No window"), (shown.Title, shown.Source));
        Assert.True(shown.NeedsAttention);
        Assert.True(shown.IsFeedback);
        Stop(f);
    });

    [Fact]
    public void It_stays_closed_during_Cleaning_mode() => WpfTestThread.Run(() =>
    {
        var f = new Fixture { Cleaning = true, Slices = [Slice(MediaAction.PlayPause)] };
        f.StartAsync().GetAwaiter().GetResult();
        ((IWheelInputSink)f.Module).Opened(f.Pc.Middle, MouseButton.Middle);
        Assert.Null(f.Module.Open);
        Assert.Equal(1, f.Input.ClosedCount);
        Assert.True(f.Input.Paused());
        Stop(f);
    });

    [Fact]
    public void The_shortcut_opens_it_for_a_click_and_closes_it_again() => WpfTestThread.Run(() =>
    {
        var f = new Fixture { Slices = [Slice(MediaAction.PlayPause), Slice(MediaAction.Next)] };
        f.StartAsync().GetAwaiter().GetResult();
        Assert.True(f.Module.ChangeShortcut(new MicShortcut(ShortcutModifiers.Control | ShortcutModifiers.Alt, 0x52)));
        f.Hotkey.Pressed!();
        Assert.False(f.Module.Open!.Holding);
        Assert.Equal(1, f.Input.ByShortcut);
        ((IWheelInputSink)f.Module).Clicked(Offset(f.Pc.Middle, 0, -100));
        Assert.Equal([MediaAction.PlayPause], f.Pc.Played);
        f.Hotkey.Pressed!();
        Assert.NotNull(f.Module.Open);
        f.Hotkey.Pressed!();
        Assert.Null(f.Module.Open);
        Stop(f);
    });

    [Fact]
    public void The_hubs_Chippi_is_wide_awake_while_Keep_awake_runs() => WpfTestThread.Run(() =>
    {
        var f = new Fixture { KeepAwake = true, Slices = [Slice(MediaAction.PlayPause)] };
        f.StartAsync().GetAwaiter().GetResult();
        ((IWheelInputSink)f.Module).Opened(f.Pc.Middle, MouseButton.Middle);
        Assert.Equal(PetMood.WideAwake, f.Module.View!.Pet.Mood);
        Stop(f);
    });

    [Fact]
    public void Esc_closes_without_running_anything() => WpfTestThread.Run(() =>
    {
        var f = new Fixture { Slices = [Slice(MediaAction.PlayPause)] };
        f.StartAsync().GetAwaiter().GetResult();
        IWheelInputSink sink = f.Module;
        sink.Opened(f.Pc.Middle, MouseButton.Middle);
        sink.Moved(Offset(f.Pc.Middle, 0, -100));
        sink.Dismissed();
        Assert.Null(f.Module.Open);
        Assert.Empty(f.Pc.Played);
        Stop(f);
    });

    [Fact]
    public void Settings_page_edits_the_wheel() => WpfTestThread.Run(() =>
    {
        var f = new Fixture();
        f.StartAsync().GetAwaiter().GetResult();
        var page = new Views.RadialMenuSettingsPage(f.Module);
        f.Module.SetSlices([Slice(MediaAction.PlayPause), new Slice { Kind = SliceKind.Submenu, Name = "Media", Slices = [Slice(MediaAction.Next)] }]);
        Assert.Equal(2, f.Settings.Value.Slices.Count);
        Assert.NotNull(page.Content);
        Stop(f);
    });

    private static Slice Slice(MediaAction action) => new() { Kind = SliceKind.Media, Value = action.ToString(), Name = SliceCatalog.NameOf(action) };

    private static PixelPoint Offset(PixelPoint at, int x, int y) => new(at.X + x, at.Y + y);

    private static void Stop(Fixture f) => f.Module.StopAsync(default).GetAwaiter().GetResult();
}
