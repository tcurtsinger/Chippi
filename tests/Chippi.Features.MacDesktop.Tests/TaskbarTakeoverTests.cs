// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.Features.MacDesktop.Shell;

namespace Chippi.Features.MacDesktop.Tests;

/// <summary>The takeover against a fake shell: the user's auto-hide is saved first and put back, and a crash restore works.</summary>
public sealed class TaskbarTakeoverTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "chippi-mac-tests", Guid.NewGuid().ToString("N"));

    private TaskbarJournal Journal => new(Path.Combine(_folder, "taskbar-autohide.txt"));

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); } catch (DirectoryNotFoundException) { }
    }

    private sealed class FakeShell : ITaskbarShell
    {
        public List<nint> Bars { get; } = [1, 2];
        public HashSet<nint> Ghosted { get; } = [];
        public bool AutoHide { get; set; }
        public int AutoHideWrites { get; private set; }
        bool ITaskbarShell.AutoHide { get => AutoHide; set { AutoHide = value; AutoHideWrites++; } }
        public IReadOnlyList<nint> Taskbars() => Bars;
        public bool IsGhosted(nint bar) => Ghosted.Contains(bar);
        public bool SetGhost(nint bar, bool ghost)
        {
            if (ghost) Ghosted.Add(bar); else Ghosted.Remove(bar);
            return true;
        }
    }

    [Fact]
    public void TakeOverHidesEveryBarAndSavesTheUsersSetting()
    {
        var shell = new FakeShell();
        var takeover = new TaskbarTakeover(shell, Journal);
        Assert.True(takeover.TakeOver());
        Assert.True(shell.AutoHide);
        Assert.Equal([1, 2], shell.Ghosted.Order());
        Assert.False(Journal.Read());
        Assert.False(takeover.NeedsRepair());
    }

    [Fact]
    public void GiveBackRestoresTheUsersSettingAndDeletesTheJournal()
    {
        var shell = new FakeShell();
        var takeover = new TaskbarTakeover(shell, Journal);
        takeover.TakeOver();
        takeover.GiveBack();
        Assert.False(shell.AutoHide);
        Assert.Empty(shell.Ghosted);
        Assert.False(Journal.Exists);
    }

    [Fact]
    public void AUserWhoAlreadyAutoHidesKeepsIt()
    {
        var shell = new FakeShell { AutoHide = true };
        var takeover = new TaskbarTakeover(shell, Journal);
        takeover.TakeOver();
        takeover.GiveBack();
        Assert.True(shell.AutoHide);
        Assert.Equal(0, shell.AutoHideWrites);
    }

    [Fact]
    public void TakingOverAgainNeverOverwritesTheSavedSetting()
    {
        var shell = new FakeShell();
        var takeover = new TaskbarTakeover(shell, Journal);
        takeover.TakeOver();
        // Explorer restarted: the shell now reports Chippi's own auto-hide.
        shell.Ghosted.Clear();
        Assert.True(takeover.NeedsRepair());
        takeover.TakeOver();
        Assert.False(Journal.Read());
    }

    [Fact]
    public void ARestartAfterACrashGivesTheTaskbarBack()
    {
        var shell = new FakeShell();
        new TaskbarTakeover(shell, Journal).TakeOver();
        // A new process: nothing in memory but the journal.
        new TaskbarTakeover(shell, Journal).GiveBack();
        Assert.False(shell.AutoHide);
        Assert.Empty(shell.Ghosted);
        Assert.False(Journal.Exists);
    }

    [Fact]
    public void NoExplorerMeansNothingIsTouched()
    {
        var shell = new FakeShell();
        shell.Bars.Clear();
        Assert.False(new TaskbarTakeover(shell, Journal).TakeOver());
        Assert.False(shell.AutoHide);
        Assert.False(Journal.Exists);
    }

    [Fact]
    public void GiveBackWithoutAJournalShowsTheBarsAndLeavesAutoHide()
    {
        var shell = new FakeShell { AutoHide = true };
        shell.Ghosted.Add(1);
        new TaskbarTakeover(shell, Journal).GiveBack();
        Assert.Empty(shell.Ghosted);
        Assert.True(shell.AutoHide);
    }
}
