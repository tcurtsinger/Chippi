// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors
using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Threading;
using Chippi.Features.Sound.Volume;
using static Chippi.Features.Sound.Tests.RoutingFixture;
namespace Chippi.Features.Sound.Tests;
public sealed class VolumeUiTests
{
    [Fact]
    public async Task Unshown_volume_and_mute_controls_change_the_same_sessions()
    {
        await using var f = new VolumeFixture(); await f.Start();
        await Sta(async () =>
        {
            var view = new VolumeView(f.Module,VolumeSurface.Island);
            var slider = Descendants(view).OfType<Slider>().Single(s=>AutomationProperties.GetName(s)=="Music volume");
            var range = (IRangeValueProvider)new SliderAutomationPeer(slider);
            Assert.Equal(100,range.Maximum);
            range.SetValue(35);
            await Until(()=>f.System.SessionById("one").Volume==0.35f);
            var mute = Descendants(view).OfType<Button>().Single(b=>AutomationProperties.GetName(b)=="Mute Music");
            ((IInvokeProvider)new ButtonAutomationPeer(mute)).Invoke();
            await Until(()=>f.System.SessionById("one").Muted);
        });
    }
    [Theory]
    [InlineData(7, 56)]
    [InlineData(12, 34)]
    [InlineData(14, 28)]
    [InlineData(18, 28)]
    public async Task Strips_fit_the_body_at_each_density_and_leave_overflow_out(int count, double width)
    {
        await using var f = new VolumeFixture();
        f.System.EndSession("one");
        for (int i = 0; i < count; i++)
            f.System.WithSession(VolumeFixture.Session($"app{i}", "A", 200 + i, $@"C:\Apps\App{i}.exe"));
        await f.Start();
        await Sta(() =>
        {
            var view = new VolumeView(f.Module, VolumeSurface.Island);
            view.Measure(new Size(496, 304)); view.Arrange(new Rect(0, 0, 496, 304)); view.UpdateLayout();
            var strips = (StackPanel)((Grid)view.Content).Children[0];
            Assert.Equal(Math.Min(14, count), strips.Children.Count);
            foreach (FrameworkElement strip in strips.Children) Assert.Equal(width, strip.Width);
            Assert.True(strips.DesiredSize.Width <= 496.001);
            Assert.True(strips.DesiredSize.Height <= 304.001);
            Assert.Empty(Descendants(view).OfType<ScrollViewer>());
            return Task.CompletedTask;
        });
    }

    [Theory]
    [InlineData("Python", "Py")]
    [InlineData(" PowerShell", "Po")]
    [InlineData("R", "R")]
    public void Fallback_distinguishes_apps_sharing_the_first_letter(string name, string expected) =>
        Assert.Equal(expected, VolumeView.Initials(name));

    [Fact]
    public async Task A_name_past_two_lines_marks_the_second_and_keeps_its_tooltip()
    {
        const string full = "Microsoft Edge WebView2 Runtime";
        await using var f = new VolumeFixture();
        f.System.EndSession("one");
        f.System.WithSession(VolumeFixture.Session("edge", "A", 222) with { AppName = full });
        await f.Start();
        await Sta(() =>
        {
            var view = new VolumeView(f.Module, VolumeSurface.Island);
            view.Measure(new Size(496, 304)); view.Arrange(new Rect(0, 0, 496, 304)); view.UpdateLayout();
            var name = Descendants(view).OfType<TextBlock>().Single(t => Equals(t.ToolTip, full));
            Assert.EndsWith("\u2026", name.Text);
            Assert.NotEqual(full, name.Text);
            Assert.Equal(TextWrapping.WrapWithOverflow, name.TextWrapping);
            Assert.Equal(TextTrimming.CharacterEllipsis, name.TextTrimming);
            var measured = new TextBlock { Text = name.Text, FontSize = name.FontSize, FontFamily = name.FontFamily, LineHeight = 14, LineStackingStrategy = LineStackingStrategy.BlockLineHeight, TextWrapping = name.TextWrapping };
            measured.Measure(new Size(56, double.PositiveInfinity));
            Assert.Equal(28, measured.DesiredSize.Height);
            return Task.CompletedTask;
        });
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        foreach(var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach(var item in Descendants(child)) yield return item;
    }
    private static Task Sta(Func<Task> run)
    {
        var done=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread=new Thread(()=>
        {
            var dispatcher=Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            Exception? error=null;
            dispatcher.BeginInvoke(async()=>
            {
                try { await run(); } catch(Exception ex) { error=ex; }
                finally { dispatcher.BeginInvokeShutdown(DispatcherPriority.Background); }
            });
            Dispatcher.Run();
            if(error is null) done.SetResult(); else done.SetException(error);
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        return done.Task;
    }
}