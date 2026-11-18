// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors
using System.Diagnostics;
using System.Text;
using Chippi.Elevated.Protocol;

namespace Chippi.Elevated.Tests;

/// <summary>Every test points the categories at a fixture tree it made under the temp folder; nothing here touches a real Windows folder.</summary>
public sealed class CategoryActionTests : IDisposable
{
    private readonly string _base = Path.Combine(Path.GetTempPath(), "chippi-cat-" + Guid.NewGuid().ToString("N"));
    private readonly DateTime _now = DateTime.UtcNow;
    private readonly List<string> _junctions = [];

    public CategoryActionTests() => Directory.CreateDirectory(_base);

    public void Dispose()
    {
        // Junctions first, as links only, so cleanup never walks into a target.
        foreach (var junction in _junctions) if (Directory.Exists(junction)) Directory.Delete(junction);
        if (Directory.Exists(_base)) Directory.Delete(_base, true);
    }

    private string Dir(string name) { string p = Path.Combine(_base, name); Directory.CreateDirectory(p); return p; }
    private static string File_(string folder, string name, int size = 100, DateTime? age = null)
    {
        string path = Path.Combine(folder, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new byte[size]);
        if (age is { } when) { File.SetLastWriteTimeUtc(path, when); File.SetCreationTimeUtc(path, when); }
        return path;
    }
    private static CategoryTarget T(string path) => new(path);
    private DateTime Old => _now - TimeSpan.FromDays(3);

    private CategoryActions Actions(string category, DateTime clockNow, params CategoryTarget[] targets) =>
        new(new Dictionary<string, IReadOnlyList<CategoryTarget>> { [category] = targets }, new Fixed(clockNow));
    private static AdminRequest Request(string verb, string category = Messages.WindowsTemp) => new(Guid.NewGuid(), verb, Category: category);
    private static AdminReply Run(CategoryActions actions, string verb, string category = Messages.WindowsTemp, CancellationToken ct = default) =>
        actions.Run(Request(verb, category), ct);

