// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Media;

namespace Chippi.Features.AgentUsage.Views;

/// <summary>The adjacent provider label or the session button names this decorative image.</summary>
internal sealed class AgentImage : Image
{
    internal AgentImage(double size, AgentKind? provider = null)
    {
        Width = Height = size;
        VerticalAlignment = VerticalAlignment.Center;
        IsHitTestVisible = false;
        SnapsToDevicePixels = true;
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);
        if (provider.HasValue) AgentIcons.SetSource(this, provider.Value);
    }

    protected override AutomationPeer? OnCreateAutomationPeer() => null;
}
