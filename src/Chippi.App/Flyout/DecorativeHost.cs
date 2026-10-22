// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows.Automation.Peers;
using System.Windows.Controls;

namespace Chippi.App.Flyout;

/// <summary>
/// Hides its child from screen readers: the WPF stand-in for UWP's
/// <c>AutomationProperties.AccessibilityView="Raw"</c>, which WPF does not have. Used for the
/// header pet, which is decorative because the status line beside it carries the state (DESIGN.md §7).
/// </summary>
public sealed class DecorativeHost : Decorator
{
    protected override AutomationPeer OnCreateAutomationPeer() => new Peer(this);

    private sealed class Peer(DecorativeHost owner) : FrameworkElementAutomationPeer(owner)
    {
        protected override List<AutomationPeer>? GetChildrenCore() => null;

        protected override bool IsControlElementCore() => false;

        protected override bool IsContentElementCore() => false;
    }
}
