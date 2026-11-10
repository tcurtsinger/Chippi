// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Chippi.App.Island;
using Chippi.Core.Island;
using Chippi.UI.Pet;
using Chippi.UI.Themes;

namespace Chippi.Island.Tests;

[Collection("ReducedMotion")]
public sealed class RecordingPuckTests
{
    [Theory]
    [InlineData(false, false)] [InlineData(true, false)] [InlineData(false, true)] [InlineData(true, true)]
    public void Recording_wins_over_music_and_controls_invoke_without_opening_the_panel(bool opensLeft, bool stop) => WpfTestThread.Run(() =>
    {
        var motion = Motion.Override; Motion.Override = true;
        try
        {
            IslandWindowTests.WithShownIsland(window =>
            {
                window.SetPlacement(opensLeft ? .8 : .2, .8);
                window.CapturePointer = _ => true;
                int pauses = 0, stops = 0;
                window.SetMedia(new IslandPanelTests.FakeMedia { Sessions = [new("spotify", "Spotify", "Song", "Artist", true, true, true, true, true, true, TimeSpan.Zero, TimeSpan.FromMinutes(3), DateTimeOffset.Now, null)] });
                var recording = new IslandActivity("screen-recording", IslandActivityKind.Recording, "0:42", "Fixture")
                { OpensTile = "recording", Recording = new(false, true, () => pauses++, () => stops++) };
                window.SetActivities([recording, new("media", IslandActivityKind.Media, "Song", "Spotify")], PetMood.Listening);
                window.UpdateLayout();
                Assert.Equal(Visibility.Visible, window.RecordingLayer.Visibility); Assert.Equal(Visibility.Collapsed, window.Bar.Visibility);
                Assert.Equal(PetRecordingState.Recording, window.Pet.Recording); Assert.Equal("0:42", window.RecordingTime.Text);
                Assert.Equal(50, window.HomeButton.ActualWidth);
                var target = stop ? window.RecordingStop : window.RecordingPause;
                var point = target.PointToScreen(new Point(target.ActualWidth / 2, target.ActualHeight / 2));
                window.PointerAt = () => point;
                window.PuckButton.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = UIElement.PreviewMouseLeftButtonDownEvent });
                window.PuckButton.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = UIElement.PreviewMouseLeftButtonUpEvent });
                Assert.Equal(stop ? 0 : 1, pauses); Assert.Equal(stop ? 1 : 0, stops); Assert.False(window.IsIslandOpen);
                window.SetActivities([recording with { Recording = new(true, true, () => pauses++, () => stops++) }], PetMood.Idle); window.UpdateLayout();
                Assert.Equal("Resume recording", AutomationProperties.GetName(window.RecordingPause)); Assert.Equal(PetRecordingState.Paused, window.Pet.Recording);
                string? shots = Environment.GetEnvironmentVariable("CHIPPI_RECORDING_SHOTS");
                if (shots != null)
                {
                    Directory.CreateDirectory(shots);
                    var scene = new RenderTargetBitmap(536 * 3, 124 * 3, 288, 288, PixelFormats.Pbgra32); scene.Render(window.Root);
                    var at = window.PuckButton.TranslatePoint(new Point(), window.Root);
                    var image = new CroppedBitmap(scene, new Int32Rect((int)Math.Round(at.X * 3), (int)Math.Round(at.Y * 3), 256 * 3, 50 * 3));
                    using var output = File.Create(Path.Combine(shots, $"recording-puck-{opensLeft}-{stop}.png")); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image)); encoder.Save(output);
                }
                window.SetActivities([], PetMood.Idle); window.UpdateLayout(); Assert.Equal(PetRecordingState.None, window.Pet.Recording);
            });
        }
        finally { Motion.Override = motion; }
    });
}
