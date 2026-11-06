// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Runtime.InteropServices;
using Chippi.Core.Shortcuts;
using Microsoft.Win32;

namespace Chippi.Core.SystemIntegration;

/// <summary>
/// The real "Use the Print screen key to open screen capture" (docs/phase-7/capture-layer.md, Print Screen). Writing it
/// takes effect at Windows' next Print Screen press, and the <c>WM_SETTINGCHANGE</c> broadcast gives the key back at once
/// (the Sandbox spike). What Chippi found is kept beside Chippi's own registry values, so the uninstaller can put it back.
/// Only Chippi.exe in the real profile uses this; dev profiles and tests use <see cref="MemoryPrintScreenSetting"/>.
/// </summary>
public sealed class WindowsPrintScreenSetting : IPrintScreenSetting, IDisposable
{
    private const string KeyboardKey = @"Control Panel\Keyboard";
    private const string ValueName = "PrintScreenKeyForSnippingEnabled";
    private const string ChippiKey = @"Software\Chippi";
    private const string FoundName = "PrintScreenFound";
    private const string Unset = "unset";

    private readonly SynchronizationContext? _ui;
    private readonly ManualResetEvent _stop = new(false);
    private Thread? _watcher;
    private Action? _changed;

    /// <param name="ui">Where <see cref="Changed"/> is raised; the shell passes its UI thread's.</param>
    public WindowsPrintScreenSetting(SynchronizationContext? ui = null) => _ui = ui;

    public int? Read() => Registry.GetValue(@"HKEY_CURRENT_USER\" + KeyboardKey, ValueName, null) is int value ? value : null;

    public void Write(int? value)
    {
        using (var key = Registry.CurrentUser.CreateSubKey(KeyboardKey, writable: true))
        {
            if (value is null)
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            else
                key.SetValue(ValueName, value.Value, RegistryValueKind.DWord);
        }
        // A broadcast can wait on hung windows: never on the UI thread.
        _ = Task.Run(Broadcast);
    }

    public PrintScreenClaim? Claim
    {
        get => ReadClaim();
        set
        {
            using var key = Registry.CurrentUser.CreateSubKey(ChippiKey, writable: true);
            if (value is null)
                key.DeleteValue(FoundName, throwOnMissingValue: false);
            else
                key.SetValue(FoundName, value.Found?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? Unset, RegistryValueKind.String);
        }
    }

    private static PrintScreenClaim? ReadClaim() =>
        Registry.GetValue(@"HKEY_CURRENT_USER\" + ChippiKey, FoundName, null) is string found
            ? new PrintScreenClaim(int.TryParse(found, out int v) ? v : null)
            : null;

    /// <summary>
    /// The uninstaller's step: when Chippi holds Print Screen, Windows' setting goes back to what Chippi found. A failure
    /// is the caller's to log; it never blocks the uninstall.
    /// </summary>
    public static void RestoreOnUninstall()
    {
        var setting = new WindowsPrintScreenSetting();
        if (setting.Claim is not { } claim)
            return;
        setting.Write(claim.Found);
        setting.Claim = null;
        Broadcast(); // the uninstaller exits next: don't leave it to a task
    }

    public event Action? Changed
    {
        add
        {
            _changed += value;
            if (_watcher is null)
            {
                _watcher = new Thread(Watch) { IsBackground = true, Name = "Chippi Print Screen setting" };
                _watcher.Start();
            }
        }
        remove => _changed -= value;
    }

    /// <summary>Waits on Windows for any change to the Keyboard key's values, and says so on the UI thread.</summary>
    private void Watch()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(KeyboardKey, System.Security.AccessControl.RegistryRights.Notify);
            if (key is null)
                return;
            using var changed = new AutoResetEvent(false);
            while (true)
            {
                if (RegNotifyChangeKeyValue(key.Handle.DangerousGetHandle(), false, NotifyChangeLastSet, changed.SafeWaitHandle.DangerousGetHandle(), true) != 0)
                    return;
                if (WaitHandle.WaitAny([changed, _stop]) == 1)
                    return;
                if (_ui is null)
                    _changed?.Invoke();
                else
                    _ui.Post(_ => _changed?.Invoke(), null);
            }
        }
        catch (Exception ex) { Diagnostics.Log.Warn("Print Screen setting: watching for changes failed", ex); }
    }

    public void Dispose() => _stop.Set();

    private static void Broadcast()
    {
        SendMessageTimeout(HwndBroadcast, WmSettingChange, 0, null, SmtoAbortIfHung, 3000, out _);
        SendMessageTimeout(HwndBroadcast, WmSettingChange, 0, KeyboardKey, SmtoAbortIfHung, 3000, out _);
    }

    private const nint HwndBroadcast = 0xFFFF;
    private const uint WmSettingChange = 0x001A, SmtoAbortIfHung = 0x0002, NotifyChangeLastSet = 0x00000004;

    [DllImport("advapi32.dll")]
    private static extern int RegNotifyChangeKeyValue(nint key, bool watchSubtree, uint filter, nint @event, bool asynchronous);

    [DllImport("user32.dll", EntryPoint = "SendMessageTimeoutW", CharSet = CharSet.Unicode)]
    private static extern nint SendMessageTimeout(nint hwnd, uint msg, nint wParam, string? lParam, uint flags, uint timeout, out nint result);
}
