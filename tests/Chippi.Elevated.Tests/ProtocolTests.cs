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
        var request = new AdminRequest(Guid.NewGuid(), "UpdatePackage", "Microsoft.PowerToys", "1.2.3");
        await Messages.WriteAsync(stream, request); stream.Position = 0;
        Assert.Equal(request, await Messages.ReadRequestAsync(stream));
        Assert.Null(await Messages.ReadRequestAsync(stream));
        stream.SetLength(stream.Length - 1); stream.Position = 0;
        await Assert.ThrowsAsync<EndOfStreamException>(() => Messages.ReadRequestAsync(stream));
    }

    [Fact]
    public void Upgrade_is_exact_machine_scope_and_never_install_or_a_shell()
    {
        var args = PackageUpdater.Arguments("7zip.7zip", "24.09", false);
        Assert.Equal(new[] { "upgrade", "--id", "7zip.7zip", "--version", "24.09", "--exact", "--source", "winget", "--scope", "machine", "--silent",
            "--accept-package-agreements", "--accept-source-agreements", "--disable-interactivity" }, args);
        Assert.Throws<ArgumentException>(() => PackageUpdater.Arguments("--all", "1.0", false));
        Assert.Throws<ArgumentException>(() => PackageUpdater.Arguments("a.b", "--all", false));
    }

    [Fact]
    public void Unknown_installed_versions_alone_get_include_unknown()
    {
        Assert.Contains("--include-unknown", PackageUpdater.Arguments("a.b", "2.0", true));
        Assert.DoesNotContain("--include-unknown", PackageUpdater.Arguments("a.b", "2.0", false));
    }

    [Theory] [InlineData("1.2.3", true)] [InlineData("2024.1.0-beta+7", true)] [InlineData("", false)] [InlineData("-x", false)]
    [InlineData("1 2", false)] [InlineData("1.0 --all", false)]
    public void Pinned_versions_are_plain_tokens(string version, bool allowed) => Assert.Equal(allowed, Messages.VersionAllowed(version));

    [Fact]
    public void An_update_request_needs_its_reviewed_version_and_done_carries_none()
    {
        Assert.False(Messages.RequestAllowed(new(Guid.NewGuid(), "UpdatePackage", "a.b")));
        Assert.True(Messages.RequestAllowed(new(Guid.NewGuid(), "UpdatePackage", "a.b", "1.0")));
        Assert.False(Messages.RequestAllowed(new(Guid.NewGuid(), "Done", null, "1.0")));
        Assert.False(Messages.RequestAllowed(new(Guid.NewGuid(), "Done", null, null, true)));
    }

    [Fact]
    public void A_version_that_is_gone_reads_as_version_changed()
    {
        var request = new AdminRequest(Guid.NewGuid(), "UpdatePackage", "a.b", "2.0");
        Assert.Equal("Done", PackageUpdater.Result(request, 0).State);
        var changed = PackageUpdater.Result(request, PackageUpdater.NoPackageFound);
        Assert.Equal(("Failed", "Version changed"), (changed.State, changed.Reason));
        Assert.Equal("Package update failed", PackageUpdater.Result(request, 1).Reason);
    }

    [Fact]
    public void The_newest_app_installer_wins_by_version_not_by_text()
    {
        var old = PackageUpdater.ParseFullName("Microsoft.DesktopAppInstaller_1.9.100.0_x64__8wekyb3d8bbwe")!.Value;
        var newer = PackageUpdater.ParseFullName("Microsoft.DesktopAppInstaller_1.10.5.0_x64__8wekyb3d8bbwe")!.Value;
        Assert.True(newer.Version > old.Version);
        Assert.Equal("x64", newer.Architecture);
        Assert.Null(PackageUpdater.ParseFullName("garbage"));
        Assert.True(PackageUpdater.ArchitectureMatches("neutral"));
        Assert.False(PackageUpdater.ArchitectureMatches("mips"));
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
