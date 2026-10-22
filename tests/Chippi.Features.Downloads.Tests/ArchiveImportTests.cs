// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Formats.Tar;
using System.IO.Compression;
using Chippi.Features.Downloads.Import;
using Chippi.Features.Downloads.Model;

namespace Chippi.Features.Downloads.Tests;

public sealed class ArchiveImportTests
{
    private static string Zip(Fixture f, params (string Name, string Content)[] entries)
    {
        string path = Path.Combine(f.Root, "in.zip");
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var (name, content) in entries)
        {
            using var writer = new StreamWriter(zip.CreateEntry(name).Open());
            writer.Write(content);
        }
        return path;
    }

    private static string Staging(Fixture f) => Path.Combine(f.Root, "data", "imports");

    [Fact]
    public void Zip_is_flattened_with_unique_names_and_traversal_removed()
    {
        using var f = new Fixture(FolderMode.Paused);
        f.Write("taken.txt");
        string zip = Zip(f, ("a/one.txt", "1"), ("b/one.txt", "2"), ("../../evil.txt", "3"), ("taken.txt", "4"), ("dir/", ""));

        var result = ArchiveImporter.Extract(zip, f.Folder, Staging(f));

        Assert.Equal(["one.txt", "one (2).txt", "evil.txt", "taken (2).txt"], result.Files.Select(Path.GetFileName));
        Assert.All(result.Files, p => Assert.Equal(result.Staging, Path.GetDirectoryName(p)));
        Assert.False(File.Exists(Path.Combine(f.Root, "evil.txt")));
    }

    [Fact]
    public void Tar_gz_extracts_regular_files_and_skips_links()
    {
        using var f = new Fixture(FolderMode.Paused);
        string path = Path.Combine(f.Root, "in.tar.gz");
        using (var file = File.Create(path))
        using (var gzip = new GZipStream(file, CompressionLevel.Fastest))
        using (var tar = new TarWriter(gzip, TarEntryFormat.Pax))
        {
            tar.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, "docs/readme.md") { DataStream = new MemoryStream("hi"u8.ToArray()) });
            tar.WriteEntry(new PaxTarEntry(TarEntryType.SymbolicLink, "link") { LinkName = "/etc/passwd" });
        }

        var result = ArchiveImporter.Extract(path, f.Folder, Staging(f));

        Assert.Equal(["readme.md"], result.Files.Select(Path.GetFileName));
        Assert.Equal(["link"], result.Skipped);
    }

    [Fact]
    public void Too_many_entries_is_refused_and_leaves_nothing_behind()
    {
        using var f = new Fixture(FolderMode.Paused);
        string zip = Zip(f, ("a.txt", "1"), ("b.txt", "2"), ("c.txt", "3"));

        Assert.Throws<ArchiveRejectedException>(() =>
            ArchiveImporter.Extract(zip, f.Folder, Staging(f), new ArchiveLimits { MaxEntries = 2 }));
        Assert.Empty(Directory.GetDirectories(Staging(f)));
    }

    [Fact]
    public void An_archive_that_expands_too_far_is_refused()
    {
        using var f = new Fixture(FolderMode.Paused);
        string zip = Zip(f, ("zeros.txt", new string('0', 2_000_000))); // compresses to a few KB

        var ex = Assert.Throws<ArchiveRejectedException>(() => ArchiveImporter.Extract(zip, f.Folder, Staging(f)));
        Assert.Contains("100:1", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Import_goes_through_preview_and_cleans_up_staging()
    {
        using var f = new Fixture(FolderMode.Manual);
        f.AddRule(new RuleSetting { Extensions = ["pdf"], Action = RuleActionKind.Move, Template = "Docs" });
        string zip = Zip(f, ("x/a.pdf", "pdf"), ("notes.txt", "txt"));
        var extracted = ArchiveImporter.Extract(zip, f.Folder, Staging(f));

        var preview = await f.Engine.PreviewImportAsync(f.Folder, extracted);
        Assert.Equal(2, preview.Items.Count);
        Assert.True(File.Exists(Path.Combine(extracted.Staging, "a.pdf"))); // nothing moved yet

        var result = await f.Engine.ApproveAsync(preview.Id, [.. preview.Items.Select(i => i.Id)]);

        Assert.Equal(2, result.Succeeded);
        Assert.True(File.Exists(f.In("Docs", "a.pdf")));
        Assert.True(File.Exists(f.In("notes.txt"))); // no rule: lands in the folder itself
        Assert.False(Directory.Exists(extracted.Staging));
        var notice = Assert.Single(f.Notifier.Shown, n => n.Kind == DownloadsNotices.ArchiveImported);
        Assert.Equal("in.zip imported", notice.Title);
    }

    [Fact]
    public async Task Discarding_an_import_preview_deletes_the_extracted_files()
    {
        using var f = new Fixture(FolderMode.Manual);
        var extracted = ArchiveImporter.Extract(Zip(f, ("a.txt", "1")), f.Folder, Staging(f));
        var preview = await f.Engine.PreviewImportAsync(f.Folder, extracted);

        f.Engine.Discard(preview.Id);

        Assert.False(Directory.Exists(extracted.Staging));
        Assert.False(File.Exists(f.In("a.txt")));
    }
}
