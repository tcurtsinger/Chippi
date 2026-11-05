// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Interop;
using Chippi.Features.AppSwitcher.Windows;
using Chippi.Island.Tests;

namespace Chippi.Features.AppSwitcher.Tests;

/// <summary>The tests' own windows on the desktop, never activated: the list's rules and a live preview.</summary>
public sealed class RealWindowTests
{
    private static Window Show(string title, Action<Window>? setup = null)
    {
        var w = new Window { Title = title, Width = 320, Height = 200, Left = -2000, Top = -2000, ShowActivated = false, ShowInTaskbar = true };
        setup?.Invoke(w);
        w.Show();
        return w;
    }

    [Fact]
    public void The_list_follows_Alt_Tabs_rules_and_leaves_out_Chippis_own_windows()
    {
        WpfTestThread.Run(() =>
        {
            string tag = Guid.NewGuid().ToString("N")[..8];
            var app = Show($"switcher app {tag}");
            var tool = Show($"switcher tool {tag}", w => w.WindowStyle = WindowStyle.ToolWindow);
            // An owned window is left out, unless it asks for the taskbar (WS_EX_APPWINDOW, which WPF sets for ShowInTaskbar).
            var owned = Show($"switcher owned {tag}", w => { w.Owner = app; w.ShowInTaskbar = false; });
            var ownedApp = Show($"switcher owned app {tag}", w => w.Owner = app);
            var untitled = Show("");
            var minimized = Show($"switcher minimized {tag}");
            minimized.WindowState = WindowState.Minimized;
            try
            {
                // Tool windows: WPF's ToolWindow style is WS_EX_TOOLWINDOW.
                var mine = new WindowsSwitcherPc(self: 0).Windows().Where(w => w.Title.Contains(tag, StringComparison.Ordinal)).ToList();
                Assert.Equal([$"switcher app {tag}", $"switcher minimized {tag}", $"switcher owned app {tag}"], mine.Select(w => w.Title).Order());
                Assert.True(mine.Single(w => w.Title.StartsWith("switcher minimized", StringComparison.Ordinal)).Minimized);
                var listed = mine.Single(w => w.Title.StartsWith("switcher app", StringComparison.Ordinal));
                Assert.Equal(new WindowInteropHelper(app).Handle, listed.Handle);
                Assert.Equal((uint)Environment.ProcessId, listed.ProcessId);
                Assert.NotEqual(0, listed.Monitor);
                Assert.EndsWith(".exe", listed.Path, StringComparison.OrdinalIgnoreCase);

                var pc = new WindowsSwitcherPc();
                Assert.DoesNotContain(pc.Windows(), w => w.Title.Contains(tag, StringComparison.Ordinal));
                Assert.True(pc.IsSame(listed.Handle, listed.ProcessId));
                Assert.False(pc.IsSame(listed.Handle, listed.ProcessId + 1));
            }
            finally
            {
                foreach (var w in new[] { owned, ownedApp, tool, untitled, minimized, app })
                    w.Close();
            }
        });
    }

    [Fact]
    public void A_closed_window_is_no_longer_the_same()
    {
        WpfTestThread.Run(() =>
        {
            var w = Show("switcher closing");
            nint h = new WindowInteropHelper(w).Handle;
            w.Close();
            Assert.False(new WindowsSwitcherPc().IsSame(h, (uint)Environment.ProcessId));
        });
    }

    [Fact]
    public void A_live_preview_draws_a_window_into_another_and_goes_when_disposed()
    {
        WpfTestThread.Run(() =>
        {
            var overlay = Show("switcher overlay", w => w.AllowsTransparency = false);
            var source = Show("switcher source");
            try
            {
                using var preview = WindowPreview.Create(new WindowInteropHelper(overlay).Handle, new WindowInteropHelper(source).Handle);
                Assert.NotNull(preview);
                var (width, height) = preview!.SourceSize;
                Assert.True(width > 0 && height > 0, $"{width}x{height}");
                Assert.True(preview.Place(8, 8, 168, 108));
                preview.Dispose();
                Assert.False(preview.Place(8, 8, 168, 108));
                Assert.Equal((0, 0), preview.SourceSize);
            }
            finally
            {
                source.Close();
                overlay.Close();
            }
            Assert.Null(WindowPreview.Create(0, 0));
        });
    }
}
