// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Chippi.UI.Controls;
using Chippi.Island.Tests;

namespace Chippi.Features.CommandBar.Tests;

public sealed class AppIconTests
{
    [Theory]
    [InlineData(14, 128)]
    [InlineData(20, 128)]
    [InlineData(24, 128)]
    [InlineData(40, 128)]
    [InlineData(96, 128)]
    [InlineData(192, 256)]
    public void Small_surfaces_share_a_large_native_source(int displayPixels, int sourcePixels) =>
        Assert.Equal(sourcePixels, AppIcon.SourcePixels(displayPixels));

    [Theory]
    [InlineData("OpenAI.Codex_2p2nqsd0c76g0!App", "OpenAI.Codex_", true)]
    [InlineData("OpenAI.CodexPrimaryRuntime_abc!App", "OpenAI.Codex_", false)]
    [InlineData("com.squirrel.AnthropicClaude.claude", "com.squirrel.AnthropicClaude.claude", true)]
    [InlineData("com.squirrel.AnthropicClaude.claude-helper", "com.squirrel.AnthropicClaude.claude", false)]
    [InlineData("ChatGPT", "OpenAI.Codex_", false)]
    [InlineData("OpenAI.Codex_abc!App", "", false)]
    public void Installed_identity_matching_does_not_select_another_product(string identity, string prefix, bool matches) =>
        Assert.Equal(matches, WindowsAppIcons.Matches(identity, prefix));

    [Fact]
    public void Windows_reader_keeps_large_artwork_and_transparency() => WpfTestThread.Run(() =>
    {
        string folder = Path.Combine(Path.GetTempPath(), "Chippi-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, "transparent.ico");
        try
        {
            // A real Windows icon fixture with a native 256px PNG frame; no installed app or desktop needed.
            const int size = 256;
            byte[] pixels = new byte[size * size * 4];
            for (int y = 32; y < size - 32; y++)
                for (int x = 32; x < size - 32; x++)
                {
                    int at = (y * size + x) * 4;
                    pixels[at + 1] = 210;
                    pixels[at + 3] = 255;
                }
            var bitmap = BitmapSource.Create(size, size, 96, 96, PixelFormats.Bgra32, null, pixels, size * 4);
            var png = new PngBitmapEncoder();
            png.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = new MemoryStream();
            png.Save(stream);
            using (var output = new BinaryWriter(File.Create(path)))
            {
                output.Write((ushort)0); output.Write((ushort)1); output.Write((ushort)1);
                output.Write((byte)0); output.Write((byte)0); output.Write((byte)0); output.Write((byte)0);
                output.Write((ushort)1); output.Write((ushort)32);
                output.Write((uint)stream.Length); output.Write(22u); output.Write(stream.ToArray());
            }
            var image = AppIcon.Read(path, 20);
            Assert.NotNull(image);
            Assert.True(image.IsFrozen);
            Assert.InRange(image.PixelWidth, 128, 256);
            Assert.Equal(image.PixelWidth, image.PixelHeight);
            byte[] actual = new byte[image.PixelWidth * image.PixelHeight * 4];
            image.CopyPixels(actual, image.PixelWidth * 4, 0);
            Assert.Equal(0, actual[3]);
            int center = (image.PixelHeight / 2 * image.PixelWidth + image.PixelWidth / 2) * 4;
            Assert.Equal(255, actual[center + 3]);
            Assert.InRange(actual[center + 1], 205, 215);
            Assert.NotNull(AppIcon.Read(path.Replace('\\', '/'), 20));
        }
        finally { Directory.Delete(folder, recursive: true); }
    });
}
