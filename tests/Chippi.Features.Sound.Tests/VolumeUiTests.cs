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