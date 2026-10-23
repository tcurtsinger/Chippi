// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Chippi.App.Shell;
using Chippi.Core.Diagnostics;
using Chippi.Core.Modules;
using Chippi.UI.Controls;

namespace Chippi.App.Settings.Pages;

/// <summary>
/// The Features hub (DESIGN.md §5): count, preset segment, search, then each group as an eyebrow
/// over a 3-column grid of cards with 12px gaps. Install switches act live; the preset and the
/// cards follow the registry, whoever changed it.
/// </summary>
internal sealed class FeaturesPage : UserControl
{
    private readonly ShellContext _shell;
    private readonly Action<FeatureEntry> _openModulePage;
    private readonly List<HubCard> _cards = [];
    private readonly List<(FrameworkElement Eyebrow, Panel Grid, List<HubCard> Cards)> _groups = [];
    private readonly TextBlock _count = new();
    private readonly SegmentedControl _presets;
    private readonly SearchBox _search;
    private readonly TextBlock _noMatches;
    private readonly TextBlock _message = SettingsUi.Message();
    private bool _syncing;

    public FeaturesPage(ShellContext shell, Action<FeatureEntry> openModulePage)
    {
        _shell = shell;
        _openModulePage = openModulePage;

        _count.SetResourceReference(StyleProperty, "NumberTextStyle");
        _count.SetResourceReference(TextBlock.ForegroundProperty, "TextTertiaryBrush");
        AutomationProperties.SetLiveSetting(_count, AutomationLiveSetting.Polite);

        _presets = SettingsUi.Segments("Preset",
            Enum.GetValues<FeaturePreset>().Select(p => (p.DisplayName(), p)), shell.Registry.Preset);
        _presets.SelectionChanged += OnPresetChosen;

        _search = new SearchBox { Placeholder = "Search features", Margin = new Thickness(0, 12, 0, 0) };
        AutomationProperties.SetName(_search, "Search features");
        _search.TextChanged += (_, _) => ApplySearch();

        _noMatches = SettingsUi.Styled(new TextBlock { Text = "No features match.", Margin = new Thickness(2, 22, 0, 0), Visibility = Visibility.Collapsed }, "CaptionTextStyle");

        var page = new StackPanel();
        page.Children.Add(SettingsUi.Header("Features", "Install only what you use. Uninstalled features never load, and reinstalling one brings its settings back.", _count));
        page.Children.Add(_presets);
        page.Children.Add(_search);
        page.Children.Add(_message);
        page.Children.Add(_noMatches);
        BuildGroups(page);
        Content = page;

        Refresh();
        // Subscribed only while shown, so a page left behind by navigation is not kept alive.
        Loaded += (_, _) =>
        {
            shell.Registry.Changed += OnRegistryChanged;
            Refresh();
        };
        Unloaded += (_, _) => shell.Registry.Changed -= OnRegistryChanged;
    }

    private void BuildGroups(Panel page)
    {
        foreach (var group in Enum.GetValues<FeatureGroup>())
        {
            var entries = _shell.Registry.Features.Where(f => f.Descriptor.Group == group).ToList();
            if (entries.Count == 0)
                continue;

            var eyebrow = SettingsUi.Eyebrow(group.DisplayName());
            var grid = new UniformGrid { Columns = 3, Margin = new Thickness(-6) };
            var cards = new List<HubCard>();
            foreach (var entry in entries)
            {
                var card = new HubCard(entry) { Margin = new Thickness(6) };
                card.InstallToggled += OnInstallToggled;
                card.OpenRequested += (_, _) =>
                {
                    if (entry.SettingsPage is not null)
                        _openModulePage(entry);
                };
                grid.Children.Add(card);
                cards.Add(card);
                _cards.Add(card);
            }
            page.Children.Add(eyebrow);
            page.Children.Add(grid);
            _groups.Add((eyebrow, grid, cards));
        }
    }

    private void OnRegistryChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(Refresh);

    /// <summary>Re-reads the registry into the count, the preset and every card, without rebuilding (focus stays put).</summary>
    private void Refresh()
    {
        _syncing = true;
        try
        {
            var registry = _shell.Registry;
            _count.Text = $"{registry.InstalledCount} of {registry.Features.Count} installed";
            _presets.SelectedItem = _presets.Items.OfType<SegmentItem>().FirstOrDefault(i => Equals(i.Value, registry.Preset));
            foreach (var card in _cards)
                card.Sync();
        }
        finally
        {
            _syncing = false;
        }
    }

    private async void OnPresetChosen(object sender, SelectionChangedEventArgs e)
    {
        if (_syncing || _presets.SelectedItem is not SegmentItem { Value: FeaturePreset preset } || preset == _shell.Registry.Preset)
            return;
        try
        {
            await _shell.Registry.ApplyPresetAsync(preset);
            _message.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            Log.Error($"Applying the {preset} preset failed", ex);
            SettingsUi.Show(_message, $"Couldn't apply {preset.DisplayName()}: {ex.Message}", problem: true);
        }
        Refresh();
    }

