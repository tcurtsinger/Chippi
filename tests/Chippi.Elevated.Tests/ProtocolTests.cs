// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors
using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using Chippi.Elevated.Protocol;

namespace Chippi.Elevated.Tests;

public sealed class ProtocolTests
{
    [Theory]
    [InlineData("Microsoft.PowerToys", true)] [InlineData("7zip.7zip", true)] [InlineData("C++.a_b-c", true)]
    [InlineData("", false)] [InlineData("-x", false)] [InlineData("a b", false)] [InlineData("a;b", false)]
    [InlineData("a..b", false)] [InlineData(".a", false)] [InlineData("a.", false)] [InlineData("é.app", false)]
    public void Package_ids_are_single_arguments(string id, bool allowed) => Assert.Equal(allowed, Messages.PackageIdAllowed(id));

    [Fact]
    public void Id_and_pipe_bounds()
    {
        Assert.False(Messages.PackageIdAllowed(new string('a', 129)));
        Assert.True(Messages.PipeNameAllowed("chippi-admin-" + Guid.NewGuid().ToString("N")));
        Assert.False(Messages.PipeNameAllowed(@"\\remote\pipe\anything"));
    }

    [Theory]
    [InlineData("{\"Id\":\"00000000-0000-0000-0000-000000000001\",\"Verb\":\"Run\",\"PackageId\":\"x\"}")]
    [InlineData("{\"Id\":\"00000000-0000-0000-0000-000000000001\",\"Verb\":\"Done\",\"Extra\":1}")]
    [InlineData("{\"Id\":\"00000000-0000-0000-0000-000000000001\",\"Verb\":\"Done\",\"Verb\":\"Done\"}")]
    [InlineData("{\"Verb\":\"Done\"}")]
    [InlineData("{\"Id\":\"00000000-0000-0000-0000-000000000001\",\"Verb\":\"Done\",\"PackageId\":\"x\"}")]
    [InlineData("not json")]
    public async Task Invalid_requests_are_rejected(string json)
    {
        using var frame = Frame(json);
        var error = await Record.ExceptionAsync(() => Messages.ReadRequestAsync(frame));
        Assert.True(error is InvalidDataException or JsonException, error?.ToString());
    }

    [Theory] [InlineData(-1)] [InlineData(0)] [InlineData(65537)]
    public async Task Frame_length_is_bounded_before_allocating(int length)
    {
        var bytes = new byte[4]; BinaryPrimitives.WriteInt32LittleEndian(bytes, length);
        using var stream = new MemoryStream(bytes);
        await Assert.ThrowsAsync<InvalidDataException>(() => Messages.ReadRequestAsync(stream));
    }

    [Fact]
    public async Task Round_trip_eof_and_truncated_frames()
    {
        using var stream = new MemoryStream();
        var request = new AdminRequest(Guid.NewGuid(), "UpdatePackage", "Microsoft.PowerToys");
        await Messages.WriteAsync(stream, request); stream.Position = 0;
        Assert.Equal(request, await Messages.ReadRequestAsync(stream));
        Assert.Null(await Messages.ReadRequestAsync(stream));
        stream.SetLength(stream.Length - 1); stream.Position = 0;
        await Assert.ThrowsAsync<EndOfStreamException>(() => Messages.ReadRequestAsync(stream));
    }

    [Fact]
    public void Upgrade_is_exact_machine_scope_and_never_install_or_a_shell()
    {
        var args = PackageUpdater.Arguments("7zip.7zip");
        Assert.Equal(new[] { "upgrade", "--id", "7zip.7zip", "--exact", "--source", "winget", "--scope", "machine", "--silent",
            "--accept-package-agreements", "--accept-source-agreements", "--disable-interactivity" }, args);
        Assert.Throws<ArgumentException>(() => PackageUpdater.Arguments("--all"));
    }

    [Fact]
    public async Task Bad_command_line_exits_without_elevating_or_connecting()
    {
        Assert.Equal(2, await Program.Main([]));
        Assert.Equal(2, await Program.Main(["--pipe", "bad"]));
        Assert.Equal(2, await Program.Main(["--pipe", "chippi-admin-" + Guid.NewGuid().ToString("N"), "--run", "anything"]));
    }

    private static MemoryStream Frame(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text); var frame = new byte[bytes.Length + 4];
        BinaryPrimitives.WriteInt32LittleEndian(frame, bytes.Length); bytes.CopyTo(frame, 4);
        return new(frame);
    }
}
