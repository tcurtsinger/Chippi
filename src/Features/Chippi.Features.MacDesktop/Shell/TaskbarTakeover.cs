// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.IO;
using System.Runtime.InteropServices;
using Chippi.Core.Diagnostics;

namespace Chippi.Features.MacDesktop.Shell;

/// <summary>The Windows taskbars, as the takeover sees them. The real one is <see cref="WindowsTaskbars"/>; tests pass a fake.</summary>
public interface ITaskbarShell
{
    /// <summary>Shell_TrayWnd and every Shell_SecondaryTrayWnd; empty while Explorer isn't running.</summary>
    IReadOnlyList<nint> Taskbars();

    /// <summary>The taskbar's own "Automatically hide the taskbar" setting.</summary>
    bool AutoHide { get; set; }

    /// <summary>Whether the bar is invisible and click-through (layered, transparent, alpha 0).</summary>
    bool IsGhosted(nint bar);

    /// <summary>Makes the bar invisible and click-through, or gives it back; true when it is as asked afterwards.</summary>
    bool SetGhost(nint bar, bool ghost);
}

/// <summary>
/// The user's own auto-hide setting, saved before Chippi first changes it and deleted only once it's put back
/// (docs/mac-style/brief.md, The taskbar): a crash and restart never mistakes Chippi's auto-hide for the user's.
/// </summary>
public sealed class TaskbarJournal(string path)
{
    public string Path { get; } = path;

    /// <summary>The saved setting, or null when nothing is saved (or the file can't be read).</summary>
    public bool? Read()
    {
        try
        {
            return File.Exists(Path) ? File.ReadAllText(Path).Trim() switch { "1" => true, "0" => false, _ => null } : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    public bool Exists => File.Exists(Path);

    /// <summary>Saves the setting. Throws when it can't, and then the taskbar isn't touched.</summary>
    public void Write(bool autoHide)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        string temp = Path + ".tmp";
        File.WriteAllText(temp, autoHide ? "1" : "0");
        File.Move(temp, Path, overwrite: true);
    }

    public void Delete()
    {
        try { File.Delete(Path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Log.Warn("Mac-style desktop: deleting the taskbar journal failed", ex); }
    }

    /// <summary>Where the real profile keeps it, for <c>--restore</c> and uninstall, which run without a module.</summary>
    public static string DefaultPath => System.IO.Path.Combine(Chippi.Core.Modules.ModuleServices.DefaultDataRoot, MacDesktopModule.Id, "taskbar-autohide.txt");
}

/// <summary>
/// Hides the Windows taskbar on every display while the menu bar or the dock stands in for it, and gives it back. Auto-hide
/// frees the work area; the bars themselves are then made invisible and click-through, since <c>SW_HIDE</c> doesn't last on
/// Windows 11. Follows Muelle's approach (MIT, THIRD-PARTY-NOTICES.md). Idempotent: called again after Explorer restarts.
/// </summary>
public sealed class TaskbarTakeover(ITaskbarShell shell, TaskbarJournal journal)
{
    /// <summary>Hides every taskbar. False when it couldn't (no Explorer yet, or the journal couldn't be saved).</summary>
    public bool TakeOver()
    {
        var bars = shell.Taskbars();
        // A logon start can run before Explorer creates its taskbar: saving an absent shell's state would lose the user's.
        if (bars.Count == 0)
            return false;
        if (!journal.Exists)
        {
            try { journal.Write(shell.AutoHide); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Never hide the taskbar without a way to give it back after a crash.
                Log.Warn("Mac-style desktop: saving the taskbar's state failed; leaving it as it is", ex);
                return false;
            }
        }
        if (!shell.AutoHide)
            shell.AutoHide = true;
        bool hidden = true;
        foreach (var bar in bars)
            hidden &= shell.IsGhosted(bar) || shell.SetGhost(bar, true);
        return hidden;
    }

    /// <summary>Whether Explorer showed a taskbar again (it can, after a display change or a theme change).</summary>
    public bool NeedsRepair() => shell.Taskbars().Any(bar => !shell.IsGhosted(bar));

    /// <summary>
    /// Puts the taskbar back as it was before <see cref="TakeOver"/>, and deletes the journal. With no journal it still makes
    /// every taskbar visible, and leaves auto-hide as it is.
    /// </summary>
    public void GiveBack()
    {
        foreach (var bar in shell.Taskbars())
            shell.SetGhost(bar, false);
        if (journal.Read() is not { } original)
        {
            if (journal.Exists)
                journal.Delete(); // unreadable: nothing to restore from
            return;
        }
        if (shell.AutoHide != original)
            shell.AutoHide = original;
        journal.Delete();
    }

    /// <summary>
    /// <c>Chippi.exe --restore</c>, uninstall and a start with the mode off: gives the real profile's taskbar back. Safe to
    /// call when nothing was taken.
    /// </summary>
    public static void Restore()
    {
        try { new TaskbarTakeover(new WindowsTaskbars(), new TaskbarJournal(TaskbarJournal.DefaultPath)).GiveBack(); }
        catch (Exception ex) { Log.Warn("Mac-style desktop: giving the taskbar back failed", ex); }
    }

    /// <summary>Whether Windows is shutting down, restarting or signing out right now.</summary>
    public static bool SessionEnding => Native.GetSystemMetrics(Native.SmShuttingDown) != 0;
}

/// <summary>The real taskbars.</summary>
public sealed class WindowsTaskbars : ITaskbarShell
{
    private const long Ghost = Native.WsExLayered | Native.WsExTransparent;

    public IReadOnlyList<nint> Taskbars()
    {
        var found = new List<nint>();
        nint primary = Native.FindWindow("Shell_TrayWnd", null);
        if (primary != 0)
            found.Add(primary);
        for (nint after = 0; (after = Native.FindWindowEx(0, after, "Shell_SecondaryTrayWnd", null)) != 0;)
            found.Add(after);
        return found;
    }

    public bool AutoHide
    {
        get
        {
            var data = Data();
            return ((int)Native.SHAppBarMessage(Native.AbmGetState, ref data) & Native.AbsAutoHide) != 0;
        }
        set
        {
            var data = Data();
            data.Param = value ? Native.AbsAutoHide : 0;
            Native.SHAppBarMessage(Native.AbmSetState, ref data);
        }
    }

    private Native.AppBarData Data() => new()
    {
        Size = Marshal.SizeOf<Native.AppBarData>(),
        Window = Taskbars().FirstOrDefault(),
    };

    public bool IsGhosted(nint bar)
    {
        if ((Native.GetWindowLongPtr(bar, Native.GwlExStyle) & Ghost) != Ghost)
            return false;
        return Native.GetLayeredWindowAttributes(bar, out _, out byte alpha, out uint flags) && alpha == 0 && (flags & Native.LwaAlpha) != 0;
    }

    public bool SetGhost(nint bar, bool ghost)
    {
        long style = Native.GetWindowLongPtr(bar, Native.GwlExStyle);
        if (ghost)
        {
            Native.SetWindowLongPtr(bar, Native.GwlExStyle, (nint)(style | Ghost));
            if (!Native.SetLayeredWindowAttributes(bar, 0, 0, Native.LwaAlpha))
                return false;
            return IsGhosted(bar);
        }
        if ((style & Ghost) != 0)
        {
            Native.SetLayeredWindowAttributes(bar, 0, 255, Native.LwaAlpha);
            Native.SetWindowLongPtr(bar, Native.GwlExStyle, (nint)(style & ~Ghost));
        }
        return !IsGhosted(bar);
    }
}
