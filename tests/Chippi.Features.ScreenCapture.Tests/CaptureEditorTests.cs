// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Chippi.Capture;
using Chippi.Core.Modules;
using Chippi.Island.Tests;
using Chippi.UI.Capture;
using Microsoft.Extensions.Logging.Abstractions;

namespace Chippi.Features.ScreenCapture.Tests;

public sealed class CaptureEditorTests
{
    [Fact]
    public void Real_tool_events_move_resize_erase_crop_and_commit_text_in_source_pixels() => WpfTestThread.Run(() =>
    {
        string folder = Path.Combine(Path.GetTempPath(), "chippi-editor-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            using var store = new RecentCaptureStore(folder);
            var context = new ModuleContext("screen-capture", new SettingsStore(), NullLogger.Instance, null!, null!);
            using var model = new ScreenCaptureModel(context, store, _ => { }, _ => false);
            var session = new Session();
            using var editor = new CaptureEditor(session, context, model, store, (_, _) => { });
            Point at = default;
            editor.PointerAt = (_, _) => at; // The owner's mouse never participates.
            session.Release();
            void Tool(string name) => Click(Find(session.Tools!, name));
            void Drag(Point from, Point to)
            {
                at = from;
                session.Canvas.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = UIElement.MouseLeftButtonDownEvent });
                at = to;
                session.Canvas.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = UIElement.MouseMoveEvent });
                session.Canvas.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = UIElement.MouseLeftButtonUpEvent });
            }
            Tool("Redact"); Drag(new(20, 20), new(80, 80));
            Assert.Single(editor.Draft!.Objects);
            Tool("Select"); Drag(new(40, 40), new(50, 50));
            Assert.Equal(new Point(30, 30), editor.Draft.Objects[0].Points[0]);
            Drag(new(90, 90), new(120, 130));
            Assert.Equal(new Point(120, 130), editor.Draft.Objects[0].Points[1]);
            var pixels = editor.Draft.Flatten();
            byte[] sample = new byte[4];
            pixels.CopyPixels(new Int32Rect(25, 25, 1, 1), sample, 4, 0);
            Assert.Equal(255, sample[2]); // Moving redaction doesn't leave its old pixels behind in this unsaved draft.
            Tool("Eraser"); Drag(new(50, 50), new(50, 50));
            Assert.Empty(editor.Draft.Objects);
            Tool("Text"); Drag(new(15, 15), new(15, 15));
            var text = Assert.Single(session.Canvas.Children.OfType<TextBox>());
            text.Text = "Source pixels";
            Tool("Pen");
            Assert.Equal("Source pixels", Assert.Single(editor.Draft.Objects).Text);
            Assert.Empty(session.Canvas.Children.OfType<TextBox>());
            Tool("Crop"); Drag(new(10, 10), new(110, 90));
            Click(Find(session.Output!, "Crop"));
            Assert.Equal(new Int32Rect(10, 10, 100, 80), editor.Draft.Crop);
            Click(Find(session.Tools!, "Undo"));
            Assert.Equal(new Int32Rect(0, 0, 400, 300), editor.Draft.Crop);
            Click(Find(session.Tools!, "Redo"));
            Assert.Equal(100, editor.Draft.Flatten().PixelWidth);
        }
        finally { Directory.Delete(folder, true); }
    });

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static Button Find(DependencyObject root, string name)
    {
        if (root is Button button && AutomationProperties.GetName(button) == name) return button;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            var found = FindOrNull(child, name);
            if (found != null) return found;
        }
        throw new InvalidOperationException("Missing button: " + name);
    }
    private static Button? FindOrNull(DependencyObject root, string name)
    {
        if (root is Button button && AutomationProperties.GetName(button) == name) return button;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            if (FindOrNull(child, name) is { } found) return found;
        return null;
    }
    private sealed class SettingsStore : IModuleSettings
    {
        public T Get<T>() where T : class, new() => new();
        public void Save<T>(T value) where T : class { }
    }
    private sealed class Session : ICaptureSession
    {
        public CaptureRequest Request { get; } = new(Chippi.Capture.CaptureMode.Region);
        public Canvas Canvas { get; } = new() { Width = 400, Height = 300 };
        public FrameworkElement? Tools, Output;
        public Freeze? Freeze { get; private set; }
        public Selection? Selection { get; private set; }
        public IReadOnlyList<CaptureSurface> Surfaces => [new("fixture", new(0, 0, 400, 300), 96, Canvas)];
        public bool Tooling { get; set; }
        public event Action<Selection>? Selected;
        public event Action<System.Windows.Input.KeyEventArgs>? KeyDown { add { } remove { } }
        public event Action<PixelPoint, uint>? Picked { add { } remove { } }
        public event Action? Frozen { add { } remove { } }
        public event Action<CaptureEnd>? Closed { add { } remove { } }
        public void SetBars(FrameworkElement? tools, FrameworkElement? output) { Tools = tools; Output = output; }
        public void Close(CaptureEnd end = CaptureEnd.Finished, bool handBack = true) { }
        public void Release()
        {
            var bytes = new byte[400 * 300 * 4];
            for (int i = 0; i < bytes.Length; i += 4) { bytes[i + 2] = 255; bytes[i + 3] = 255; }
            var screen = new CapturedScreen("fixture", 0, new(0, 0, 400, 300), 96, bytes);
            Freeze = new Freeze([screen], [], DateTimeOffset.Now);
            Selection = new(new(0, 0, 400, 300), Freeze, Chippi.Capture.CaptureMode.Region, null, null, false, screen);
            Selected?.Invoke(Selection);
        }
    }
}
