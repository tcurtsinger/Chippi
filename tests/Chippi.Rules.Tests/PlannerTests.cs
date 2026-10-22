// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.Rules;

namespace Chippi.Rules.Tests;

public sealed class PlannerTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 1, 0, 0, TimeSpan.Zero);
    private static readonly TimeZoneInfo Zone = TimeZoneInfo.CreateCustomTimeZone("Test", TimeSpan.FromHours(-5), "Test", "Test");
    private static PlanContext Context(params string[] occupied) => new(@"C:\Inbox", Now, Zone, occupied);
    private static FileFacts File(string name = "notes.PDF", long bytes = 100, DateTimeOffset? modified = null, FileAttributes attributes = FileAttributes.Normal) =>
        new(name, @"C:\Inbox\" + name, bytes, Now.AddYears(-1), modified ?? Now, attributes);
    private static Rule Rule(string id = "first", int order = 1, Conditions? conditions = null, RuleAction? action = null, bool enabled = true) =>
        new(id, id, enabled, order, conditions ?? new Conditions(), action ?? new MoveTo("Sorted"));

    [Fact]
    public void Order_is_numeric_and_only_the_first_enabled_match_is_used()
    {
        var rules = new[] { Rule("later", 20), Rule("disabled", 1, enabled: false), Rule("winner", 10) };
        var result = Planner.Plan(File(), rules, Context());
        Assert.Equal("winner", result.RuleId);
        Assert.Equal(OperationKind.Move, result.Kind);
        Assert.Equal(@"C:\Inbox\Sorted\notes.PDF", result.Destination);
    }

    [Theory]
    [InlineData("PDF", true)]
    [InlineData("pdf", true)]
    [InlineData("*", true)]
    [InlineData("txt", false)]
    [InlineData("", false)]
    public void Extension_conditions_are_explicit_and_case_insensitive(string extension, bool matches) =>
        Assert.Equal(matches ? OperationKind.Move : OperationKind.NoMatch,
            Planner.Plan(File(), [Rule(conditions: new() { Extensions = [extension] })], Context()).Kind);

    [Theory]
    [InlineData("", true)]
    [InlineData("*", true)]
    [InlineData("pdf", false)]
    public void Extensionless_files_need_empty_extension_or_catch_all(string extension, bool matches) =>
        Assert.Equal(matches ? OperationKind.Move : OperationKind.NoMatch,
            Planner.Plan(File("README"), [Rule(conditions: new() { Extensions = [extension] })], Context()).Kind);

    [Fact]
    public void All_conditions_are_anded_with_inclusive_byte_and_local_date_bounds()
    {
        var c = new Conditions { Extensions = ["pdf"], NameRegex = @"^NOTES\.PDF$", MinBytes = 100, MaxBytes = 100,
            ModifiedAfter = new(2025, 12, 31), ModifiedBefore = new(2025, 12, 31) };
        Assert.Equal(OperationKind.Move, Planner.Plan(File(), [Rule(conditions: c)], Context()).Kind);
        Assert.Equal(OperationKind.NoMatch, Planner.Plan(File(bytes: 99), [Rule(conditions: c)], Context()).Kind);
        Assert.Equal(OperationKind.NoMatch, Planner.Plan(File(bytes: 101), [Rule(conditions: c)], Context()).Kind);
        Assert.Equal(OperationKind.NoMatch, Planner.Plan(File("other.pdf"), [Rule(conditions: c)], Context()).Kind);
        Assert.Equal(OperationKind.NoMatch, Planner.Plan(File(modified: Now.AddDays(1)), [Rule(conditions: c)], Context()).Kind);
        Assert.Equal(OperationKind.NoMatch, Planner.Plan(File(modified: Now.AddDays(-1)), [Rule(conditions: c)], Context()).Kind);
    }

    [Fact]
    public void Local_planning_date_not_creation_or_modification_drives_placeholders()
    {
        var result = Planner.Plan(File(modified: Now.AddYears(-4)), [Rule(action: new MoveTo("{year}/{month}/{day}/{extension}/{stem}"))], Context());
        Assert.Equal(@"C:\Inbox\2025\12\31\pdf\notes\notes.PDF", result.Destination);
    }

    [Fact]
    public void Supplied_zone_accounts_for_daylight_saving_when_evaluating_modified_date()
    {
        var start = TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 2, 0, 0), 3, 2, DayOfWeek.Sunday);
        var end = TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 2, 0, 0), 11, 1, DayOfWeek.Sunday);
        var adjustment = TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(new DateTime(2000, 1, 1), new DateTime(2099, 12, 31), TimeSpan.FromHours(1), start, end);
        var zone = TimeZoneInfo.CreateCustomTimeZone("DST test", TimeSpan.FromHours(-5), "Test", "Standard", "Summer", [adjustment]);
        var summer = new DateTimeOffset(2026, 7, 1, 4, 30, 0, TimeSpan.Zero);
        var c = new Conditions { ModifiedAfter = new(2026, 7, 1), ModifiedBefore = new(2026, 7, 1) };
        Assert.Equal(OperationKind.Move, Planner.Plan(File(modified: summer), [Rule(conditions: c)], Context() with { TimeZone = zone }).Kind);
    }

    [Fact]
    public void Rename_only_keeps_parent_and_full_filename_and_stem_have_distinct_meaning()
    {
        var result = Planner.Plan(File(), [Rule(action: new RenameTo("{stem}-{year}.{extension}"))], Context());
        Assert.Equal(OperationKind.Rename, result.Kind);
        Assert.Equal(@"C:\Inbox\notes-2025.pdf", result.Destination);
        Assert.Equal(@"C:\Inbox\ByName\notes.PDF\notes.PDF", Planner.Plan(File(), [Rule(action: new MoveTo("ByName/{filename}"))], Context()).Destination);
    }

    [Fact]
    public void Collision_suffixes_begin_at_two_and_preserve_original_extension_case()
    {
        var result = Planner.Plan(File(), [Rule()], Context(@"c:\inbox\sorted\NOTES.PDF", @"C:\Inbox\Sorted\notes (2).PDF"));
        Assert.Equal(@"C:\Inbox\Sorted\notes (3).PDF", result.Destination);
    }

    [Fact]
    public void Rename_collision_and_extensionless_collision_are_safe()
    {
        var result = Planner.Plan(File(), [Rule(action: new RenameTo("new"))], Context(@"C:\Inbox\new"));
        Assert.Equal(@"C:\Inbox\new (2)", result.Destination);
    }

    [Fact]
    public void Batch_reserves_proposals_in_input_order()
    {
        var result = Planner.PlanBatch([File("first.txt"), File("second.txt")], [Rule(action: new RenameTo("same.txt"))], Context());
        Assert.Equal([@"C:\Inbox\same.txt", @"C:\Inbox\same (2).txt"], result.Select(r => r.Destination));
    }

    [Theory]
    [InlineData(@"D:\Archive", @"D:\Archive\notes.PDF")]
    [InlineData(@"\\server\share\Archive", @"\\server\share\Archive\notes.PDF")]
    public void Absolute_destinations_do_not_resolve_under_source(string destination, string expected) =>
        Assert.Equal(expected, Planner.Plan(File(), [Rule(action: new MoveTo(destination))], Context()).Destination);

    [Fact]
    public void Source_folder_and_same_name_rename_are_not_operations()
    {
        Assert.Equal(OperationKind.Hold, Planner.Plan(File(), [Rule(action: new MoveTo(@"C:\Inbox"))], Context()).Kind);
        Assert.Equal(OperationKind.Hold, Planner.Plan(File(), [Rule(action: new RenameTo("{filename}"))], Context()).Kind);
    }

    [Fact]
    public void Rules_and_context_are_not_mutated_and_repeat_calls_are_equal()
    {
        var context = Context();
        var rules = new[] { Rule() };
        var first = Planner.Plan(File(), rules, context);
        var second = Planner.Plan(File(), rules, context);
        Assert.Equal(first.Destination, second.Destination);
        Assert.Equal(first.RuleId, second.RuleId);
        Assert.Empty(context.OccupiedPaths);
        Assert.Equal("Sorted", Assert.IsType<MoveTo>(rules[0].Action).DestinationTemplate);
    }

    [Fact]
    public void Explicit_hold_stops_rule_selection()
    {
        var result = Planner.Plan(File(), [Rule(action: new Hold("Review")), Rule("later", 2)], Context());
        Assert.Equal(OperationKind.Hold, result.Kind);
        Assert.Equal("Review", result.HoldReason);
        Assert.Equal("first", result.RuleId);
        Assert.Null(result.Destination);
    }

    [Fact]
    public void Ignore_and_safety_cannot_be_overridden_by_a_rule()
    {
        var c = Context() with { IgnorePatterns = IgnoreFile.Parse("*.pdf") };
        var result = Planner.Plan(File(), [Rule()], c);
        Assert.Equal(WarningKind.IgnoreMatch, Assert.Single(result.Warnings).Kind);
        Assert.Equal(OperationKind.Hold, result.Kind);
        Assert.Null(result.Destination);
    }

    [Fact]
    public void Invalid_and_nested_source_paths_hold_instead_of_proposing_a_move()
    {
        foreach (string source in new[] { @"C:\Other\notes.PDF", @"C:\Inbox\nested\notes.PDF", @"C:\Inbox\..\notes.PDF", "relative", @"C:\Inbox\wrong.pdf" })
            Assert.Equal(WarningKind.InvalidPath, Assert.Single(Planner.Plan(File() with { FullPath = source }, [Rule()], Context()).Warnings).Kind);
    }

    [Fact]
    public void Template_expansion_does_not_interpret_braces_in_real_filename()
    {
        var result = Planner.Plan(File("{year}.pdf"), [Rule(action: new RenameTo("copy-{filename}"))], Context());
        Assert.Equal(@"C:\Inbox\copy-{year}.pdf", result.Destination);
    }

    [Fact]
    public void Regex_timeout_is_a_diagnostic_and_falls_through_to_next_rule()
    {
        var result = Planner.Plan(File(new string('a', 180) + "!.txt"), [Rule(conditions: new() { NameRegex = "^(a+)+$" }), Rule("fallback", 2)], Context());
        Assert.Equal("fallback", result.RuleId);
        Assert.Equal(WarningKind.RegexTimeout, Assert.Single(result.Warnings).Kind);
        Assert.Equal(TimeSpan.FromMilliseconds(50), RuleValidator.RegexTimeout);
    }
    [Fact]
    public void Preview_proposes_warned_name_without_authorizing_automatic_move()
    {
        var file = File("report.pdf.exe");
        Assert.Equal(OperationKind.Hold, Planner.Plan(file, [Rule()], Context()).Kind);
        var preview = Planner.Plan(file, [Rule()], Context() with { Preview = true });
        Assert.Equal(OperationKind.Move, preview.Kind);
        Assert.NotNull(preview.Destination);
        Assert.Equal(WarningKind.MisleadingName, Assert.Single(preview.Warnings).Kind);
    }

    [Fact]
    public void Batch_reserves_other_sources_and_rejects_duplicate_sources()
    {
        var files = new[] { File("first.txt"), File("occupied.txt") };
        var plan = Planner.PlanBatch(files, [Rule(action: new RenameTo("occupied.txt"))], Context());
        Assert.Equal(@"C:\Inbox\occupied (2).txt", plan[0].Destination);
        Assert.Throws<ArgumentException>(() => Planner.PlanBatch([File(), File()], [Rule()], Context()));
    }

    [Fact]
    public void Dates_are_zero_padded_and_all_template_tokens_have_defined_values()
    {
        var rendered = Templates.Render("{year}|{month}|{day}|{extension}|{filename}|{stem}", "some.name.PDF", new(2026, 1, 2));
        Assert.Equal("2026|01|02|pdf|some.name.PDF|some.name", rendered);
        Assert.Equal("README", Templates.Render("{stem}", "README", new(2026, 1, 2)));
    }

    [Theory]
    [InlineData("doc.pdf", 0, FileAttributes.Normal, WarningKind.EmptyFile)]
    [InlineData("doc.tmp", 1, FileAttributes.Normal, WarningKind.BuiltInExclusion)]
    [InlineData("doc.pdf", 1, FileAttributes.Offline, WarningKind.CloudPlaceholder)]
    public void Safety_still_holds_files_in_preview(string name, long bytes, FileAttributes attributes, WarningKind reason)
    {
        var plan = Planner.Plan(File(name, bytes, attributes: attributes), [Rule()], Context() with { Preview = true });
        Assert.Equal(OperationKind.Hold, plan.Kind);
        Assert.Equal(reason, Assert.Single(plan.Warnings).Kind);
        Assert.Null(plan.Destination);
    }
}