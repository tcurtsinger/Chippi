// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.Core.Island;

namespace Chippi.Core.Tests;

public sealed class FeedbackLineTests
{
    private sealed class Island : IIslandPublisher
    {
        public List<string> Cleared { get; } = [];
        public void Publish(IslandActivity activity) { }
        public void Clear(string key) => Cleared.Add(key);
        public void ReportSuccess(string? message = null) { }
    }

    private static IslandActivity Line(string title) => new("k", IslandActivityKind.Status, title, "src");

    [Fact]
    public void An_expired_callback_of_a_replaced_line_does_not_clear_the_new_one()
    {
        var island = new Island();
        using var line = new FeedbackLine(island, "k", TimeProvider.System, TimeSpan.FromHours(1));
        line.Show(Line("A"));
        line.Show(Line("B"));
        line.Expire(1); // A's callback, already queued when B replaced it
        Assert.Empty(island.Cleared);
        line.Expire(2);
        Assert.Equal(["k"], island.Cleared);
    }
}
