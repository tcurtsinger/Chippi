// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.IO;
using System.Text.Json.Nodes;
using System.Windows.Automation;
using Xunit.Abstractions;

namespace Chippi.UiSmoke;

public sealed class IslandTests(ITestOutputHelper output)
{
    [DesktopFact]
    public void InvokeTogglesTheIslandAndItsFeatureViewsWork()
    {
        using var app = new DesktopSession(output, Seed);
        var (_, tab) = Tab(app);
        CloseIfOpen(app, tab);
        DesktopSession.Invoke(tab);
        var panel = Panel(app);
        DesktopSession.Find(panel, "Keep awake");
        DesktopSession.Find(panel, "island-inbox");
        DesktopSession.Until(() => panel.FindAll(TreeScope.Descendants, Condition.TrueCondition).Cast<AutomationElement>()
            .Any(e => e.Current.Name.StartsWith("Manual", StringComparison.Ordinal)), "island Tidy mode shown");
        foreach (string name in new[] { "30 min", "1 hr", "2 hr", "No limit" })
            DesktopSession.Find(panel, name, TogglePattern.Pattern);
        Assert.DoesNotContain(panel.FindAll(TreeScope.Descendants, Condition.TrueCondition).Cast<AutomationElement>(),
            e => e.Current.Name.Contains('{'));

        var hour = DesktopSession.Find(panel, "1 hr", TogglePattern.Pattern);
        Assert.Equal(ToggleState.Off, DesktopSession.Toggle(hour).Current.ToggleState);
        DesktopSession.Toggle(hour).Toggle();
        DesktopSession.Until(() => DesktopSession.Toggle(hour).Current.ToggleState == ToggleState.On, "island one-hour session started");
        DesktopSession.Until(() => panel.FindAll(TreeScope.Descendants, Condition.TrueCondition).Cast<AutomationElement>()
            .Any(e => e.Current.Name.StartsWith("On", StringComparison.Ordinal)), "island shows the running session");
        DesktopSession.Toggle(hour).Toggle();
        DesktopSession.Until(() => DesktopSession.Toggle(hour).Current.ToggleState == ToggleState.Off, "island session stopped");
        DesktopSession.Find(panel, "Off");

        DesktopSession.Invoke(tab);
        ExpectClosed(app);
        DesktopSession.Invoke(tab);
        panel = Panel(app);
        DesktopSession.Invoke(DesktopSession.Find(panel, "Organize now", InvokePattern.Pattern));
        AutomationElement? settings = null;
        DesktopSession.Until(() => (settings = app.WindowOrNull("Chippi Settings")) is not null, "island Tidy opens preview");
        DesktopSession.Find(settings!, "Include island.txt", TogglePattern.Pattern);
        Assert.True(File.Exists(Path.Combine(app.FixtureRoot, "island-inbox", "island.txt")));
    }

    [DesktopFact]
    public void EnterOpensAndClosesTheFocusedTab() => CheckKey(0x0D);

    [DesktopFact]
    public void SpaceOpensAndClosesTheFocusedTab() => CheckKey(0x20);

    private void CheckKey(byte key)
    {
        using var app = new DesktopSession(output, Seed);
        var (window, tab) = Tab(app);
        CloseIfOpen(app, tab);
        app.PressKey(window, tab, key);
        Panel(app);
        app.PressKey(window, tab, key);
        ExpectClosed(app);
    }

    private static (AutomationElement Window, AutomationElement Tab) Tab(DesktopSession app)
    {
        AutomationElement? window = null, tab = null;
        DesktopSession.Until(() =>
        {
            foreach (var candidate in Windows(app))
            {
                var button = candidate.FindFirst(TreeScope.Descendants, new AndCondition(
                    new PropertyCondition(AutomationElement.NameProperty, "Chippi"),
                    new PropertyCondition(AutomationElement.IsInvokePatternAvailableProperty, true)));
                if (button is null) continue;
                window = candidate;
                tab = button;
                return true;
            }
            return false;
        }, "profile island tab ready");
        return (window!, tab!);
    }

    private static AutomationElement[] Windows(DesktopSession app) => AutomationElement.RootElement.FindAll(TreeScope.Children,
        new AndCondition(new PropertyCondition(AutomationElement.ProcessIdProperty, app.Process.Id),
            new PropertyCondition(AutomationElement.NameProperty, "Chippi island")))
        .Cast<AutomationElement>().Where(e => !e.Current.IsOffscreen).ToArray();

    private static AutomationElement? PanelOrNull(DesktopSession app) => Windows(app).FirstOrDefault(w =>
        w.FindFirst(TreeScope.Descendants, new AndCondition(
            new PropertyCondition(AutomationElement.NameProperty, "Organize now"),
            new PropertyCondition(AutomationElement.IsInvokePatternAvailableProperty, true))) is not null);

    private static AutomationElement Panel(DesktopSession app)
    {
        AutomationElement? panel = null;
        DesktopSession.Until(() => (panel = PanelOrNull(app)) is not null, "profile island panel opened");
        return panel!;
    }

    private static void CloseIfOpen(DesktopSession app, AutomationElement tab)
    {
        if (PanelOrNull(app) is not null) DesktopSession.Invoke(tab);
        ExpectClosed(app);
    }

    private static void ExpectClosed(DesktopSession app) => DesktopSession.Until(() => PanelOrNull(app) is null, "profile island panel closed");

    private static void Seed(string path, string root)
    {
        string folder = Path.Combine(root, "island-inbox");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "island.txt"), "island smoke fixture");
        var config = JsonNode.Parse(File.ReadAllText(path))!;
        config["app"]!["islandEnabled"] = true;
        config["modules"]!["downloads-organizer"]!["folders"] = new JsonArray(new JsonObject
        {
            ["path"] = folder, ["mode"] = "manual", ["graceSeconds"] = 30,
            ["baselineUtc"] = DateTimeOffset.UtcNow.AddDays(-1).ToString("O")
        });
        config["modules"]!["downloads-organizer"]!["rules"] = new JsonArray(new JsonObject
        {
            ["id"] = "island-text", ["name"] = "Text", ["enabled"] = true, ["order"] = 0,
            ["extensions"] = new JsonArray("txt"), ["action"] = "move", ["template"] = "Sorted"
        });
        File.WriteAllText(path, config.ToJsonString());
    }
}
