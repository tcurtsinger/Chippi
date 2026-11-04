// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;

namespace Chippi.Features.CommandBar.Views;

/// <summary>A single-selection result list whose typing focus stays in the search field.</summary>
internal sealed class SearchResultList : StackPanel
{
    internal List<SearchResultButton> Items { get; } = [];

    protected override AutomationPeer OnCreateAutomationPeer() => new ListPeer(this);

    private sealed class ListPeer(SearchResultList owner) : FrameworkElementAutomationPeer(owner), ISelectionProvider
    {
        protected override string GetClassNameCore() => "SearchResults";
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.List;
        protected override List<AutomationPeer> GetChildrenCore() =>
            [.. owner.Items.Select(CreatePeerForElement).OfType<AutomationPeer>()];
        public override object? GetPattern(PatternInterface patternInterface) =>
            patternInterface == PatternInterface.Selection ? this : base.GetPattern(patternInterface);
        public bool CanSelectMultiple => false;
        public bool IsSelectionRequired => owner.Items.Count > 0;
        public IRawElementProviderSimple[] GetSelection() =>
            [.. owner.Items.Where(item => item.IsSelected).Select(CreatePeerForElement).OfType<AutomationPeer>().Select(ProviderFromPeer)];
    }
}

internal sealed class SearchResultButton(SearchResultList list, Action select, Action run) : Button
{
    internal bool IsSelected { get; init; }
    internal Button? HideAction { get; set; }

    protected override AutomationPeer OnCreateAutomationPeer() => new ResultPeer(this, list, select, run);

    internal void AnnounceSelected() =>
        UIElementAutomationPeer.FromElement(this)?.RaiseAutomationEvent(AutomationEvents.SelectionItemPatternOnElementSelected);

    private sealed class ResultPeer(SearchResultButton owner, SearchResultList list, Action select, Action run)
        : ButtonAutomationPeer(owner), ISelectionItemProvider, IInvokeProvider
    {
        protected override string GetClassNameCore() => "SearchResult";
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.ListItem;
        // The name carries the icon, two lines and primary action as one item; hiding stays a separate control.
        protected override List<AutomationPeer>? GetChildrenCore() => owner.HideAction is { } hide
            ? [CreatePeerForElement(hide)] : null;
        public override object? GetPattern(PatternInterface patternInterface) =>
            patternInterface is PatternInterface.SelectionItem or PatternInterface.Invoke ? this : base.GetPattern(patternInterface);
        public bool IsSelected => owner.IsSelected;
        public IRawElementProviderSimple SelectionContainer => ProviderFromPeer(CreatePeerForElement(list));
        public void Select() => Dispatch(select);
        public void AddToSelection() => Select();
        public void RemoveFromSelection()
        {
            if (owner.IsSelected)
                throw new InvalidOperationException("Search keeps one result selected.");
        }
        public void Invoke() => Dispatch(run);

        private void Dispatch(Action action)
        {
            if (!IsEnabled())
                throw new ElementNotEnabledException();
            owner.Dispatcher.BeginInvoke(action);
        }
    }
}
