// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Xml.Linq;
using Chippi.App.Notifications;
using Chippi.Core.Notifications;

public sealed class FoundationTests
{
    [Fact] public void Body_and_buttons_round_trip_without_executing_arguments()
    {
        var notice = new Notice("organized", "A&B <files>") { Body = "\"folder\" & <destination>", Activation = "--downloads-run 42", Actions = [new("Undo", "--downloads-undo 42"), new("History", "--downloads-run 42")] };
        var xml = XDocument.Parse(ToastPayload.Build("downloads-organizer", notice));
        Assert.Equal(notice.Title, xml.Descendants("text").First().Value);
        Assert.Equal(notice.Body, xml.Descendants("text").Last().Value);
        Assert.True(ToastActivation.TryDecode(xml.Root!.Attribute("launch")!.Value, out var module, out var body));
        Assert.Equal("downloads-organizer", module); Assert.Equal(["--downloads-run", "42"], body);
        var actions = xml.Descendants("action").ToArray();
        Assert.Equal(["Undo", "History"], actions.Select(a => a.Attribute("content")!.Value));
        Assert.True(ToastActivation.TryDecode(actions[0].Attribute("arguments")!.Value, out _, out var undo));
        Assert.Equal(["--downloads-undo", "42"], undo);
    }
    [Fact] public void Quoted_windows_arguments_keep_spaces_quotes_and_unicode()
    {
        var args = ToastActivation.Parse("--organize \"C:\\Downloads\\A & B\" \"résumé.pdf\"");
        Assert.Equal(["--organize", @"C:\Downloads\A & B", "résumé.pdf"], args);
        Assert.True(ToastActivation.TryDecode(ToastActivation.Encode("downloads-organizer", args), out _, out var decoded));
        Assert.Equal(args, decoded);
    }
    [Theory]
    [InlineData(null)] [InlineData("")] [InlineData("--organize C:\\")] [InlineData("chippi-v1:!")]
    [InlineData("chippi-v1:bnVsbA==")] [InlineData("chippi-v1:e30=")]
    public void Invalid_callbacks_are_rejected(string? value) => Assert.False(ToastActivation.TryDecode(value, out _, out _));
    [Fact] public void Future_or_null_envelopes_are_rejected()
    {
        foreach(var json in new[]{"{\"Version\":2,\"Module\":\"downloads\",\"Arguments\":[]}","{\"Version\":1,\"Module\":null,\"Arguments\":[]}","{\"Version\":1,\"Module\":\"downloads\",\"Arguments\":null}"})
            Assert.False(ToastActivation.TryDecode("chippi-v1:"+Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(json)),out _,out _));
    }
    [Fact] public void Oversized_and_nul_arguments_are_rejected()
    {
        Assert.Throws<ArgumentException>(()=>ToastActivation.Encode("downloads",["--review",new string('x',4097)]));
        Assert.Throws<ArgumentException>(()=>ToastActivation.Encode("downloads",["--review","x\0y"]));
        Assert.False(ToastActivation.TryDecode(new string('x',16385),out _,out _));
    }
    [Fact] public void Payload_does_not_add_buttons_or_empty_body_that_were_not_supplied()
    {
        var xml = XDocument.Parse(ToastPayload.Build("downloads",new Notice("held","1 file held")));
        Assert.Empty(xml.Descendants("action")); Assert.Single(xml.Descendants("text"));
    }
    [Fact] public void Too_many_buttons_are_rejected() => Assert.Throws<ArgumentException>(()=>ToastPayload.Build("downloads",new Notice("held","Held"){Actions=Enumerable.Range(0,6).Select(_=>new NoticeAction("Review","--review")).ToArray()}));
    [Fact] public void Dev_identities_are_stable_and_separate_from_production_and_each_other()
    {
        var prod=ToastIdentity.Create(null,@"C:\Tools\Chippi.exe",@"C:\Programs");
        var a=ToastIdentity.Create("toast-a",@"C:\Tools\Chippi.exe",@"C:\Programs");
        var b=ToastIdentity.Create("toast-b",@"C:\Tools\Chippi.exe",@"C:\Programs");
        Assert.Equal("velopack.ChippiApp",prod.AppId); Assert.Equal(a,ToastIdentity.Create("toast-a",@"C:\Tools\Chippi.exe",@"C:\Programs"));
        Assert.NotEqual(prod.AppId,a.AppId); Assert.NotEqual(prod.Activator,a.Activator);Assert.NotEqual(a.Activator,b.Activator);Assert.NotEqual(a.Shortcut,prod.Shortcut);
        Assert.Contains("--dev-profile \"toast-a\"",a.ServerArguments);Assert.DoesNotContain("--dev-profile",prod.ServerArguments);
    }
    [Theory] [InlineData("p2.views")] [InlineData("bad\"name")] [InlineData("../real")]
    public void Invalid_dev_names_cannot_fall_back_to_production(string profile) => Assert.Throws<ArgumentException>(()=>ToastIdentity.Create(profile,@"C:\Tools\Chippi.exe",@"C:\Programs"));
}