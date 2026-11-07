// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Chippi.Core.Modules;
using Microsoft.Extensions.Logging.Abstractions;

namespace Chippi.Features.ScreenCapture.Tests;

public sealed class ScreenCaptureModelTests
{
    private sealed class SettingsStore : IModuleSettings
    {
        public ScreenCaptureSettings Value = new();
        public bool Fail;
        public T Get<T>() where T : class, new() => (T)(object)Value;
        public void Save<T>(T value) where T : class
        {
            if (Fail) throw new IOException();
            Value = (ScreenCaptureSettings)(object)value;
        }
    }

    [Fact]
    public void Model_preserves_failed_settings_and_reopens_flattened_recent_without_deleting_saved_file()
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            string folder = Path.Combine(Path.GetTempPath(), "chippi-model-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            try
            {
                var settings = new SettingsStore();
                var context = new ModuleContext("screen-capture", settings, NullLogger.Instance, null!, null!);
                using var store = new RecentCaptureStore(folder);
                var image = BitmapSource.Create(1920, 1080, 96, 96, PixelFormats.Bgra32, null, new byte[1920 * 1080 * 4], 1920 * 4);
                var id = Guid.NewGuid();
                string saved = Path.Combine(folder, "saved.png");
                File.WriteAllText(saved, "external file");
                store.Put(id, image, DateTimeOffset.Now, saved);
                BitmapSource? annotated = null;
                RecentCapture? recycled = null;
                using var model = new ScreenCaptureModel(context, store, pixels => annotated = pixels,
                    capture => { recycled = capture; return false; });
                Assert.True(model.Thumbnail(id)!.IsFrozen);
                Assert.True(model.SetEnter(EnterAction.CopyAndSave));
                settings.Fail = true;
                Assert.False(model.SetEnter(EnterAction.Save));
                Assert.Equal(EnterAction.CopyAndSave, model.Settings.Enter);
                Assert.Equal("Save failed", model.SettingsError);
                settings.Fail = false;
                Assert.True(model.SetSaveFolder(folder));
                Assert.False(model.SetSaveFolder("relative"));
                Assert.Equal(folder, model.SaveFolder);
                model.Annotate(id);
                Assert.Equal(1920, annotated!.PixelWidth);
                Assert.True(annotated.IsFrozen);
                Assert.False(model.DeleteFile(id));
                Assert.Equal(saved, recycled!.SavedFile);
                Assert.Single(model.Recent);
                using var result = new CaptureRecentModel(model, store, context);
                var opened = result.OpenAsync(id);
                Assert.NotNull(result.Result?.Thumbnail);
                opened.GetAwaiter().GetResult();
                Assert.Equal(1920, ((BitmapSource)result.Result!.Image!).PixelWidth);
                Assert.False(result.Result.FileMissing);
                File.Move(saved, saved + ".original");
                File.WriteAllText(saved, "replacement file");
                Assert.True(model.SetMagnifier(false));
                Assert.True(result.Result.FileMissing);
                model.Remove(id);
                Assert.Null(result.Result);
                Assert.Empty(model.Recent);
                Assert.Null(model.Thumbnail(id));
                Assert.Equal("replacement file", File.ReadAllText(saved));
            }
            catch (Exception ex) { error = ex; }
            finally { Directory.Delete(folder, true); }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (error != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
    }

    [Fact]
    public void Annotate_with_a_missing_or_damaged_retained_image_fails_and_keeps_the_island_and_entry()
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            string folder = Path.Combine(Path.GetTempPath(), "chippi-model-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            try
            {
                int closes = 0;
                var context = new ModuleContext("screen-capture", new SettingsStore(), NullLogger.Instance, null!, null!) { CloseIsland = () => closes++ };
                using var store = new RecentCaptureStore(folder);
                var image = BitmapSource.Create(64, 48, 96, 96, PixelFormats.Bgra32, null, new byte[64 * 48 * 4], 64 * 4);
                var id = Guid.NewGuid();
                store.Put(id, image, DateTimeOffset.Now, null);
                BitmapSource? annotated = null;
                using var model = new ScreenCaptureModel(context, store, pixels => annotated = pixels, _ => false);
                using var result = new CaptureRecentModel(model, store, context);
                // Removed before anything reads it: WPF's image cache would otherwise serve a decoded copy.
                string retained = Path.Combine(folder, model.Recent[0].Image);
                File.Delete(retained);
                result.OpenAsync(id).GetAwaiter().GetResult();
                Assert.NotNull(result.Result);
                Assert.Null(result.Result.Image);
                Assert.False(model.Annotate(id));
                Assert.False(result.Annotate());
                File.WriteAllText(retained, "not a png");
                Assert.False(model.Annotate(id));
                Assert.False(result.Annotate());

                Assert.Null(annotated);
                Assert.Equal(0, closes);
                Assert.Single(model.Recent);
                model.Remove(id);
                Assert.Empty(model.Recent);
            }
            catch (Exception ex) { error = ex; }
            finally { Directory.Delete(folder, true); }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (error != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
    }
}
