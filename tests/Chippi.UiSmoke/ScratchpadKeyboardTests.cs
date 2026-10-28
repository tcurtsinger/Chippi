// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using System.Windows.Automation;
using Xunit.Abstractions;

namespace Chippi.UiSmoke;

/// <summary>Opt-in desktop checks, build-only until an authorized live session. Only Scratchpad is installed.</summary>
public sealed class ScratchpadKeyboardTests(ITestOutputHelper output)
{
    [DesktopFact]
    public void Scratchpad_shortcuts_cycle_create_rename_and_leave_the_editor()
    {
        using var app = new DesktopSession(output, Seed);
        var panel = app.OpenPages();
        DesktopSession.Invoke(DesktopTests.Tile(panel, "Scratchpad"));
        AutomationElement Editor() => DesktopSession.Find(panel, "Note", ValuePattern.Pattern);
        AutomationElement Note(string name) => DesktopSession.Find(panel, name, SelectionItemPattern.Pattern);
        bool Selected(string name) => ((SelectionItemPattern)Note(name).GetCurrentPattern(SelectionItemPattern.Pattern)).Current.IsSelected;
        var editor = Editor();
        ((ValuePattern)editor.GetCurrentPattern(ValuePattern.Pattern)).SetValue("Invented keyboard fixture");
        Chord(app, panel, editor, 0x4E, 0x11); // Ctrl+N
        DesktopSession.Until(() => Selected("Note 2"), "Ctrl+N creates and selects Note 2");
        Chord(app, panel, Editor(), 0x09, 0x11); // Ctrl+Tab
        DesktopSession.Until(() => Selected("Note 1"), "Ctrl+Tab cycles to Note 1");
        Assert.Equal("Invented keyboard fixture", ((ValuePattern)Editor().GetCurrentPattern(ValuePattern.Pattern)).Current.Value);
        Chord(app, panel, Editor(), 0x09, 0x11, 0x10); // Ctrl+Shift+Tab
        DesktopSession.Until(() => Selected("Note 2"), "Ctrl+Shift+Tab cycles back");
        app.PressKey(panel, Editor(), 0x71); // F2
        var rename = DesktopSession.Find(panel, "Name", ValuePattern.Pattern);
        ((ValuePattern)rename.GetCurrentPattern(ValuePattern.Pattern)).SetValue("Cancelled title");
        app.PressKey(panel, rename, 0x1B);
        Assert.True(Selected("Note 2"));
        DesktopSession.ExpectAbsent(panel, "Name");
        app.PressKey(panel, Editor(), 0x71);
        rename = DesktopSession.Find(panel, "Name", ValuePattern.Pattern);
        ((ValuePattern)rename.GetCurrentPattern(ValuePattern.Pattern)).SetValue("Renamed note");
        app.PressKey(panel, rename, 0x0D);
        DesktopSession.Until(() => Selected("Renamed note"), "F2 then Enter renames");
        app.PressKey(panel, Note("Renamed note"), 0x25); // Left in the tab row
        DesktopSession.Until(() => Selected("Note 1"), "Left selects the previous note");
        app.PressKey(panel, Note("Note 1"), 0x27);
        DesktopSession.Until(() => Selected("Renamed note"), "Right selects the next note");
        editor = Editor();
        string before = ((ValuePattern)editor.GetCurrentPattern(ValuePattern.Pattern)).Current.Value;
        app.PressKey(panel, editor, 0x09);
        DesktopSession.Until(() => !Automation.Compare(AutomationElement.FocusedElement, editor), "Tab leaves the editor");
        Assert.Equal(before, ((ValuePattern)editor.GetCurrentPattern(ValuePattern.Pattern)).Current.Value);
        app.PressKey(panel, editor, 0x1B);
        DesktopSession.Until(() => app.WindowOrNull("Chippi") is not { } visible || visible.Current.IsOffscreen, "Esc from the note closes the island");
    }

    private static void Chord(DesktopSession app, AutomationElement window, AutomationElement target, byte key, params byte[] modifiers)
    {
        Assert.Equal(app.Process.Id, window.Current.ProcessId); Assert.Equal(app.Process.Id, target.Current.ProcessId);
        nint handle = (nint)window.Current.NativeWindowHandle;
        SetForegroundWindow(handle); target.SetFocus();
        DesktopSession.Until(() => GetForegroundWindow() == handle && Automation.Compare(AutomationElement.FocusedElement, target), "profile control focused before chord");
        var held = new List<byte>();
        try
        {
            foreach (byte modifier in modifiers)
            {
                Assert.Equal(handle, GetForegroundWindow());
                keybd_event(modifier, 0, 0, 0); held.Add(modifier);
            }
            Assert.Equal(handle, GetForegroundWindow());
            keybd_event(key, 0, 0, 0); keybd_event(key, 0, 2, 0);
        }
        finally { foreach (byte modifier in held.AsEnumerable().Reverse()) keybd_event(modifier, 0, 2, 0); }
    }
    private static void Seed(string path, string unused)
    {
        var config = JsonNode.Parse(File.ReadAllText(path))!;
        config["features"] = new JsonObject { ["preset"] = "custom", ["installed"] = new JsonArray("scratchpad") };
        config["app"]!["islandEnabled"] = true;
        config["app"]!["islandOpensOn"] = "allFeatures";
        File.WriteAllText(path, config.ToJsonString());
    }
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint hwnd);
    [DllImport("user32.dll")] private static extern void keybd_event(byte key, byte scan, uint flags, nuint extra);
}
