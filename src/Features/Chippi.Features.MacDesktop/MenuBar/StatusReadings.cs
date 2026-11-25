// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Globalization;
using System.Windows.Threading;
using Chippi.Features.MacDesktop.Shell;

namespace Chippi.Features.MacDesktop.MenuBar;

/// <summary>What the right of the menu bar shows: the network, the battery and the input language. Read on the UI thread.</summary>
internal sealed class StatusReadings : IDisposable
{
    private readonly DispatcherTimer _slow, _fast;

    public enum Network { None, WiFi, Wired }

    public Network Connection { get; private set; } = Network.WiFi;

    /// <summary>Wi-Fi signal bars, 0 to 4; null when unknown.</summary>
    public int? Bars { get; private set; }

    /// <summary>The battery's charge, 0 to 100; null on a PC without one.</summary>
    public int? Battery { get; private set; }

    public bool Charging { get; private set; }

    /// <summary>The input language of the app in front ("ENG"); null with only one keyboard installed.</summary>
    public string? Language { get; private set; }

    public event Action? Changed;

    public StatusReadings(Dispatcher dispatcher)
    {
        _slow = new DispatcherTimer(TimeSpan.FromSeconds(30), DispatcherPriority.Background, (_, _) => ReadSlow(), dispatcher);
        _fast = new DispatcherTimer(TimeSpan.FromSeconds(1.5), DispatcherPriority.Background, (_, _) => ReadLanguage(), dispatcher);
        try { Windows.Networking.Connectivity.NetworkInformation.NetworkStatusChanged += OnNetworkChanged; }
        catch (Exception ex) { Chippi.Core.Diagnostics.Log.Warn("Mac-style desktop: watching the network failed", ex); }
        ReadSlow();
        ReadLanguage();
    }

    private void OnNetworkChanged(object sender) => _slow.Dispatcher.BeginInvoke(ReadSlow);

    private void ReadSlow()
    {
        var (connection, bars) = ReadNetwork();
        int? battery = null;
        bool charging = false;
        if (Native.GetSystemPowerStatus(out var power) && (power.BatteryFlag & 128) == 0 && power.BatteryPercent <= 100)
        {
            battery = power.BatteryPercent;
            charging = power.AcLine == 1;
        }
        if (connection == Connection && bars == Bars && battery == Battery && charging == Charging)
            return;
        (Connection, Bars, Battery, Charging) = (connection, bars, battery, charging);
        Changed?.Invoke();
    }

    private static (Network, int?) ReadNetwork()
    {
        try
        {
            var profile = Windows.Networking.Connectivity.NetworkInformation.GetInternetConnectionProfile();
            if (profile is null)
                return (Network.None, null);
            if (profile.IsWlanConnectionProfile)
                return (Network.WiFi, profile.GetSignalBars() is { } bars ? Math.Clamp((int)bars, 0, 4) : null);
            return (Network.Wired, null);
        }
        catch (Exception)
        {
            return (Network.WiFi, null);
        }
    }

    private void ReadLanguage()
    {
        string? language = null;
        if (Native.GetKeyboardLayoutList(0, null) > 1)
        {
            nint front = Native.GetForegroundWindow();
            uint thread = front == 0 ? 0 : Native.GetWindowThreadProcessId(front, out _);
            int langId = (int)(Native.GetKeyboardLayout(thread) & 0xFFFF);
            try { language = CultureInfo.GetCultureInfo(langId).ThreeLetterISOLanguageName.ToUpperInvariant(); }
            catch (CultureNotFoundException) { language = null; }
        }
        if (language == Language)
            return;
        Language = language;
        Changed?.Invoke();
    }

    public void Start()
    {
        _slow.Start();
        _fast.Start();
    }

    public void Dispose()
    {
        _slow.Stop();
        _fast.Stop();
        try { Windows.Networking.Connectivity.NetworkInformation.NetworkStatusChanged -= OnNetworkChanged; }
        catch (Exception) { }
    }
}
