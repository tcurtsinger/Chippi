// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors
namespace Chippi.Features.WindowLayouts.Tests;

public sealed class ArrangementSummaryTests
{
    [Fact]
    public void One_problem_reads_needs_attention()
    {
        var one = new ArrangementSummary(false, 11, 12, 1, 0, 0, 0, null);
        Assert.Equal("1 needs attention", one.Note);
        Assert.Equal("11 of 12 ready · 1 needs attention", one.Status);

        var three = new ArrangementSummary(false, 9, 12, 3, 0, 0, 0, null);
        Assert.Equal("3 need attention", three.Note);
        Assert.Equal("9 of 12 ready · 3 need attention", three.Status);
    }
}
