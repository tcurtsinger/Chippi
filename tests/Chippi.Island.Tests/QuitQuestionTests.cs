// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.IO;
using Chippi.App.Shell;
using Chippi.Core.Config;
using Microsoft.Extensions.Logging.Abstractions;

namespace Chippi.Island.Tests;

/// <summary>Quit asks each feature's question first (screen-recording.md, "Stop recording and quit?"). Shows nothing.</summary>
public sealed class QuitQuestionTests
{
    private static void WithShell(Action<ShellContext> test) => WpfTestThread.Run(() =>
    {
        var root = Directory.CreateTempSubdirectory("chippi-quit-").FullName;
        var shell = new ShellContext(new ConfigStore(Path.Combine(root, "settings.json")), NullLoggerFactory.Instance, false, "quit-question-test");
        try { test(shell); }
        finally
        {
            shell.Clipboard.Dispose();
            shell.Input.Dispose();
            Directory.Delete(root, recursive: true);
        }
    });

    // The answers below complete at once, so waiting on the UI thread can't block.
    private static bool MayQuit(ShellContext shell) => shell.MayQuitAsync().GetAwaiter().GetResult();

    [Fact]
    public void Quit_asks_a_feature_s_question_and_Cancel_stops_it() => WithShell(shell =>
    {
        var asked = new List<string>();
        bool answer = false;
        shell.ConfirmQuit = q => { asked.Add(q); return Task.FromResult(answer); };
        string? question = null;
        shell.QuitQuestions.Add(() => question);

        Assert.True(MayQuit(shell));
        Assert.Empty(asked); // nothing recording: nothing asked

        question = "Stop recording and quit?";
        Assert.False(MayQuit(shell));
        answer = true;
        Assert.True(MayQuit(shell));
        Assert.Equal(["Stop recording and quit?", "Stop recording and quit?"], asked);
    });

    [Fact]
    public void Without_a_confirm_Quit_goes_ahead() => WithShell(shell =>
    {
        shell.QuitQuestions.Add(() => "Stop recording and quit?");
        Assert.True(MayQuit(shell));
    });
}
