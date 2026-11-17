// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.App;

// Velopack starts Chippi in its install folder; a program Chippi started there kept the folder open and Setup couldn't
// replace it (2026-11-17). Only that start leaves the folder: a terminal's run keeps its relative arguments.
public sealed class StartupFolderTests
{
    private const string Exe = @"C:\Users\Owner\AppData\Local\ChippiApp\current\";

    [Theory]
    [InlineData(@"C:\Users\Owner\AppData\Local\ChippiApp\current")]
    [InlineData(@"C:\Users\Owner\AppData\Local\ChippiApp\current\")]
    [InlineData(@"c:\users\owner\appdata\local\chippiapp")]
    [InlineData(@"C:\Users\Owner\AppData\Local\ChippiApp\packages")]
    public void Install_folder_and_Velopack_root_are_left(string workingFolder) =>
        Assert.True(App.InInstallFolder(workingFolder, Exe));

    [Theory]
    [InlineData(@"C:\Users\Owner\AppData\Local\ChippiAppOld")]
    [InlineData(@"C:\Users\Owner\AppData\Local")]
    [InlineData(@"X:\code\chippi")]
    [InlineData(@"C:\Windows\System32")]
    public void Other_folders_are_kept(string workingFolder) =>
        Assert.False(App.InInstallFolder(workingFolder, Exe));
}
