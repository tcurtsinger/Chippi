// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.Core.Clipboard;

namespace Chippi.Features.Clipboard.Views;

internal static class ClipboardFeedback
{
    public static string ForPaste(PasteOutcome outcome) => outcome switch
    {
        PasteOutcome.Sent => "",
        PasteOutcome.Copied => "Copied",
        PasteOutcome.Busy => "Clipboard busy",
        _ => "Item unavailable",
    };

    public static string ForCopy(ClipStatus status) => status switch
    {
        ClipStatus.Ok => "Copied",
        ClipStatus.Busy => "Clipboard busy",
        _ => "Item unavailable",
    };
}
