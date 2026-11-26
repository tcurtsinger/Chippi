// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Automation;
using Chippi.Core.Diagnostics;
using Chippi.Features.MacDesktop.Shell;

namespace Chippi.Features.MacDesktop.Dock;

/// <summary>
/// Reads the hidden taskbar's app buttons through UI Automation every two seconds, on a thread of its own, and reports them
/// when any has changed. It only reads: the taskbar stays hidden and nothing on it is pressed. The taskbar is found again
/// after Explorer restarts. Explorer raises no event when an overlay changes, so the buttons are read, not listened to.
/// </summary>
internal sealed class TaskbarBadgeReader : IDisposable
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(2);
    private readonly Action<IReadOnlyList<TaskbarButton>> _changed;
    private readonly ManualResetEventSlim _stop = new();
    private readonly Thread _thread;
    private IReadOnlyList<TaskbarButton> _last = [];
    private AutomationElement? _frame;
    private bool _warned;

    public TaskbarBadgeReader(Action<IReadOnlyList<TaskbarButton>> changed)
    {
        _changed = changed;
        _thread = new Thread(Run) { IsBackground = true, Name = "Chippi dock badges" };
        _thread.SetApartmentState(ApartmentState.MTA);
    }

    public void Start() => _thread.Start();

    private void Run()
    {
        do
        {
            IReadOnlyList<TaskbarButton> buttons;
            try { buttons = Read() ?? []; }
            catch (Exception ex) when (ex is ElementNotAvailableException or COMException or InvalidOperationException or TimeoutException)
            {
                // Explorer restarted or is busy: find the taskbar again next time.
                _frame = null;
                if (!_warned)
                    Log.Warn("Mac-style desktop: reading the taskbar's badges failed", ex);
                _warned = true;
                continue;
            }
            if (buttons.SequenceEqual(_last))
                continue;
            _last = buttons;
            if (!_stop.IsSet)
                _changed(buttons);
        }
        while (!_stop.Wait(Interval));
    }

    /// <summary>The app buttons, in the taskbar's order; null when there's no taskbar to read.</summary>
    private IReadOnlyList<TaskbarButton>? Read()
    {
        if (_frame is null)
        {
            nint tray = Native.FindWindow("Shell_TrayWnd", null);
            if (tray == 0)
                return null;
            _frame = AutomationElement.FromHandle(tray).FindFirst(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.AutomationIdProperty, "TaskbarFrame"));
            if (_frame is null)
                return null;
        }
        var cache = new CacheRequest { TreeScope = TreeScope.Element, AutomationElementMode = AutomationElementMode.None };
        cache.Add(AutomationElement.NameProperty);
        cache.Add(AutomationElement.AutomationIdProperty);
        cache.Add(AutomationElement.HelpTextProperty);
        AutomationElementCollection children;
        using (cache.Activate())
            children = _frame.FindAll(TreeScope.Children, Condition.TrueCondition);
        var buttons = new List<TaskbarButton>(children.Count);
        foreach (AutomationElement child in children)
        {
            var id = child.Cached.AutomationId ?? "";
            if (!id.StartsWith(DockBadges.AppIdPrefix, StringComparison.Ordinal))
                continue; // Start, Search, Task view, widgets
            buttons.Add(new TaskbarButton(child.Cached.Name ?? "", id[DockBadges.AppIdPrefix.Length..], child.Cached.HelpText ?? ""));
        }
        return buttons;
    }

    public void Dispose() => _stop.Set(); // a read under way ends on its own; the thread is a background one
}
