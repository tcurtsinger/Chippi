// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Chippi.Features.AgentUsage;

/// <summary>Provider artwork shared by the widget, its activity notices and Settings.</summary>
public static class AgentIcons
{
    private static readonly BitmapFrame ClaudeCode = Load("ClaudeCode");
    private static readonly BitmapFrame Codex = Load("Codex");

    public static ImageSource Source(AgentKind agent) => agent switch
    {
        AgentKind.Claude => ClaudeCode,
        AgentKind.Codex => Codex,
        _ => throw new ArgumentOutOfRangeException(nameof(agent)),
    };

    private static BitmapFrame Load(string name)
    {
        var uri = new Uri($"pack://application:,,,/Chippi.Features.AgentUsage;component/Assets/{name}.png");
        var image = BitmapFrame.Create(uri, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        image.Freeze();
        return image;
    }
}
