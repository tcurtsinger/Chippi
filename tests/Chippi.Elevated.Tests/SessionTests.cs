// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors
using System.ComponentModel;
using System.IO.Pipes;
using Chippi.Core.Admin;
using Chippi.Elevated.Protocol;
using Chippi.UI.Admin;
using Microsoft.Extensions.Logging.Abstractions;

namespace Chippi.Elevated.Tests;

public sealed class SessionTests
{
    [Fact]
    public async Task Batch_uses_one_session_and_continues_after_a_failure()
    {
        await using var factory = new Factory(r => Task.FromResult(new AdminReply(r.Id, r.PackageId == "a.b" ? "Failed" : "Done", r.PackageId, 0)));
        using var helper = new AdminHelper(factory, NullLogger.Instance);
        var observed = new List<AdminUpdateProgress>();
        var results = await helper.UpdatePackagesAsync(["a.b", "c.d", "A.B"], new Reports(observed));
        Assert.Equal(new[] { "Failed", "Done" }, results.Select(r => r.State));
        Assert.Equal(1, factory.Opens); Assert.Equal(new[] { "a.b", "c.d" }, factory.Requests);
        Assert.Equal(new[] { 1, 1, 2, 2 }, observed.Select(p => p.Index));
        Assert.Null(observed[0].Result);
        Assert.Equal("Failed", observed[1].Result!.State);
        Assert.Null(observed[2].Result);
        Assert.Equal("Done", observed[3].Result!.State);
        await factory.Finished.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Cancel_during_update_finishes_that_installer_and_skips_the_rest()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var factory = new Factory(async r => { started.SetResult(); await finish.Task; return new(r.Id, "Done", r.PackageId, 0); });
        using var helper = new AdminHelper(factory, NullLogger.Instance);
        using var cancel = new CancellationTokenSource();
        var batch = helper.UpdatePackagesAsync(["a.b", "c.d"], ct: cancel.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5)); cancel.Cancel();
        Assert.False(batch.IsCompleted); finish.SetResult();
        var results = await batch.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(new[] { "Done", "Cancelled" }, results.Select(r => r.State));
        Assert.Single(factory.Requests);
    }

    [Fact]
    public async Task Decline_and_missing_helper_never_retry()
    {
        var factory = new RefusingFactory(new Win32Exception(1223));
        using var helper = new AdminHelper(factory, NullLogger.Instance);
        Assert.All(await helper.UpdatePackagesAsync(["a.b", "c.d"]), r => Assert.Equal("NeedsAdmin", r.State));
        Assert.Equal(1, factory.Opens);
        var absent = new RefusingFactory(new FileNotFoundException());
        using var missing = new AdminHelper(absent, NullLogger.Instance);
        Assert.Equal("Failed", (await missing.UpdatePackagesAsync(["a.b"])).Single().State);
        Assert.Equal(1, absent.Opens);
    }

    [Fact]
    public async Task Invalid_or_cancelled_batches_never_open_a_session_and_dev_profiles_refuse()
    {
        var factory = new RefusingFactory(new Exception("Must not open"));
        using var helper = new AdminHelper(factory, NullLogger.Instance);
        await Assert.ThrowsAsync<ArgumentException>(() => helper.UpdatePackagesAsync(["a b"]));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => helper.UpdatePackagesAsync(["a.b"], ct: new CancellationToken(true)));
        Assert.Empty(await helper.UpdatePackagesAsync([])); Assert.Equal(0, factory.Opens);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => new WindowsAdminSession(false).OpenAsync(default));
    }

    [Fact]
    public async Task Mismatched_result_ends_the_batch_without_retry()
    {
        await using var factory = new Factory(r => Task.FromResult(new AdminReply(Guid.NewGuid(), "Done", r.PackageId, 0)));
        using var helper = new AdminHelper(factory, NullLogger.Instance);
        Assert.All(await helper.UpdatePackagesAsync(["a.b", "c.d"]), r => Assert.Equal("Failed", r.State));
        Assert.Single(factory.Requests); Assert.Equal(1, factory.Opens);
    }

    [Fact]
    public async Task Real_local_pipe_is_first_instance_only_and_identifies_the_client()
    {
        string name = "chippi-admin-" + Guid.NewGuid().ToString("N");
        using var server = LocalPipe.Create(name);
        Assert.ThrowsAny<Win32Exception>(() => LocalPipe.Create(name));
        using var client = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
        var accept = server.WaitForConnectionAsync(); await client.ConnectAsync(5000); await accept;
        Assert.Equal((uint)Environment.ProcessId, LocalPipe.ClientId(server.SafePipeHandle));
        Assert.Equal((uint)Environment.ProcessId, LocalPipe.ServerId(client.SafePipeHandle));
    }

    private sealed class RefusingFactory(Exception error) : IAdminSessionFactory
    {
        public int Opens;
        public Task<IAdminConnection> OpenAsync(CancellationToken ct) { Opens++; return Task.FromException<IAdminConnection>(error); }
    }
    private sealed class Reports(List<AdminUpdateProgress> target) : IProgress<AdminUpdateProgress>
    { public void Report(AdminUpdateProgress value) => target.Add(value); }
    private sealed class Updater(Func<AdminRequest, Task<AdminReply>> run) : IPackageUpdater
    { public Task<AdminReply> UpdateAsync(AdminRequest request) => run(request); }
    private sealed class Factory(Func<AdminRequest, Task<AdminReply>> update) : IAdminSessionFactory, IAsyncDisposable
    {
        public int Opens;
        public List<string> Requests { get; } = [];
        public Task Finished { get; private set; } = Task.CompletedTask;
        private NamedPipeClientStream? _client;
        private NamedPipeServerStream? _server;
        public async Task<IAdminConnection> OpenAsync(CancellationToken ct)
        {
            Opens++; string name = "chippi-test-" + Guid.NewGuid().ToString("N");
            _server = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
            _client = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
            var accept = _server.WaitForConnectionAsync(ct); await _client.ConnectAsync(ct); await accept;
            Finished = new HelperSession(new Updater(r => { Requests.Add(r.PackageId!); return update(r); })).RunAsync(_client);
            return new Connection(_server);
        }
        public async ValueTask DisposeAsync()
        {
            _server?.Dispose(); _client?.Dispose();
            try { await Finished; } catch (IOException) { }
        }
        private sealed class Connection(Stream stream) : IAdminConnection
        { public Stream Stream => stream; public ValueTask DisposeAsync() { stream.Dispose(); return ValueTask.CompletedTask; } }
    }
}
