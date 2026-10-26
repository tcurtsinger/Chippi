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
    public void SecondLaunchOpensThePages()
    {
        using var app = new DesktopSession(output);
        var pages = app.OpenPages();
        Assert.False(pages.Current.IsOffscreen);
        DesktopSession.Find(pages, "Settings", InvokePattern.Pattern);
        DesktopSession.Find(pages, "Chippi"); // the grid's header
        Assert.False(app.Process.HasExited);
        Assert.Equal(0, app.LastActivationExitCode);
        Assert.DoesNotContain(pages.FindAll(TreeScope.Descendants, Condition.TrueCondition).Cast<AutomationElement>(),
            e => e.Current.Name.Contains('{'));
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
    public void PagesReopenOnTheLastPageAndSettingsOpensTheFirstFeature()
    {
        using var app = new DesktopSession(output);
        var pages = app.OpenPages();
        DesktopSession.Invoke(Tile(pages, "Keep awake"));
        DesktopSession.Find(pages, "1 hr", TogglePattern.Pattern);
        DesktopSession.Invoke(DesktopSession.Find(pages, "All features", InvokePattern.Pattern));
        DesktopSession.Invoke(Tile(pages, "File organizer"));
        DesktopSession.Find(pages, "No folders yet");
        DesktopSession.Invoke(DesktopSession.Find(pages, "Settings", InvokePattern.Pattern));
        AutomationElement? settings = null;
        DesktopSession.Until(() => (settings = app.WindowOrNull("Chippi Settings")) is not null, "settings opened");
        AssertFirstFeature(settings!);
        ((WindowPattern)settings!.GetCurrentPattern(WindowPattern.Pattern)).Close();
        pages = app.OpenPages();
        DesktopSession.Find(pages, "No folders yet"); // the last page used
    }

    /// <summary>A grid tile: its accessible name is the tile's name, then its status.</summary>
    internal static AutomationElement Tile(AutomationElement pages, string name)
    {
        AutomationElement? tile = null;
        DesktopSession.Until(() => (tile = pages.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.IsInvokePatternAvailableProperty, true))
            .Cast<AutomationElement>().FirstOrDefault(e => e.Current.Name == name || e.Current.Name.StartsWith(name + ",", StringComparison.Ordinal))) is not null, $"{name} tile");
        return tile!;
    }

    // The sidebar's first item: Files comes first, so File organizer.
    private static void AssertFirstFeature(AutomationElement settings)
    {
        var first = DesktopSession.Find(settings, "File organizer", SelectionItemPattern.Pattern);
        Assert.True(((SelectionItemPattern)first.GetCurrentPattern(SelectionItemPattern.Pattern)).Current.IsSelected);
    }
}
