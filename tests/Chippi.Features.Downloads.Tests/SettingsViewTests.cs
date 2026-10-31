// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.Features.Downloads.Model;
using Chippi.Features.Downloads.Views.Settings;

namespace Chippi.Features.Downloads.Tests;

public sealed class SettingsViewTests
{
    [Theory]
    [InlineData(@"Documents\{yr}", false, "Unknown placeholder {yr}")]
    [InlineData(@"..\Other\{year}", false, "Can't go above the folder")]
    [InlineData(@"Documents\CON", false, "CON is a reserved name")]
    [InlineData(@"Docs\a|b", false, "Can't use | in a name")]
    [InlineData(@"{year}\{stem}", true, "A name can't contain \\ or /")]
    [InlineData(@"{stem}", false, "Unknown placeholder {stem}")]
    [InlineData("", false, "A folder is required")]
    public void Template_problems_read_as_the_mockups_show(string template, bool rename, string message) =>
        Assert.Equal(message, RuleChecks.Template(template, rename));

    [Theory]
    [InlineData(@"Documents\Invoices\{year}\{month}", false)]
    [InlineData(@"D:\Archive", false)]
    [InlineData(@"\\nas\scans\{year}", false)]
    [InlineData("{year}-{month}-{day} {stem}.{extension}", true)]
    public void Valid_templates_have_no_message(string template, bool rename) =>
        Assert.Null(RuleChecks.Template(template, rename));

    [Theory]
    [InlineData("^(invoice|receipt", "Missing )")]
    [InlineData("[abc", "Missing ]")]
    [InlineData("^(invoice|receipt)", null)]
    public void Regex_problems(string pattern, string? message) => Assert.Equal(message, RuleChecks.Regex(pattern));

    [Theory]
    [InlineData("pdf, .DOCX  txt", new[] { "pdf", "docx", "txt" })]
    [InlineData("*", new[] { "*" })]
    public void Extensions_are_split_and_normalized(string text, string[] expected) =>
        Assert.Equal(expected, RuleChecks.SplitExtensions(text));

    [Fact]
    public void Summary_reads_like_the_collapsed_rule()
    {
        var rule = new RuleSetting { Extensions = ["pdf"], NameRegex = "^inv", MaxBytes = 25L * 1024 * 1024, Action = RuleActionKind.Move, Template = @"Documents\{year}" };

        Assert.Equal(@".pdf · name matches ""^inv"" · up to 25 MB → Documents\{year}", RuleChecks.Summary(rule));
        Assert.Equal(".torrent · hold: Check before opening",
            RuleChecks.Summary(new RuleSetting { Extensions = ["torrent"], Action = RuleActionKind.Hold, HoldReason = "Check before opening" }));
    }

    [Fact]
    public void Summary_lists_extensions_with_their_dots()
    {
        var rule = new RuleSetting { Extensions = ["png", "jpg", "webp"], Action = RuleActionKind.Move, Template = "Pictures" };

        Assert.Equal(".png .jpg .webp → Pictures", RuleChecks.Summary(rule));
    }

    [Theory]
    [InlineData(@"C:\Users\x\Documents", "Documents")]
    [InlineData(@"D:\Sorted\Invoices\", "Invoices")]
    [InlineData(@"D:\Sorted\Invoices\{year}\{month}", @"Invoices\{year}\{month}")]
    [InlineData(@"\\server\share\Scans", "Scans")]
    [InlineData(@"Documents\{year}", @"Documents\{year}")] // relative, from before the picker: as typed
    public void A_chosen_folder_reads_by_its_name_in_the_summary(string template, string shown)
    {
        Assert.Equal(shown, RuleChecks.DestinationName(template));
        var rule = new RuleSetting { Extensions = ["txt"], Action = RuleActionKind.Move, Template = template };
        Assert.Equal($".txt → {shown}", RuleChecks.Summary(rule));
    }

    [Theory]
    [InlineData(DownloadsRequestKind.Organize, @"C:\Users\x\Downloads", null, @"module:downloads-organizer/preview/C:\Users\x\Downloads")]
    [InlineData(DownloadsRequestKind.Organize, null, null, "module:downloads-organizer/review")]
    [InlineData(DownloadsRequestKind.Review, null, null, "module:downloads-organizer/review")]
    [InlineData(DownloadsRequestKind.Run, null, 42L, "module:downloads-organizer/history/42")]
    [InlineData(DownloadsRequestKind.Rules, null, null, "module:downloads-organizer/rules")]
    [InlineData(DownloadsRequestKind.Organize, @"C:\Users\x\Downloads", null, @"module:downloads-organizer/explorer/C:\Users\x\Downloads", true)]
    public void Requests_map_to_settings_links(DownloadsRequestKind kind, string? folder, long? run, string link, bool fromExplorer = false) =>
        Assert.Equal(link, DownloadsModule.SettingsLink(new DownloadsRequest(kind, folder, run, fromExplorer)));

    [Fact]
    public void Clone_is_deep()
    {
        var settings = new DownloadsSettings
        {
            Folders = [new WatchedFolder { Path = @"C:\D", Mode = FolderMode.Manual }],
            Rules = [new RuleSetting { Id = "a", Extensions = ["pdf"] }],
        };

        var copy = settings.Clone();
        copy.Folders[0].Mode = FolderMode.Silent;
        copy.Rules[0].Extensions!.Add("txt");
        copy.Rules[0].Name = "changed";

        Assert.Equal(FolderMode.Manual, settings.Folders[0].Mode);
        Assert.Equal(["pdf"], settings.Rules[0].Extensions!);
        Assert.Equal("", settings.Rules[0].Name);
    }
}
