// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using Chippi.Features.MacDesktop.Shell;

namespace Chippi.Features.MacDesktop.MenuBar;

/// <summary>
/// What the right of the menu bar shows: the network, the volume, the battery and the input language. The network, the
/// battery and the language are read on the UI thread (a few milliseconds); the volume is read off it.
/// </summary>
internal sealed class StatusReadings : IDisposable
{
    private readonly DispatcherTimer _slow, _fast;
    private bool _readingVolume, _disposed;

    public enum Network { None, WiFi, Wired }

    public Network Connection { get; private set; } = Network.WiFi;

    /// <summary>Wi-Fi signal bars, 0 to 4; null when unknown.</summary>
    public int? Bars { get; private set; }

    /// <summary>The battery's charge, 0 to 100; null on a PC without one.</summary>
    public int? Battery { get; private set; }

    public bool Charging { get; private set; }

    /// <summary>The input language of the app in front ("ENG"); null with only one keyboard installed.</summary>
    public string? Language { get; private set; }

    /// <summary>The default output's volume, 0 to 100; null when there's no output or it can't be read.</summary>
    public int? Volume { get; private set; }

    public bool Muted { get; private set; }

    public event Action? Changed;

    public StatusReadings(Dispatcher dispatcher)
    {
        _slow = new DispatcherTimer(TimeSpan.FromSeconds(30), DispatcherPriority.Background, (_, _) => ReadSlow(), dispatcher);
        _fast = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => { ReadLanguage(); _ = ReadVolumeAsync(); }, dispatcher);
        try { Windows.Networking.Connectivity.NetworkInformation.NetworkStatusChanged += OnNetworkChanged; }
        catch (Exception ex) { Chippi.Core.Diagnostics.Log.Warn("Mac-style desktop: watching the network failed", ex); }
        ReadSlow();
        ReadLanguage();
        _ = ReadVolumeAsync();
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

    /// <summary>Reads the default output's volume and mute once a second: only read, never set.</summary>
    private async Task ReadVolumeAsync()
    {
        if (_readingVolume || _disposed)
            return;
        _readingVolume = true;
        var reading = await Task.Run(VolumeReading.Read).ConfigureAwait(false);
        // Back on the bar's thread, wherever the wait resumed.
        _ = _fast.Dispatcher.BeginInvoke(() =>
        {
            _readingVolume = false;
            if (_disposed)
                return;
            int? volume = reading is { } read ? (int)Math.Round(Math.Clamp(read.Level, 0, 1) * 100) : null;
            bool muted = reading is { Muted: true };
            if (volume == Volume && muted == Muted)
                return;
            (Volume, Muted) = (volume, muted);
            Changed?.Invoke();
        });
    }

    public void Start()
    {
        _slow.Start();
        _fast.Start();
    }

    public void Dispose()
    {
        _disposed = true;
        _slow.Stop();
        _fast.Stop();
        try { Windows.Networking.Connectivity.NetworkInformation.NetworkStatusChanged -= OnNetworkChanged; }
        catch (Exception) { }
    }
}

/// <summary>
/// The default output's volume and mute, read through Core Audio. It only reads: the slots of the interfaces that set
/// anything are declared without their arguments, so nothing here can call them.
/// </summary>
internal static class VolumeReading
{
    public static (bool Muted, double Level)? Read()
    {
        object? enumerator = null, volume = null;
        IMMDevice? device = null;
        try
        {
            enumerator = Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E"), throwOnError: true)!);
            if (enumerator is not IMMDeviceEnumerator devices || devices.GetDefaultAudioEndpoint(0 /* render */, 1 /* multimedia */, out device) != 0 || device is null)
                return null;
            var iid = typeof(IAudioEndpointVolume).GUID;
            if (device.Activate(ref iid, 0x17 /* CLSCTX_ALL */, 0, out volume) != 0 || volume is not IAudioEndpointVolume endpoint)
                return null;
            if (endpoint.GetMute(out bool muted) != 0 || endpoint.GetMasterVolumeLevelScalar(out float level) != 0)
                return null;
            return (muted, level);
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or InvalidOperationException or NotSupportedException or UnauthorizedAccessException)
        {
            return null;
        }
        finally
        {
            if (volume is not null) Marshal.ReleaseComObject(volume);
            if (device is not null) Marshal.ReleaseComObject(device);
            if (enumerator is not null) Marshal.ReleaseComObject(enumerator);
        }
    }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints();
        [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice? device);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, uint context, nint activation, [MarshalAs(UnmanagedType.IUnknown)] out object? instance);
    }

    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioEndpointVolume
    {
        [PreserveSig] int RegisterControlChangeNotify();
        [PreserveSig] int UnregisterControlChangeNotify();
        [PreserveSig] int GetChannelCount(out uint count);
        [PreserveSig] int SetMasterVolumeLevel();
        [PreserveSig] int SetMasterVolumeLevelScalar();
        [PreserveSig] int GetMasterVolumeLevel(out float decibels);
        [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
        [PreserveSig] int SetChannelVolumeLevel();
        [PreserveSig] int SetChannelVolumeLevelScalar();
        [PreserveSig] int GetChannelVolumeLevel(uint channel, out float decibels);
        [PreserveSig] int GetChannelVolumeLevelScalar(uint channel, out float level);
        [PreserveSig] int SetMute();
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool muted);
    }
}
