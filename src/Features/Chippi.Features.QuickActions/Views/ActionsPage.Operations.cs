// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Controls.Primitives;
using Chippi.Core.Diagnostics;

namespace Chippi.Features.QuickActions.Views;

internal sealed partial class ActionsPage
{
    private readonly Dictionary<QuickActionId, DateTime> _failed = [];
    private readonly HashSet<(QuickActionId Action, string Key)> _running = [];
    private Popup? _confirm;
    internal Popup? Confirmation => _confirm;
    internal Action<Popup> OpenConfirmation { get; set; } = popup => popup.IsOpen = true;

    /// <summary>A shortcut's action failed. It stays marked until that action succeeds.</summary>
    public void ShowFailed(QuickActionId id)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => ShowFailed(id));
            return;
        }
        _failed[id] = DateTime.MaxValue;
        if (id == QuickActionId.Eject && _module.State.Drives is [var only])
            _rowFailures[(id, only.Id)] = "Eject failed";
        Render();
    }

    /// <summary>A shortcut succeeded. Its old warning should not greet the next visit.</summary>
    internal void ShowSucceeded(QuickActionId id)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => ShowSucceeded(id));
            return;
        }
        foreach (var key in _rowFailures.Keys.Where(k => k.Action == id).ToArray())
            _rowFailures.Remove(key);
        _failed.Remove(id);
        Render();
    }

    /// <summary>A removed target no longer has a retry to offer. Never clear another target's error.</summary>
    private void PruneFailures()
    {
        var state = _module.State;
        foreach (var key in _rowFailures.Keys.ToArray())
        {
            bool exists = key.Action switch
            {
                QuickActionId.Quit => state.Apps.Any(a => string.Equals(a.Path, key.Key, StringComparison.OrdinalIgnoreCase)),
                QuickActionId.Eject => state.Drives.Any(d => d.Id == key.Key),
                QuickActionId.Plan => state.Plans.Any(p => p.Id.ToString() == key.Key),
                _ => true,
            };
            if (exists)
                continue;
            _rowFailures.Remove(key);
            if (!_rowFailures.Keys.Any(k => k.Action == key.Action))
                _failed.Remove(key.Action);
        }
    }

    private bool Running(QuickActionId action, string key) => _running.Contains((action, key));

    /// <summary>Runs once at a time per target, with persistent, target-specific feedback and a retry at that target.</summary>
    private async Task Act(QuickActionId id, Func<Task> work, string? failure = null, string? item = null)
    {
        var target = (Action: id, Key: item ?? "");
        // Removed buttons may still have a queued invocation. Enforce operation ownership here as well as visually.
        if (_running.Any(r => r.Action == id &&
            (id is QuickActionId.Plan or QuickActionId.Power || id == QuickActionId.Quit && (r.Key == "bulk" || target.Key == "bulk"))))
            return;
        if (!_running.Add(target))
            return;
        Render();
        try
        {
            await work();
            if (item is not null)
                _rowFailures.Remove(target);
            if (!_rowFailures.Keys.Any(k => k.Action == id))
                _failed.Remove(id);
        }
        catch (Exception ex)
        {
            Log.Warn($"Quick actions: {QuickAction.Of(id).Key} failed", ex);
            _failed[id] = DateTime.MaxValue;
            if (ex is QuitAppsException quits)
            {
                foreach (var app in quits.Apps)
                    _rowFailures[(QuickActionId.Quit, app.Path)] = "Quit failed";
            }
            else if (item is not null)
                _rowFailures[target] = failure ?? $"{QuickAction.Of(id).Label} failed";
            else if (id == QuickActionId.Eject && _module.State.Drives is [var only])
                _rowFailures[(id, only.Id)] = "Eject failed";
        }
        finally
        {
            _running.Remove(target);
            Render();
        }
    }

    private void AskFirst(UIElement anchor, string question, string verb, Action confirmed, string? detail = null)
    {
        CloseConfirm();
        ConfirmationQuestion = question;
        var popup = Ui.Confirm(anchor, question, verb, () =>
        {
            CloseConfirm();
            confirmed();
        }, detail);
        _confirm = popup;
        popup.Closed += (_, _) =>
        {
            if (_confirm != popup)
                return;
            _confirm = null;
            ConfirmationQuestion = null;
            ConfirmedApps = null;
            Render();
        };
        OpenConfirmation(popup);
    }

    private void CloseConfirm()
    {
        var popup = _confirm;
        _confirm = null;
        ConfirmationQuestion = null;
        ConfirmedApps = null;
        if (popup is not null)
        {
            popup.IsOpen = false;
            Render();
        }
    }
}
