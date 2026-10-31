// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.App.Island;

namespace Chippi.Island.Tests;

/// <summary>The island tests' setup; WpfTestThread.cs is also linked into feature tests that don't reference the app.</summary>
internal static partial class WpfTestThread
{
    // A real click or Esc on the PC while the tests run must not close a test's panel; tests press for themselves.
    static partial void Prepare() => IslandWindow.WatchesInput = false;
}
