// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows.Automation;
using Xunit.Abstractions;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Chippi.UiSmoke;

// Desktop automation is opt-in: ordinary/headless dotnet test must not take the keyboard.
public sealed class DesktopFactAttribute : FactAttribute
{
    public DesktopFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("CHIPPI_UI_SMOKE") != "1")
            Skip = "Set CHIPPI_UI_SMOKE=1 on an unlocked Windows desktop.";
    }
}

public sealed class DesktopTests(ITestOutputHelper output)
{
    [DesktopFact]
    public void SecondLaunchOpensFlyout()
    {
        using var app = new DesktopSession(output);
        var flyout = app.OpenFlyout();
        Assert.False(flyout.Current.IsOffscreen);
        DesktopSession.Find(flyout, "Open settings", InvokePattern.Pattern);
        var quit = DesktopSession.Find(flyout, "Quit Chippi", InvokePattern.Pattern);
        Assert.False(app.Process.HasExited);
        Assert.Equal(0, app.LastActivationExitCode);
        DesktopSession.Invoke(quit);
        Assert.True(app.Process.WaitForExit(15000), "Quit did not exit the profile process");
        Assert.Equal(0, app.Process.ExitCode);
    }

    [DesktopFact]
    public void SettingsTogglesChangeTheirRealBackingState()
    {
        using var app = new DesktopSession(output);
        var settings = app.OpenSettings();
        var startup = DesktopSession.Find(settings, "Run at startup", TogglePattern.Pattern);
        Assert.Equal(ToggleState.Off, DesktopSession.Toggle(startup).Current.ToggleState);
        DesktopSession.Toggle(startup).Toggle();
        DesktopSession.Until(() => app.StartupCommand is not null, "profile startup entry created");
        Assert.Equal(ToggleState.On, DesktopSession.Toggle(startup).Current.ToggleState);
        Assert.Contains(app.Executable, app.StartupCommand!, StringComparison.OrdinalIgnoreCase);
        DesktopSession.Toggle(startup).Toggle();
        DesktopSession.Until(() => app.StartupCommand is null, "profile startup entry removed");
        Assert.Equal(ToggleState.Off, DesktopSession.Toggle(startup).Current.ToggleState);

        DesktopSession.Select(settings, "Island & pet");
        var island = DesktopSession.Find(settings, "Island", TogglePattern.Pattern);
        Assert.Equal(ToggleState.Off, DesktopSession.Toggle(island).Current.ToggleState);
        DesktopSession.Toggle(island).Toggle();
        DesktopSession.Until(() => app.IslandEnabled, "island enabled persisted");
        Assert.Equal(ToggleState.On, DesktopSession.Toggle(island).Current.ToggleState);
        DesktopSession.Toggle(island).Toggle();
        DesktopSession.Until(() => !app.IslandEnabled, "island disabled persisted");
        Assert.Equal(ToggleState.Off, DesktopSession.Toggle(island).Current.ToggleState);
    }

    [DesktopFact]
    public void HubExposesCardBadgeComingSoonAndInvoke()
    {
        using var app = new DesktopSession(output);
        var settings = app.OpenSettings();
        DesktopSession.Select(settings, "Features");
        var audio = DesktopSession.Find(settings, "Audio routing");
        Assert.Equal(ControlType.Group, audio.Current.ControlType);
        Assert.False(string.IsNullOrWhiteSpace(audio.Current.HelpText));
        DesktopSession.Find(audio, "From Hopper");
        DesktopSession.Find(audio, "Coming soon");
        var install = DesktopSession.Find(audio, "Install Audio routing");
        Assert.False(install.Current.IsEnabled);
        Assert.Contains("Coming soon", install.Current.HelpText);
        var nodes = settings.FindAll(TreeScope.Descendants, Condition.TrueCondition);
        Assert.DoesNotContain(nodes.Cast<AutomationElement>(), e => e.Current.Name.Contains("FeatureBadge {", StringComparison.Ordinal));
        var keepAwake = DesktopSession.Find(settings, "Keep awake settings", InvokePattern.Pattern);
        Assert.Equal(ControlType.Button, keepAwake.Current.ControlType);
        DesktopSession.Invoke(keepAwake);
        DesktopSession.Find(settings, "Back to Features", InvokePattern.Pattern);
    }

    [DesktopFact]
    public void BackEscapeSidebarAndCloseLeaveModulePage()
    {
        using var app = new DesktopSession(output);
        var settings = app.OpenSettings();
        DesktopSession.Select(settings, "Features");
        DesktopSession.OpenModule(settings);
        DesktopSession.Invoke(DesktopSession.Find(settings, "Back to Features", InvokePattern.Pattern));
        DesktopSession.ExpectHub(settings);

        DesktopSession.OpenModule(settings);
        app.PressEscape(settings);
        DesktopSession.ExpectHub(settings);
        Assert.False(settings.Current.IsOffscreen);

        DesktopSession.OpenModule(settings);
        DesktopSession.Select(settings, "General");
        DesktopSession.Find(settings, "Run at startup", TogglePattern.Pattern);
        DesktopSession.ExpectAbsent(settings, "Back to Features");
        DesktopSession.Select(settings, "Features");
        DesktopSession.ExpectHub(settings);

        DesktopSession.OpenModule(settings);
        ((WindowPattern)settings.GetCurrentPattern(WindowPattern.Pattern)).Close();
        DesktopSession.Until(() => app.WindowOrNull("Chippi Settings") is null, "settings closed");
        settings = app.OpenSettings();
        DesktopSession.Select(settings, "Features");
        DesktopSession.ExpectHub(settings);
    }
}
