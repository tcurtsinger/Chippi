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
        DesktopSession.Find(flyout, "Settings", InvokePattern.Pattern);
        var quit = DesktopSession.Find(flyout, "Quit Chippi", InvokePattern.Pattern);
        Assert.False(app.Process.HasExited);
        Assert.Equal(0, app.LastActivationExitCode);
        Assert.DoesNotContain(flyout.FindAll(TreeScope.Descendants, Condition.TrueCondition).Cast<AutomationElement>(),
            e => e.Current.Name.Contains('{'));
        DesktopSession.Invoke(quit);
        Assert.True(app.Process.WaitForExit(15000), "Quit did not exit the profile process");
        Assert.Equal(0, app.Process.ExitCode);
    }

    [DesktopFact]
    public void SettingsTogglesChangeTheirRealBackingState()
    {
        using var app = new DesktopSession(output);
        var settings = app.OpenSettings();
        DesktopSession.Select(settings, "General");
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
    public void AddFeaturesExposesAvailableSwitchesAndComingLaterNames()
    {
        using var app = new DesktopSession(output);
        var settings = app.OpenSettings();
        DesktopSession.Select(settings, "Add features");
        var search = DesktopSession.Find(settings, "Search features", ValuePattern.Pattern);
        Assert.Equal(ToggleState.On, DesktopSession.Toggle(DesktopSession.Find(settings, "Keep awake", TogglePattern.Pattern)).Current.ToggleState);
        Assert.Equal(ToggleState.On, DesktopSession.Toggle(DesktopSession.Find(settings, "File organizer", TogglePattern.Pattern)).Current.ToggleState);
        DesktopSession.Find(settings, "Overlaps PowerToys");
        var later = DesktopSession.Find(settings, "Coming later", ExpandCollapsePattern.Pattern);
        var expand = (ExpandCollapsePattern)later.GetCurrentPattern(ExpandCollapsePattern.Pattern);
        Assert.Equal(ExpandCollapseState.Collapsed, expand.Current.ExpandCollapseState);
        expand.Expand();
        DesktopSession.Until(() => expand.Current.ExpandCollapseState == ExpandCollapseState.Expanded, "Coming later expanded");
        DesktopSession.Find(settings, "Audio routing");
        Assert.DoesNotContain(settings.FindAll(TreeScope.Descendants, Condition.TrueCondition).Cast<AutomationElement>(),
            e => e.Current.Name.Contains("Audio routing", StringComparison.Ordinal) && e.TryGetCurrentPattern(TogglePattern.Pattern, out _));
        Assert.DoesNotContain(settings.FindAll(TreeScope.Descendants, Condition.TrueCondition).Cast<AutomationElement>(),
            e => e.Current.Name.Contains('{'));
        ((ValuePattern)search.GetCurrentPattern(ValuePattern.Pattern)).SetValue("Audio routing");
        DesktopSession.Find(settings, "Audio routing");
        DesktopSession.ExpectAbsent(settings, "Overlaps PowerToys");
        ((ValuePattern)search.GetCurrentPattern(ValuePattern.Pattern)).SetValue("");
        DesktopSession.Find(settings, "Keep awake", TogglePattern.Pattern);
    }

    [DesktopFact]
    public void SidebarAndCloseReopenSettingsOnTheFirstFeature()
    {
        using var app = new DesktopSession(output);
        var settings = app.OpenSettings();
        AssertFirstFeature(settings);
        DesktopSession.Select(settings, "General");
        DesktopSession.Find(settings, "Run at startup", TogglePattern.Pattern);
        DesktopSession.Select(settings, "Keep awake");
        DesktopSession.Find(settings, "Keep awake", TogglePattern.Pattern);
        DesktopSession.ExpectAbsent(settings, "Back to Features");
        app.PressEscape(settings);
        DesktopSession.Until(() => app.WindowOrNull("Chippi Settings") is null, "Escape closed Settings");
        settings = app.OpenSettings();
        AssertFirstFeature(settings);
        DesktopSession.Select(settings, "Add features");
        DesktopSession.Find(settings, "Search features", ValuePattern.Pattern);
        ((WindowPattern)settings.GetCurrentPattern(WindowPattern.Pattern)).Close();
        DesktopSession.Until(() => app.WindowOrNull("Chippi Settings") is null, "settings closed");
        settings = app.OpenSettings();
        AssertFirstFeature(settings);
    }

    [DesktopFact]
    public void FlyoutTabsKeepTheirSelectionAndSettingsOpensTheFirstFeature()
    {
        using var app = new DesktopSession(output);
        var flyout = app.OpenFlyout();
        var tabs = DesktopSession.Find(flyout, "Flyout sections");
        DesktopSession.Select(tabs, "Keep awake");
        DesktopSession.Find(flyout, "Keep awake", TogglePattern.Pattern);
        DesktopSession.Select(tabs, "Tidy");
        DesktopSession.Find(flyout, "No folders yet");
        DesktopSession.Invoke(DesktopSession.Find(flyout, "Settings", InvokePattern.Pattern));
        AutomationElement? settings = null;
        DesktopSession.Until(() => (settings = app.WindowOrNull("Chippi Settings")) is not null, "settings opened");
        AssertFirstFeature(settings!);
        ((WindowPattern)settings!.GetCurrentPattern(WindowPattern.Pattern)).Close();
        flyout = app.OpenFlyout();
        var tidy = DesktopSession.Find(DesktopSession.Find(flyout, "Flyout sections"), "Tidy", SelectionItemPattern.Pattern);
        Assert.True(((SelectionItemPattern)tidy.GetCurrentPattern(SelectionItemPattern.Pattern)).Current.IsSelected);
        DesktopSession.Find(flyout, "No folders yet");
    }

    private static void AssertFirstFeature(AutomationElement settings)
    {
        var first = DesktopSession.Find(settings, "Keep awake", SelectionItemPattern.Pattern);
        Assert.True(((SelectionItemPattern)first.GetCurrentPattern(SelectionItemPattern.Pattern)).Current.IsSelected);
        DesktopSession.Find(settings, "Keep awake", TogglePattern.Pattern);
    }
}
