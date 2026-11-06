// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Automation;
using Chippi.Features.ScreenText.Views;
using Chippi.Island.Tests;
using static Chippi.Features.ScreenText.Tests.Shots;

namespace Chippi.Features.ScreenText.Tests;

/// <summary>The island's Text page (Capture results v3) over a fake model: the text, its count, Copy all, the lit lines and the states.</summary>
public sealed class TextPageTests
{
    [Fact]
    public void Opens_filled_and_copies_the_text_as_edited()
    {
        WpfTestThread.Run(() =>
        {
            IslandTheme();
            var model = new FakeText { Result = Document() };
            var view = Laid(new TextResultView(model));
            Assert.Equal(string.Join("\n", DocumentLines), view.TextBox.Text);
            Assert.Equal("English · 39 words", view.Status);
            Assert.Equal("Extracted text", AutomationProperties.GetName(view.TextBox));

            view.TextBox.Text += "\nExtra";
            Assert.Equal("English · 40 words", view.Status);
            Click(view.CopyAllButton);
            Assert.EndsWith("\nExtra", Assert.Single(model.Copies));
            Assert.Equal("Copied", view.Status);

            model.Refuse = true;
            Click(view.CopyAllButton);
            Assert.Equal(TextResultView.CopyFailed, view.Status);
            Assert.EndsWith("Extra", view.TextBox.Text); // The text stays on the page.
        });
    }

    [Fact]
    public void The_lines_a_selection_touches_light_on_the_capture_until_the_text_is_edited()
    {
        WpfTestThread.Run(() =>
        {
            IslandTheme();
            var model = new FakeText { Result = Document() };
            var view = Laid(new TextResultView(model));
            Assert.Empty(view.Marked); // Nothing until the text is used.

            var lines = model.Result!.Lines;
            view.TextBox.Select(lines[4].Start + 2, 4);
            Assert.Equal([4], view.Marked);
            view.TextBox.Select(lines[1].Start + 5, lines[2].Start - lines[1].Start + 3);
            Assert.Equal([1, 2], view.Marked);

            view.TextBox.Text = view.TextBox.Text.Replace("Friday", "Monday", StringComparison.Ordinal);
            view.TextBox.Select(lines[4].Start, 4);
            Assert.Empty(view.Marked);
        });
    }

    [Fact]
    public void Each_state_says_what_happened_and_keeps_Reselect_and_Retry()
    {
        WpfTestThread.Run(() =>
        {
            IslandTheme();
            var model = new FakeText { Result = Document() with { State = TextReadState.NoText, Text = "", Lines = [] } };
            var view = Laid(new TextResultView(model));
            Assert.Contains(TextResultView.NoText, Texts(view.Message));
            Assert.Equal(Visibility.Collapsed, view.CopyAllButton.Visibility);
            Assert.Equal(Visibility.Visible, view.ReselectButton.Visibility);
            Assert.Equal(Visibility.Visible, view.RetryButton.Visibility);

            model.Set(Document() with { State = TextReadState.NoLanguage, Text = "", Lines = [] });
            Assert.Equal([TextResultView.NoLanguage], Texts(view.Message));
            model.Set(Document() with { State = TextReadState.Failed, Text = "", Lines = [] });
            Assert.Equal([TextResultView.Failed], Texts(view.Message));

            Click(view.ReselectButton);
            Click(view.RetryButton);
            Assert.Equal(["reselect", "retry"], model.Calls);
        });
    }

    [Fact]
    public void A_new_read_keeps_the_old_text_until_it_lands()
    {
        WpfTestThread.Run(() =>
        {
            IslandTheme();
            var model = new FakeText { Result = Document() };
            var view = Laid(new TextResultView(model));
            view.TextBox.Text = "Edited";

            model.Reading = true;
            model.Raise();
            Assert.Equal("Edited", view.TextBox.Text);
            Assert.Equal(TextResultView.Reading, view.Status);
            Assert.False(view.ReselectButton.IsEnabled);
            Assert.False(view.RetryButton.IsEnabled);

            model.Reading = false;
            model.Set(Document("German"));
            Assert.StartsWith("Quarterly review", view.TextBox.Text, StringComparison.Ordinal);
            Assert.Equal("German · 39 words", view.Status);
            Assert.True(view.ReselectButton.IsEnabled);
        });
    }

    [Fact]
    public void Shots()
    {
        if (Folder is null) return;
        WpfTestThread.Run(() =>
        {
            IslandTheme();
            var model = new FakeText { Result = Document() };
            var view = Laid(new TextResultView(model));
            var lines = model.Result!.Lines;
            view.TextBox.Select(lines[4].Start, lines[4].Length);
            Panel(view, view.Header, "island-text");

            var none = new TextResultView(new FakeText { Result = Document() with { State = TextReadState.NoText, Text = "", Lines = [] } });
            Panel(Laid(none), none.Header, "island-text-none");
            var language = new TextResultView(new FakeText { Result = Document() with { State = TextReadState.NoLanguage, Text = "", Lines = [] } });
            Panel(Laid(language), language.Header, "island-text-no-language");

            var refused = new FakeText { Result = Document(), Refuse = true };
            var failed = Laid(new TextResultView(refused));
            Click(failed.CopyAllButton);
            Panel(failed, failed.Header, "island-text-copy-failed");
        });
    }

    internal sealed class FakeText : ITextPageModel
    {
        public TextResult? Result { get; set; }

        public bool Reading { get; set; }

        public bool Refuse { get; set; }

        public List<string> Copies { get; } = [];

        public List<string> Calls { get; } = [];

        public event Action? Changed;

        public void Set(TextResult result)
        {
            Result = result;
            Changed?.Invoke();
        }

        public void Raise() => Changed?.Invoke();

        public bool Copy(string text)
        {
            if (Refuse) return false;
            Copies.Add(text);
            return true;
        }

        public void Reselect() => Calls.Add("reselect");

        public void Retry() => Calls.Add("retry");
    }
}