    private async void OnInstallToggled(object? sender, bool install)
    {
        if (_syncing || sender is not HubCard card)
            return;
        try
        {
            await _shell.Registry.SetInstalledAsync(card.Entry.Id, install);
            _message.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            Log.Error($"{(install ? "Installing" : "Uninstalling")} {card.Entry.Id} failed", ex);
            SettingsUi.Show(_message, $"Couldn't {(install ? "install" : "uninstall")} {card.Entry.Descriptor.Name}: {ex.Message}", problem: true);
        }
        Refresh();
    }

    private void ApplySearch()
    {
        string query = _search.Text.Trim();
        bool any = false;
        foreach (var (eyebrow, grid, cards) in _groups)
        {
            int shown = 0;
            foreach (var card in cards)
            {
                var d = card.Entry.Descriptor;
                bool match = query.Length == 0
                    || d.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase)
                    || d.Description.Contains(query, StringComparison.CurrentCultureIgnoreCase);
                card.Visibility = match ? Visibility.Visible : Visibility.Collapsed;
                if (match)
                    shown++;
            }
            var visibility = shown > 0 ? Visibility.Visible : Visibility.Collapsed;
            eyebrow.Visibility = visibility;
            grid.Visibility = visibility;
            any |= shown > 0;
        }
        _noMatches.Visibility = any ? Visibility.Collapsed : Visibility.Visible;
    }
}

/// <summary>
/// A hub card for one registry entry. The switch reports user toggles through
/// <see cref="InstallToggled"/>; when the feature is installed and has a settings page, the card
/// itself opens it (click, Enter or Space) and takes keyboard focus for that.
/// </summary>
internal sealed class HubCard : FeatureCard
{
    private bool _syncing;

    public HubCard(FeatureEntry entry)
    {
        Entry = entry;
        SetResourceReference(StyleProperty, typeof(FeatureCard));
        FocusVisualStyle = (Style)Application.Current.FindResource("FocusRing8");
        var d = entry.Descriptor;
        FeatureName = d.Name;
        Description = d.Description;
        Icon = LucideIcons.Get(d.Icon);
        IsComingSoon = entry.IsComingSoon;
        Sync();
    }

    public FeatureEntry Entry { get; }

    public event EventHandler<bool>? InstallToggled;

    public event EventHandler? OpenRequested;

    private bool HasPage => Entry.IsInstalled && Entry.SettingsPage is not null;

    /// <summary>Copies the entry's state onto the card without raising <see cref="InstallToggled"/>.</summary>
    public void Sync()
    {
        _syncing = true;
        try
        {
            IsInstalled = Entry.IsInstalled;
            IEnumerable<FeatureBadge> badges = Entry.Descriptor.Badges;
            if (Entry.IsInstalled && !Entry.IsRunning && Entry.LastError is not null)
                badges = badges.Append(FeatureBadge.Needs("Couldn't start"));
            Badges = badges.ToList();
        }
        finally
        {
            _syncing = false;
        }

        Focusable = HasPage;
        Cursor = HasPage ? Cursors.Hand : null;
        ToolTip = HasPage ? $"Open {Entry.Descriptor.Name} settings" : null;
        AutomationProperties.SetName(this, HasPage ? $"{Entry.Descriptor.Name} settings" : Entry.Descriptor.Name);
        AutomationProperties.SetHelpText(this, Entry.Descriptor.Description);
        // Invoke comes and goes with the page; tell clients the patterns changed.
        UIElementAutomationPeer.FromElement(this)?.InvalidatePeer();
    }

    /// <summary>Screen readers and switch access open the settings page through Invoke, as a click or Enter does.</summary>
    protected override AutomationPeer OnCreateAutomationPeer() => new HubCardAutomationPeer(this);

    private sealed class HubCardAutomationPeer(HubCard owner) : FeatureCardAutomationPeer(owner), IInvokeProvider
    {
        protected override AutomationControlType GetAutomationControlTypeCore() =>
            owner.HasPage ? AutomationControlType.Button : AutomationControlType.Group;

        public override object? GetPattern(PatternInterface patternInterface) =>
            patternInterface == PatternInterface.Invoke && owner.HasPage ? this : base.GetPattern(patternInterface);

        void IInvokeProvider.Invoke()
        {
            if (!IsEnabled() || !owner.HasPage)
                throw new ElementNotEnabledException();
            owner.OpenRequested?.Invoke(owner, EventArgs.Empty);
        }
    }

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == IsInstalledProperty && !_syncing)
            InstallToggled?.Invoke(this, (bool)e.NewValue);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        // Clicks on the switch belong to the switch.
        if (!HasPage || e.Handled || IsInside<ToggleButton>(e.OriginalSource as DependencyObject))
            return;
        e.Handled = true;
        OpenRequested?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (HasPage && !e.Handled && e.OriginalSource == this && e.Key is Key.Enter or Key.Space)
        {
            e.Handled = true;
            OpenRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    private bool IsInside<T>(DependencyObject? node) where T : DependencyObject
    {
        for (; node is not null && node != this; node = VisualTreeHelper.GetParent(node))
        {
            if (node is T)
                return true;
        }
        return false;
    }
}
