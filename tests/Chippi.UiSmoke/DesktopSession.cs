// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Automation;
using Microsoft.Win32;
using Xunit.Abstractions;

namespace Chippi.UiSmoke;

internal sealed class DesktopSession : IDisposable
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private readonly ITestOutputHelper _output;
    private readonly List<Process> _children = [];
    private readonly string _profile = "ui-smoke-" + Guid.NewGuid().ToString("N");
    private readonly string _roaming;
    private readonly string _local;
    private readonly nint _foreground = GetForegroundWindow();
    private bool _disposed;

    public DesktopSession(ITestOutputHelper output, Action<string, string>? initialize = null)
    {
        _output = output;
        Executable = Path.GetFullPath(Environment.GetEnvironmentVariable("CHIPPI_SMOKE_EXE")
            ?? throw new InvalidOperationException("Set CHIPPI_SMOKE_EXE to the built Chippi.exe. See README.md."));
        Assert.True(File.Exists(Executable), $"Executable missing: {Executable}");
        _roaming = ProfileDirectory(Environment.SpecialFolder.ApplicationData);
        _local = ProfileDirectory(Environment.SpecialFolder.LocalApplicationData);
        Directory.CreateDirectory(_roaming);
        // Do not flash an island on the user's desktop except during its explicit toggle test.
        File.WriteAllText(SettingsPath, """{"version":1,"app":{"islandEnabled":false,"reduceMotion":true},"modules":{"downloads-organizer":{"initialized":true,"organizeInExplorer":false,"folders":[],"rules":[]}}}""");
        _output.WriteLine($"Profile: {_profile}; executable: {Executable}");
        try
        {
            initialize?.Invoke(SettingsPath, FixtureRoot);
            Process = Start();
            Assert.True(Process.WaitForInputIdle(15000), "First instance never became ready");
            Assert.False(Process.HasExited, "First instance exited during startup");
        }
        catch { Dispose(); throw; }
    }

    public Process Process { get; private set; } = null!;
    public string Executable { get; }
    public string FixtureRoot => Path.Combine(_local, "fixtures");
    public JsonElement DownloadsSettings
    {
        get { using var json = JsonDocument.Parse(File.ReadAllText(SettingsPath)); return json.RootElement.GetProperty("modules").GetProperty("downloads-organizer").Clone(); }
    }
    public int LastActivationExitCode { get; private set; }
    private string SettingsPath => Path.Combine(_roaming, "settings.json");
    private string StartupName => "Chippi-dev-" + _profile;
    public string? StartupCommand
    {
        get { using var key = Registry.CurrentUser.OpenSubKey(RunKey); return key?.GetValue(StartupName) as string; }
    }
    public bool IslandEnabled
    {
        get
        {
            using var json = JsonDocument.Parse(File.ReadAllText(SettingsPath));
            return json.RootElement.GetProperty("app").GetProperty("islandEnabled").GetBoolean();
        }
    }

    private string ProfileDirectory(Environment.SpecialFolder folder) => Path.Combine(Environment.GetFolderPath(folder), "Chippi-dev", _profile);
    private Process Start()
    {
        var info = new ProcessStartInfo(Executable) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(Executable)! };
        info.Environment["CHIPPI_DEV_PROFILE"] = _profile;
        info.Environment.Remove("CHIPPI_DEBUG");
        var process = System.Diagnostics.Process.Start(info) ?? throw new InvalidOperationException("Launch failed");
        _children.Add(process);
        return process;
    }

    public AutomationElement OpenFlyout()
    {
        var activation = Start();
        Assert.True(activation.WaitForExit(15000), "Second launch did not exit");
        LastActivationExitCode = activation.ExitCode;
        Assert.Equal(0, LastActivationExitCode);
        AutomationElement? window = null;
        Until(() => (window = WindowOrNull("Chippi")) is not null, "profile flyout opened");
        return window!;
    }

    public AutomationElement OpenSettings()
    {
        Invoke(Find(OpenFlyout(), "Settings", InvokePattern.Pattern));
        AutomationElement? window = null;
        Until(() => (window = WindowOrNull("Chippi Settings")) is not null, "profile settings opened");
        return window!;
    }

    public AutomationElement? WindowOrNull(string name) => AutomationElement.RootElement.FindFirst(TreeScope.Children,
        new AndCondition(Named(name), new PropertyCondition(AutomationElement.ProcessIdProperty, Process.Id)));
    private static PropertyCondition Named(string name) => new(AutomationElement.NameProperty, name);

    public static AutomationElement Find(AutomationElement root, string name, AutomationPattern? pattern = null)
    {
        AutomationElement? found = null;
        Until(() =>
        {
            var matches = root.FindAll(TreeScope.Descendants, Named(name));
            found = matches.Cast<AutomationElement>().FirstOrDefault(e => pattern is null || e.TryGetCurrentPattern(pattern, out _));
            return found is not null;
        }, $"'{name}'{(pattern is null ? "" : " with " + pattern.ProgrammaticName)}");
        return found!;
    }

    public static void Until(Func<bool> condition, string description)
    {
        var timer = Stopwatch.StartNew();
        Exception? last = null;
        while (timer.Elapsed < TimeSpan.FromSeconds(15))
        {
            try { if (condition()) return; }
            catch (Exception ex) when (ex is ElementNotAvailableException or IOException) { last = ex; }
            Thread.Sleep(100);
        }
        throw new TimeoutException($"Timed out waiting for {description}", last);
    }

    public static TogglePattern Toggle(AutomationElement element) => (TogglePattern)element.GetCurrentPattern(TogglePattern.Pattern);
    public static void Invoke(AutomationElement element) => ((InvokePattern)element.GetCurrentPattern(InvokePattern.Pattern)).Invoke();
    public static void Select(AutomationElement root, string name) => ((SelectionItemPattern)Find(root, name, SelectionItemPattern.Pattern).GetCurrentPattern(SelectionItemPattern.Pattern)).Select();
    public static void ExpectAbsent(AutomationElement root, string name) => Until(
        () => root.FindFirst(TreeScope.Descendants, Named(name)) is null, $"'{name}' removed");

    public void PressEscape(AutomationElement settings)
    {
        // Send a real key only after verifying the foreground belongs to this profile's process.
        var handle = (nint)settings.Current.NativeWindowHandle;
        SetForegroundWindow(handle);
        Find(settings, "Keep awake", SelectionItemPattern.Pattern).SetFocus();
        Until(() => GetForegroundWindow() == handle, "test settings foreground for Escape");
        const byte escape = 0x1B;
        keybd_event(escape, 0, 0, 0);
        keybd_event(escape, 0, 2, 0);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        // Only exact Process objects started by this session may be stopped. Never kill by name/path.
        foreach (var child in _children)
        {
            try { if (!child.HasExited) { child.Kill(entireProcessTree: true); child.WaitForExit(10000); } }
            finally { child.Dispose(); }
        }
        bool notificationsCleaned = false;
        try
        {
            var info = new ProcessStartInfo(Executable) { UseShellExecute = false, CreateNoWindow = true };
            info.ArgumentList.Add("--cleanup-dev-notifications");
            info.ArgumentList.Add("--dev-profile");
            info.ArgumentList.Add(_profile);
            using var cleanup = System.Diagnostics.Process.Start(info) ?? throw new InvalidOperationException("Notification cleanup did not start");
            if (cleanup.WaitForExit(15000)) notificationsCleaned = cleanup.ExitCode == 0;
            else { cleanup.Kill(entireProcessTree: true); cleanup.WaitForExit(10000); }
        }
        catch (Exception ex) { _output.WriteLine($"Notification cleanup failed: {ex.Message}"); }
        using (var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true))
            key?.DeleteValue(StartupName, throwOnMissingValue: false);
        if (Directory.Exists(Path.Combine(_local, "logs")))
            foreach (var log in Directory.EnumerateFiles(Path.Combine(_local, "logs")))
                _output.WriteLine(File.ReadAllText(log));
        // These are GUID-specific paths constructed here, never a supplied or shared profile root.
        DeleteProfile(_roaming);
        DeleteProfile(_local);
        if (_foreground != 0) SetForegroundWindow(_foreground);
        Assert.Null(StartupCommand);
        Assert.True(notificationsCleaned, "Dev notification registration was not cleaned");
    }

    private void DeleteProfile(string path)
    {
        if (Path.GetFileName(path) != _profile || Path.GetFileName(Path.GetDirectoryName(path)) != "Chippi-dev")
            throw new InvalidOperationException("Refusing an unexpected cleanup path");
        if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
    }

    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetForegroundWindow(nint window);
    [DllImport("user32.dll")] private static extern void keybd_event(byte key, byte scan, uint flags, nuint extra);
}