    private string Junction(string name, string target)
    {
        string link = Path.Combine(_base, name);
        using var p = Process.Start(new ProcessStartInfo("cmd.exe")
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
            Arguments = $"/c mklink /J \"{link}\" \"{target}\"",
        })!;
        p.WaitForExit();
        Assert.Equal(0, p.ExitCode);
        _junctions.Add(link);
        return link;
    }

    [Fact]
    public void Old_files_are_deleted_and_new_files_kept()
    {
        string root = Dir("temp");
        string fresh = File_(root, "fresh.tmp");
        string stale = File_(root, "stale.tmp", 300, Old);
        string writtenOldCreatedNew = File_(root, "mixed.tmp"); File.SetLastWriteTimeUtc(writtenOldCreatedNew, Old);
        string createdOldWrittenNew = File_(root, "mixed2.tmp"); File.SetCreationTimeUtc(createdOldWrittenNew, Old);
        var reply = Run(Actions(Messages.WindowsTemp, _now + TimeSpan.FromHours(1), T(root)), Messages.CleanCategory);
        Assert.Equal((Messages.CategoryCleaned, 300L, 1, 0), (reply.State, reply.Bytes, reply.Files, reply.InUse));
        Assert.Equal(Messages.WindowsTemp, reply.Category);
        Assert.False(File.Exists(stale));
        Assert.True(File.Exists(fresh)); Assert.True(File.Exists(writtenOldCreatedNew)); Assert.True(File.Exists(createdOldWrittenNew));
        Assert.True(Directory.Exists(root));
    }

    [Fact]
    public void Measure_deletes_nothing_and_sums_sizes_and_counts_across_folders_and_targets()
    {
        string a = Dir("a"), b = Dir("b");
        File_(a, "one", 1000, Old); File_(a, @"sub\two", 2000, Old); File_(a, @"sub\deeper\three", 4000, Old); File_(a, "new", 8000);
        File_(b, "four", 16000, Old);
        var actions = Actions(Messages.SystemCrashDumps, _now + TimeSpan.FromHours(1), new(a), T(b));
        var reply = Run(actions, Messages.MeasureCategory, Messages.SystemCrashDumps);
        Assert.Equal((Messages.CategoryMeasured, 23000L, 4, 0), (reply.State, reply.Bytes, reply.Files, reply.InUse));
        Assert.Equal(5, Directory.EnumerateFiles(_base, "*", SearchOption.AllDirectories).Count());
        // A clean selects exactly what the measure said.
        var clean = Run(actions, Messages.CleanCategory, Messages.SystemCrashDumps);
        Assert.Equal((23000L, 4), (clean.Bytes, clean.Files));
        Assert.Single(Directory.EnumerateFiles(_base, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public void A_file_held_open_is_skipped_and_counted_never_forced()
    {
        string root = Dir("temp");
        string held = File_(root, "held.tmp", 500, Old), free = File_(root, "free.tmp", 50, Old);
        FileStream? hold = new(held, FileMode.Open, FileAccess.Read, FileShare.Read);
        try
        {
            var reply = Run(Actions(Messages.WindowsTemp, _now + TimeSpan.FromHours(1), T(root)), Messages.CleanCategory);
            Assert.Equal((50L, 1, 1), (reply.Bytes, reply.Files, reply.InUse));
            Assert.True(File.Exists(held)); Assert.False(File.Exists(free));
        }
        finally { hold.Dispose(); }
    }

    [Fact]
    public void A_read_only_file_is_cleaned()
    {
        string root = Dir("temp");
        string path = File_(root, "ro.tmp", 10, Old); File.SetAttributes(path, FileAttributes.ReadOnly);
        var reply = Run(Actions(Messages.WindowsTemp, _now + TimeSpan.FromHours(1), T(root)), Messages.CleanCategory);
        Assert.Equal(1, reply.Files);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void A_junction_inside_is_removed_as_a_link_and_its_target_is_untouched()
    {
        string root = Dir("temp"), outside = Dir("outside");
        string precious = File_(outside, "precious.dat", 777, Old);
        string sub = File_(outside, @"nested\deep.dat", 5, Old);
        Junction(@"temp\link", outside);
        // Two days on, everything counts as old: following the link would delete the target's files.
        var measured = Run(Actions(Messages.WindowsTemp, _now + TimeSpan.FromDays(2), T(root)), Messages.MeasureCategory);
        Assert.Equal((0L, 0), (measured.Bytes, measured.Files));
        var reply = Run(Actions(Messages.WindowsTemp, _now + TimeSpan.FromDays(2), T(root)), Messages.CleanCategory);
        Assert.Equal((Messages.CategoryCleaned, 0L, 0), (reply.State, reply.Bytes, reply.Files));
        Assert.False(Directory.Exists(Path.Combine(root, "link")));
        Assert.True(File.Exists(precious)); Assert.True(File.Exists(sub));
        Assert.True(Directory.Exists(root));
    }

    [Fact]
    public void A_junction_younger_than_a_day_stays_and_is_not_followed()
    {
        string root = Dir("temp"), outside = Dir("outside");
        string precious = File_(outside, "precious.dat", 777, Old);
        Junction(@"temp\link", outside);
        var reply = Run(Actions(Messages.WindowsTemp, _now + TimeSpan.FromHours(1), T(root)), Messages.CleanCategory);
        Assert.Equal(0, reply.Files);
        Assert.True(Directory.Exists(Path.Combine(root, "link"))); Assert.True(File.Exists(precious));
    }

    [Fact]
    public void A_category_root_that_is_a_link_or_missing_is_unavailable_and_nothing_is_touched()
    {
        string outside = Dir("outside");
        string precious = File_(outside, "precious.dat", 5, Old);
        string link = Junction("root-link", outside);
        foreach (string verb in new[] { Messages.MeasureCategory, Messages.CleanCategory })
        {
            Assert.Equal(Messages.CategoryUnavailable, Run(Actions(Messages.WindowsTemp, _now + TimeSpan.FromDays(2), T(link)), verb).State);
            Assert.Equal(Messages.CategoryUnavailable, Run(Actions(Messages.WindowsTemp, _now + TimeSpan.FromDays(2), T(Path.Combine(_base, "missing"))), verb).State);
        }
        Assert.True(File.Exists(precious));
    }

    [Fact]
    public void A_single_file_target_follows_the_same_rule_and_an_absent_one_is_skipped()
    {
        string dumps = Dir("dumps");
        string dump = File_(_base, "MEMORY.DMP", 9000, Old);
        string minidump = File_(dumps, "a.dmp", 100, Old);
        var targets = new CategoryTarget[] { new(dumps), new(dump, IsFile: true), new(Path.Combine(_base, "absent.DMP"), IsFile: true) };
        var measured = Run(Actions(Messages.SystemCrashDumps, _now + TimeSpan.FromHours(1), targets), Messages.MeasureCategory, Messages.SystemCrashDumps);
        Assert.Equal((9100L, 2), (measured.Bytes, measured.Files));
        Assert.True(File.Exists(dump));
        var fresh = Run(Actions(Messages.SystemCrashDumps, _now - TimeSpan.FromDays(5), targets), Messages.CleanCategory, Messages.SystemCrashDumps);
        Assert.Equal(0, fresh.Files);   // nothing is a day old yet
        Assert.True(File.Exists(dump));
        var reply = Run(Actions(Messages.SystemCrashDumps, _now + TimeSpan.FromHours(1), targets), Messages.CleanCategory, Messages.SystemCrashDumps);
        Assert.Equal((9100L, 2), (reply.Bytes, reply.Files));
        Assert.False(File.Exists(dump)); Assert.False(File.Exists(minidump)); Assert.True(Directory.Exists(dumps));
    }

    [Fact]
    public void A_single_file_target_that_is_a_folder_or_all_targets_missing_is_unavailable()
    {
        string folder = Dir("MEMORY.DMP");
        File_(folder, "inside", 10, Old);
        var reply = Run(Actions(Messages.SystemCrashDumps, _now + TimeSpan.FromDays(2), new CategoryTarget(folder, IsFile: true)), Messages.CleanCategory, Messages.SystemCrashDumps);
        Assert.Equal(Messages.CategoryUnavailable, reply.State);
        Assert.True(File.Exists(Path.Combine(folder, "inside")));
    }

    [Fact]
    public void Folders_the_clean_emptied_go_but_the_root_and_folders_with_new_files_stay()
    {
        string root = Dir("temp");
        File_(root, @"gone\inner\old.tmp", 10, Old);
        File_(root, @"stays\new.tmp");
        File_(root, @"stays\old.tmp", 10, Old);
        Directory.CreateDirectory(Path.Combine(root, "empty-new"));
        var reply = Run(Actions(Messages.WindowsTemp, _now + TimeSpan.FromHours(1), T(root)), Messages.CleanCategory);
        Assert.Equal(2, reply.Files);
        Assert.False(Directory.Exists(Path.Combine(root, "gone")));
        Assert.True(File.Exists(Path.Combine(root, "stays", "new.tmp")));
        Assert.True(Directory.Exists(Path.Combine(root, "empty-new")));   // a new folder isn't swept
        // Two days on, the rest of the contents go but the root itself never does; a folder that was already empty is left alone.
        Run(Actions(Messages.WindowsTemp, _now + TimeSpan.FromDays(2), T(root)), Messages.CleanCategory);
        Assert.True(Directory.Exists(root));
        Assert.Equal([Path.Combine(root, "empty-new")], Directory.EnumerateFileSystemEntries(root));
    }

    [Fact]
    public void Cancelling_stops_before_the_next_file_and_reports_what_was_freed()
    {
        string root = Dir("temp");
        for (int i = 0; i < 20; i++) File_(root, $"f{i}.tmp", 10, Old);
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        var reply = Run(Actions(Messages.WindowsTemp, _now + TimeSpan.FromHours(1), T(root)), Messages.CleanCategory, ct: cancel.Token);
        Assert.Equal((Messages.CategoryCleaned, Messages.CancelledReason, 0), (reply.State, reply.Reason, reply.Files));
        Assert.Equal(20, Directory.GetFiles(root).Length);
    }

    [Fact]
    public async Task A_category_the_map_does_not_hold_or_a_bad_request_is_failed_without_touching_anything()
    {
        string root = Dir("temp"); string file = File_(root, "x", 1, Old);
        var actions = Actions(Messages.WindowsTemp, _now + TimeSpan.FromDays(2), T(root));
        Assert.Equal(Messages.CategoryFailed, Run(actions, Messages.CleanCategory, Messages.DeliveryOptimization).State);
        Assert.Equal(Messages.CategoryFailed, (await actions.RunAsync(new AdminRequest(Guid.NewGuid(), "UpdatePackage", "a.b", "1"), default)).State);
        Assert.Equal(Messages.CategoryFailed, (await actions.RunAsync(new AdminRequest(Guid.NewGuid(), Messages.CleanCategory, Category: @"C:\Windows"), default)).State);
        Assert.True(File.Exists(file));
    }

    [Fact]
    public void The_shipped_map_names_the_four_fixed_categories_in_the_windows_folders()
    {
        var map = CategoryRoots.ForWindows();
        Assert.Equal(Messages.Categories.Order(), map.Keys.Order());
        string windows = Path.GetDirectoryName(Environment.SystemDirectory)!;
        Assert.Equal(Path.Combine(windows, "Temp"), map[Messages.WindowsTemp].Single().Path);
        Assert.Equal(Path.Combine(windows, "SoftwareDistribution", "Download"), map[Messages.WindowsUpdateDownloads].Single().Path);
        Assert.EndsWith(Path.Combine("ServiceProfiles", "NetworkService", "AppData", "Local", "Microsoft", "Windows", "DeliveryOptimization", "Cache"), map[Messages.DeliveryOptimization].Single().Path);
        var dumps = map[Messages.SystemCrashDumps];
        Assert.Equal(4, dumps.Count);
        Assert.Equal(Path.Combine(windows, "MEMORY.DMP"), Assert.Single(dumps, t => t.IsFile).Path);
        Assert.Contains(dumps, t => t.Path == Path.Combine(windows, "Minidump"));
        Assert.Contains(dumps, t => t.Path.EndsWith(Path.Combine("Windows", "WER", "ReportArchive"), StringComparison.OrdinalIgnoreCase));
        Assert.Contains(dumps, t => t.Path.EndsWith(Path.Combine("Windows", "WER", "ReportQueue"), StringComparison.OrdinalIgnoreCase));
    }

    private sealed class Fixed(DateTime utc) : TimeProvider { public override DateTimeOffset GetUtcNow() => new(utc, TimeSpan.Zero); }
}

public sealed class CategoryProtocolTests
{
    private static AdminRequest Valid(string verb = Messages.CleanCategory, string category = "WindowsTemp") => new(Guid.NewGuid(), verb, Category: category);

    [Fact]
    public void Only_the_four_names_are_allowed_on_the_category_verbs()
    {
        foreach (string name in Messages.Categories)
        {
            Assert.True(Messages.RequestAllowed(Valid(Messages.MeasureCategory, name)));
            Assert.True(Messages.RequestAllowed(Valid(Messages.CleanCategory, name)));
        }
        Assert.False(Messages.RequestAllowed(Valid(category: "Windows")));
        Assert.False(Messages.RequestAllowed(Valid(category: "windowstemp")));
        Assert.False(Messages.RequestAllowed(Valid(category: @"C:\Windows\Temp")));
        Assert.False(Messages.RequestAllowed(Valid(category: "")));
        Assert.False(Messages.RequestAllowed(Valid() with { Category = null }));
        Assert.False(Messages.RequestAllowed(Valid() with { PackageId = "a.b" }));
        Assert.False(Messages.RequestAllowed(Valid() with { Pid = 4 }));
        Assert.False(Messages.RequestAllowed(Valid() with { ServiceName = "x" }));
        Assert.False(Messages.RequestAllowed(Valid() with { Id = Guid.Empty }));
    }

    [Fact]
    public void A_category_name_does_not_ride_on_other_verbs()
    {
        Assert.False(Messages.RequestAllowed(new AdminRequest(Guid.NewGuid(), "Done", Category: "WindowsTemp")));
        Assert.False(Messages.RequestAllowed(new AdminRequest(Guid.NewGuid(), "UpdatePackage", "a.b", "1.0", Category: "WindowsTemp")));
        Assert.False(Messages.RequestAllowed(new AdminRequest(Guid.NewGuid(), Messages.EndProcess, Protocol: "Tcp", LocalAddress: "::1", LocalPort: 1, Pid: 2, StartTime: 3, Category: "WindowsTemp")));
        Assert.False(Messages.RequestAllowed(new AdminRequest(Guid.NewGuid(), Messages.StopService, Protocol: "Tcp", LocalAddress: "::1", LocalPort: 1, Pid: 2, StartTime: 3, ServiceName: "x", Category: "WindowsTemp")));
    }

    [Theory]
    [InlineData("{\"Id\":\"00000000-0000-0000-0000-000000000001\",\"Verb\":\"CleanCategory\",\"Category\":\"Everything\"}")]
    [InlineData("{\"Id\":\"00000000-0000-0000-0000-000000000001\",\"Verb\":\"CleanCategory\",\"Category\":\"WindowsTemp\",\"Path\":\"C:\\\\\"}")]
    [InlineData("{\"Id\":\"00000000-0000-0000-0000-000000000001\",\"Verb\":\"CleanCategory\",\"Category\":\"WindowsTemp\",\"Category\":\"WindowsTemp\"}")]
    [InlineData("{\"Id\":\"00000000-0000-0000-0000-000000000001\",\"Verb\":\"CleanCategory\"}")]
    public async Task Invalid_category_frames_end_the_session(string json)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        var frame = new MemoryStream();
        frame.Write(BitConverter.GetBytes(bytes.Length)); frame.Write(bytes); frame.Position = 0;
        var error = await Record.ExceptionAsync(() => Messages.ReadRequestAsync(frame));
        Assert.True(error is InvalidDataException or System.Text.Json.JsonException, error?.ToString());
    }

    [Fact]
    public async Task Category_messages_round_trip()
    {
        var stream = new MemoryStream();
        var request = Valid(Messages.MeasureCategory, "SystemCrashDumps");
        var reply = new AdminReply(request.Id, Messages.CategoryMeasured, Category: "SystemCrashDumps", Bytes: 5_000_000_000, Files: 12, InUse: 1);
        await Messages.WriteAsync(stream, request); await Messages.WriteAsync(stream, reply); stream.Position = 0;
        Assert.Equal(request, await Messages.ReadRequestAsync(stream));
        Assert.Equal(reply, await Messages.ReadReplyAsync(stream));
    }
}
