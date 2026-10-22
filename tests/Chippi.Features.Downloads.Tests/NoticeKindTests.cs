// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.Core.Notifications;

namespace Chippi.Features.Downloads.Tests;

public sealed class NoticeKindTests
{
    [Fact]
    public void Five_kinds_with_ready_to_review_on_the_island_only()
    {
        Assert.Equal(
            ["organized", "held", "failed", "archive-imported", "ready-to-review"],
            DownloadsNotices.Kinds.Select(k => k.Id));
        Assert.Equal(NoticePlaces.Island, DownloadsNotices.Kinds.Single(k => k.Id == DownloadsNotices.ReadyToReview).Default);
    }

    [Fact]
    public void Module_id_matches_the_catalog_placeholder()
    {
        var module = new DownloadsModule();

        Assert.Equal("downloads-organizer", module.Descriptor.Id);
        Assert.Contains(module.Descriptor.Requires, c => c.Id == "shell-integration");
    }
}
