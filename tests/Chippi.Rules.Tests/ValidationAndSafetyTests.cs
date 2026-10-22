// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.Rules;

namespace Chippi.Rules.Tests;

public sealed class ValidationAndSafetyTests
{
    private static Rule Rule(RuleAction action, Conditions? c = null) => new("id", "Rule", true, 1, c ?? new(), action);
    private static FileFacts File(string name, FileAttributes attributes = FileAttributes.Normal, long bytes = 1) =>
        new(name, @"C:\Inbox\" + name, bytes, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, attributes);

    [Theory]
    [InlineData(@"..\Outside")]
    [InlineData(@"A\..\B")]
    [InlineData(@"C:relative")]
    [InlineData(@"\rootRelative")]
    [InlineData(@"\\?\C:\device")]
    [InlineData(@"\\.\device")]
    [InlineData("{unknown}")]
    [InlineData("{year")]
    [InlineData("year}")]
    [InlineData("Folder.")]
    [InlineData("Folder ")]
    [InlineData("CON")]
    [InlineData("aux.txt")]
    [InlineData("COM1.pdf")]
    [InlineData("LPT\u00B2.log")]
    [InlineData("NUL")]
    [InlineData("file:stream")]
    [InlineData("bad*name")]
    [InlineData("bad?name")]
    [InlineData("bad|name")]
    [InlineData("bad<name")]
    [InlineData("bad\nname")]
    [InlineData("")]
    [InlineData(" ")]
    public void Invalid_move_templates_are_rejected_before_planning(string template) =>
        Assert.NotEmpty(RuleValidator.Validate([Rule(new MoveTo(template))]));

    [Theory]
    [InlineData("../name")]
    [InlineData("folder/name")]
    [InlineData(@"folder\name")]
    [InlineData("{bad}")]
    [InlineData("{filename}.")]
    [InlineData("PRN.txt")]
    public void Rename_templates_cannot_be_paths(string template) => Assert.NotEmpty(RuleValidator.Validate([Rule(new RenameTo(template))]));

    [Fact]
    public void Conditions_validate_bad_regex_and_bounds_and_extensions()
    {
        foreach (var c in new Conditions[] { new() { NameRegex = "[" }, new() { MinBytes = -1 }, new() { MaxBytes = -1 }, new() { MinBytes = 10, MaxBytes = 1 },
            new() { ModifiedAfter = new(2026, 2, 1), ModifiedBefore = new(2026, 1, 1) }, new() { Extensions = [] }, new() { Extensions = [".pdf"] }, new() { Extensions = ["exe,msi"] } })
            Assert.NotEmpty(RuleValidator.Validate([Rule(new MoveTo("Sorted"), c)]));
        Assert.Empty(RuleValidator.Validate([Rule(new MoveTo("Sorted"), new() { Extensions = ["", "*", "pdf"] })]));
    }

    [Fact]
    public void Rule_ids_orders_names_and_hold_reasons_are_validated()
    {
        var rule = Rule(new MoveTo("Sorted"));
        Assert.NotEmpty(RuleValidator.Validate([rule, rule with { Id = "other" }]));
        Assert.NotEmpty(RuleValidator.Validate([rule, rule with { Order = 2 }]));
        Assert.NotEmpty(RuleValidator.Validate([rule with { Name = "" }]));
        Assert.NotEmpty(RuleValidator.Validate([rule with { Id = "" }]));
        Assert.NotEmpty(RuleValidator.Validate([rule with { Action = new Hold("") }]));
        Assert.Throws<ArgumentException>(() => Planner.Plan(File("note.txt"), [rule, rule], new(@"C:\Inbox", DateTimeOffset.UnixEpoch, TimeZoneInfo.Utc, [])));
    }

    [Fact]
    public void Lexical_paths_support_long_paths_and_drive_roots_without_IO()
    {
        string path = @"C:\" + string.Join('\\', Enumerable.Repeat(new string('a', 90), 4));
        Assert.True(path.Length > 260);
        Assert.Equal(path, WindowsPaths.NormalizeAbsolute(path));
        Assert.Equal(@"C:\Inbox\Sorted", WindowsPaths.NormalizeAbsolute("c:/Inbox//Sorted"));
        Assert.Equal(@"C:\", WindowsPaths.Parent(@"C:\name.txt"));
        Assert.Equal(@"\\server\share\", WindowsPaths.NormalizeAbsolute(@"\\server\share"));
        Assert.Throws<ArgumentException>(() => WindowsPaths.ValidateComponent(new string('a', 256)));
        Assert.Throws<ArgumentException>(() => WindowsPaths.NormalizeAbsolute(@"\\server"));
    }

    [Theory]
    [InlineData("picture.pdf.exe")]
    [InlineData("invoice.pdf .exe")]
    [InlineData("picture.PDF.EXE")]
    [InlineData("image.png.lnk")]
    [InlineData("archive.zip.ps1")]
    [InlineData("run  .exe")]
    [InlineData("ab\u202Ecd.exe")]
    [InlineData("ab\u202Dcd.msi")]
    public void Misleading_names_are_held_even_by_a_catch_all(string name)
    {
        Assert.True(FileSafety.IsMisleadingName(name));
        var plan = Planner.Plan(File(name), [Rule(new MoveTo("Sorted"))], new(@"C:\Inbox", DateTimeOffset.UnixEpoch, TimeZoneInfo.Utc, []));
        Assert.Equal(OperationKind.Hold, plan.Kind);
        Assert.Equal(WarningKind.MisleadingName, Assert.Single(plan.Warnings).Kind);
        Assert.Null(plan.Destination);
    }

    [Theory]
    [InlineData("installer.exe")]
    [InlineData("notes.pdf")]
    [InlineData("version.2.exe")]
    [InlineData("notes.exe.txt")]
    [InlineData("ordinary name.exe")]
    public void Ordinary_names_are_not_misleading(string name) => Assert.False(FileSafety.IsMisleadingName(name));

    [Theory]
    [InlineData("partial.part")]
    [InlineData("partial.partial")]
    [InlineData("partial.crdownload")]
    [InlineData("partial.download")]
    [InlineData("partial.TMP")]
    [InlineData("partial.opdownload")]
    [InlineData("partial.!ut")]
    [InlineData(".chippiignore")]
    [InlineData(".hidden")]
    [InlineData("desktop.ini")]
    [InlineData("Thumbs.db")]
    [InlineData("~$document.docx")]
    [InlineData("pagefile.sys")]
    public void Built_ins_are_held(string name) => Assert.Equal(WarningKind.BuiltInExclusion, FileSafety.Exclusion(File(name))!.Kind);

    [Theory]
    [InlineData(FileAttributes.Hidden, WarningKind.BuiltInExclusion)]
    [InlineData(FileAttributes.System, WarningKind.BuiltInExclusion)]
    [InlineData(FileAttributes.Directory, WarningKind.BuiltInExclusion)]
    [InlineData(FileAttributes.ReparsePoint, WarningKind.BuiltInExclusion)]
    [InlineData(FileAttributes.Offline, WarningKind.CloudPlaceholder)]
    [InlineData((FileAttributes)0x00400000, WarningKind.CloudPlaceholder)]
    public void Attribute_safety_is_based_only_on_supplied_facts(FileAttributes attributes, WarningKind expected) =>
        Assert.Equal(expected, FileSafety.Exclusion(File("item.txt", attributes))!.Kind);

    [Fact]
    public void Empty_file_waits_until_it_grows()
    {
        Assert.Equal(WarningKind.EmptyFile, FileSafety.Exclusion(File("item.txt", bytes: 0))!.Kind);
        Assert.Null(FileSafety.Exclusion(File("item.txt", bytes: 1)));
    }

    [Theory]
    [InlineData("*.pdf", "FILE.PDF", true)]
    [InlineData("pre*mid*end", "PRE-xx-MID-yy-END", true)]
    [InlineData("pre*mid*end", "pre-end", false)]
    [InlineData("exact", "notexact", false)]
    [InlineData("exact", "exactly", false)]
    [InlineData("exact", "EXACT", true)]
    [InlineData("*", "anything", true)]
    [InlineData("a*b", "ab", true)]
    [InlineData("a*b", "abb", true)]
    [InlineData("a*b", "abc", false)]
    public void Ignore_matching_is_filename_only_with_star_and_invariant_case(string pattern, string name, bool match) => Assert.Equal(match, IgnoreFile.Matches(name, pattern));

    [Fact]
    public void Ignore_parser_handles_comments_whitespace_and_literal_hashes()
    {
        var patterns = IgnoreFile.Parse("# comment\r\n\r\n *.pdf # explanation\n report\\#final.txt # comment\nexact\n");
        Assert.Equal(["*.pdf", "report#final.txt", "exact"], patterns.Patterns);
        Assert.Empty(patterns.Errors);
        Assert.Contains(patterns.Patterns, p => IgnoreFile.Matches("report#final.txt", p));
    }

    [Theory]
    [InlineData("!keep")]
    [InlineData("**/cache")]
    [InlineData("ab**cd")]
    [InlineData("name?.txt")]
    [InlineData("folder/")]
    [InlineData(@"folder\item")]
    public void Unsupported_ignore_syntax_is_reported_per_line(string pattern)
    {
        var parsed = IgnoreFile.Parse(pattern);
        Assert.Empty(parsed.Patterns);
        Assert.Equal(1, Assert.Single(parsed.Errors).Line);
    }

    [Fact]
    public void Library_references_no_core_WPF_or_external_dependency()
    {
        var refs = typeof(Planner).Assembly.GetReferencedAssemblies().Select(a => a.Name).ToArray();
        Assert.DoesNotContain(refs, name => name is not null && (name.StartsWith("Chippi.", StringComparison.Ordinal) || name.StartsWith("Presentation", StringComparison.Ordinal) || name.StartsWith("Microsoft.Extensions", StringComparison.Ordinal)));
    }
}
