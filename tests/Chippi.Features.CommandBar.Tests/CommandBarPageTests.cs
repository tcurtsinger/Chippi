// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Chippi.Core.Island;
using Chippi.Core.Modules;
using Chippi.Features.CommandBar.Search;
using Chippi.Features.CommandBar.Views;
using Chippi.Features.Sound.Mute;
using Chippi.Island.Tests;
using Microsoft.Extensions.Logging.Abstractions;

namespace Chippi.Features.CommandBar.Tests;

/// <summary>Pure controls and fake PC services: these tests never create a Window or send desktop input.</summary>
public sealed class CommandBarPageTests
{
    [Fact]
    public void Refresh_keeps_a_deliberately_chosen_result_even_with_an_empty_query() => WpfTestThread.Run(() =>
    {
        using var f = new Fixture();
        Assert.True(f.Page.Shown.Count >= 2);
        Press(f.Page, Key.Down);
        string selected = f.Selected.Key;

        f.Page.Refresh();

        Assert.Equal(selected, f.Selected.Key);
        Assert.Equal("", f.Page.Field.Text);
    });

    [Fact]
    public void Files_replace_the_automatic_web_selection_when_they_arrive() => WpfTestThread.Run(() =>
    {
        using var f = new Fixture();
        var found = new TaskCompletionSource<IReadOnlyList<FoundFile>>();
        f.Pc.Find = (_, _) => found.Task;
        f.Page.Field.Text = "budget";
        FindFiles(f.Page);
        Assert.Equal(ResultKind.Web, f.Selected.Kind);
        Assert.Contains(Texts(f.Page), text => text.StartsWith("Searching files", StringComparison.Ordinal));

        found.SetResult([Budget]);
        Drain();

        Assert.Equal(ResultKind.File, f.Selected.Kind);
        Assert.Equal("Budget.xlsx", f.Selected.Name);
        Assert.DoesNotContain(Texts(f.Page), text => text.StartsWith("Searching files", StringComparison.Ordinal));
    });

    [Fact]
    public void Files_do_not_replace_a_deliberately_chosen_web_result() => WpfTestThread.Run(() =>
    {
        using var f = new Fixture();
        var found = new TaskCompletionSource<IReadOnlyList<FoundFile>>();
        f.Pc.Find = (_, _) => found.Task;
        f.Page.Field.Text = "budget";
        FindFiles(f.Page);
        Press(f.Page, Key.Down); // Explicitly keeps the only row, rather than accepting the automatic default.

        found.SetResult([Budget]);
        Drain();

        Assert.Equal(ResultKind.Web, f.Selected.Kind);
        Assert.Contains(f.Page.Shown, row => row.Kind == ResultKind.File);
    });

    [Fact]
    public void A_pending_result_only_dispatches_once() => WpfTestThread.Run(() =>
    {
        using var f = new Fixture();
        var finished = new TaskCompletionSource();
        int dispatched = 0;
        f.Alpha = () => { dispatched++; return finished.Task; };
        f.Page.Field.Text = "Alpha";
        Result result = f.Selected;

        f.Page.Run(result);
        f.Page.Run(result);
        Assert.Equal(1, dispatched);
        Assert.Equal(0, f.Closed);

        finished.SetResult();
        Drain();
        Assert.Equal(1, f.Closed);
    });

    [Fact]
    public void An_old_completion_does_not_close_a_new_visit_or_release_its_pending_run() => WpfTestThread.Run(() =>
    {
        using var f = new Fixture();
        var oldRun = new TaskCompletionSource();
        var newRun = new TaskCompletionSource();
        int dispatched = 0;
        f.Alpha = () => ++dispatched == 1 ? oldRun.Task : newRun.Task;
        f.Page.Field.Text = "Alpha";
        f.Page.Run(f.Selected);

        f.Page.Deactivate();
        Invoke(f.Page, "Opened");
        f.Page.Field.Text = "Alpha";
        f.Page.Run(f.Selected);
        Assert.Equal(2, dispatched);

        oldRun.SetResult();
        Drain();
        Assert.Equal(0, f.Closed);
        f.Page.Run(f.Selected);
        Assert.Equal(2, dispatched);

        newRun.SetResult();
        Drain();
        Assert.Equal(1, f.Closed);
    });

    [Fact]
    public void No_results_is_explicit_after_file_search_finishes() => WpfTestThread.Run(() =>
    {
        using var f = new Fixture(new CommandBarSettings { Off = [ResultSwitch.Web] });
        f.Page.Field.Text = "zzzz_unmatched";
        FindFiles(f.Page);

        Assert.Empty(f.Page.Shown);
        Assert.Contains("No results", Texts(f.Page));
    });

    [Fact]
    public void Unavailable_files_leave_other_results_usable_and_retry_the_same_query() => WpfTestThread.Run(() =>
    {
        using var f = new Fixture();
        f.Pc.Find = (_, _) => Task.FromException<IReadOnlyList<FoundFile>>(new IOException("Indexer unavailable"));
        f.Page.Field.Text = "budget";
        FindFiles(f.Page);

        Assert.Contains(Texts(f.Page), text => text.StartsWith("Files unavailable", StringComparison.Ordinal));
        Assert.Equal(ResultKind.Web, f.Selected.Kind);
        var retry = Assert.Single(Elements(f.Page).OfType<Button>(), button => Equals(button.Content, "Retry"));
        f.Pc.Find = (_, _) => Task.FromResult<IReadOnlyList<FoundFile>>([Budget]);
        retry.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Drain();

        Assert.Equal(2, f.Pc.FindCalls);
        Assert.Equal("budget", f.Page.Field.Text);
        Assert.Equal(ResultKind.File, f.Selected.Kind);
        Assert.DoesNotContain(Texts(f.Page), text => text.StartsWith("Files unavailable", StringComparison.Ordinal));
    });

    [Fact]
    public void An_outdated_file_search_cannot_replace_the_current_query() => WpfTestThread.Run(() =>
    {
        using var f = new Fixture();
        var old = new TaskCompletionSource<IReadOnlyList<FoundFile>>();
        f.Pc.Find = (text, _) => text == "budget" ? old.Task : Task.FromResult<IReadOnlyList<FoundFile>>([]);
        f.Page.Field.Text = "budget";
        FindFiles(f.Page);
        f.Page.Field.Text = "Chrome";
        FindFiles(f.Page);

        old.SetResult([Budget]);
        Drain();

        Assert.Equal("Chrome", f.Page.Field.Text);
        Assert.DoesNotContain(f.Page.Shown, result => result.Kind == ResultKind.File);
        Assert.Equal(ResultKind.App, f.Selected.Kind);
    });

    [Fact]
    public void A_run_failure_retains_the_result_identity_and_explains_the_failure() => WpfTestThread.Run(() =>
    {
        using var f = new Fixture();
        f.Pc.SwitchError = new InvalidOperationException("Window closed");
        f.Page.Field.Text = "Project notes";
        Result window = Assert.Single(f.Page.Shown, result => result.Kind == ResultKind.Window);

        f.Page.Run(window);
        Drain();

        Assert.Contains(window.Name, Texts(f.Page));
        Assert.Contains(window.Detail, Texts(f.Page));
        Assert.Contains(Texts(f.Page), text => text.Contains("Window closed", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(0, f.Closed);
    });

    [Fact]
    public void Hidden_results_can_be_restored_inline() => WpfTestThread.Run(() =>
    {
        using var f = new Fixture();
        f.Page.Field.Text = "Chrome";
        string key = f.Selected.Key;
        Press(f.Page, Key.Delete, ModifierKeys.Shift);

        Assert.Contains(f.Settings.Value.Hidden, result => result.Key == key);
        Assert.DoesNotContain(f.Page.Shown, result => result.Key == key);
        Assert.Contains(Elements(f.Page).OfType<Button>(), button => Equals(button.Content, "Undo"));

        f.Page.UndoHidden();

        Assert.DoesNotContain(f.Settings.Value.Hidden, result => result.Key == key);
        Assert.Contains(f.Page.Shown, result => result.Key == key);
    });

    [Fact]
    public void Failed_hide_or_undo_keeps_the_saved_state_and_can_be_retried() => WpfTestThread.Run(() =>
    {
        using var f = new Fixture();
        f.Page.Field.Text = "Chrome";
        string key = f.Selected.Key;
        f.Settings.Fail = true;

        Press(f.Page, Key.Delete, ModifierKeys.Shift);
        Assert.Empty(f.Settings.Value.Hidden);
        Assert.Contains(f.Page.Shown, result => result.Key == key);

        f.Settings.Fail = false;
        Press(f.Page, Key.Delete, ModifierKeys.Shift);
        Assert.Contains(f.Settings.Value.Hidden, result => result.Key == key);
        f.Settings.Fail = true;
        f.Page.UndoHidden();
        Assert.Contains(f.Settings.Value.Hidden, result => result.Key == key);
        Assert.DoesNotContain(f.Page.Shown, result => result.Key == key);

        f.Settings.Fail = false;
        f.Page.UndoHidden();
        Assert.DoesNotContain(f.Settings.Value.Hidden, result => result.Key == key);
        Assert.Contains(f.Page.Shown, result => result.Key == key);
    });

    [Fact]
    public void App_and_window_rows_show_their_providers_actual_icons() => WpfTestThread.Run(() =>
    {
        using var f = new Fixture();
        f.Pc.Icon = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, new byte[] { 30, 100, 200, 255 }, 4);
        f.Pc.Icon.Freeze();
        f.Page.Field.Text = "Chrome";
        Drain();

        Assert.Contains("Chrome", f.Pc.AppIconRequests);
        Assert.Contains((nint)42, f.Pc.WindowIconRequests);
        Assert.Equal(2, Elements(f.Page).OfType<Image>().Count(image => ReferenceEquals(image.Source, f.Pc.Icon)));
    });

    [Fact]
    public void Results_expose_selection_action_and_position_while_typing_focus_stays_in_the_field() => WpfTestThread.Run(() =>
    {
        using var f = new Fixture();
        var list = Assert.Single(Elements(f.Page).OfType<SearchResultList>());
        var listPeer = UIElementAutomationPeer.CreatePeerForElement(list);
        var selection = Assert.IsAssignableFrom<ISelectionProvider>(listPeer.GetPattern(PatternInterface.Selection));
        Assert.False(selection.CanSelectMultiple);
        Assert.True(selection.IsSelectionRequired);
        Assert.Single(selection.GetSelection());
        Assert.Equal(f.Page.Shown.Count, listPeer.GetChildren().Count(peer => peer.GetAutomationControlType() == AutomationControlType.ListItem));
        Assert.Equal(AutomationControlType.List, listPeer.GetAutomationControlType());

        var second = list.Items[1];
        var secondPeer = UIElementAutomationPeer.CreatePeerForElement(second);
        var item = Assert.IsAssignableFrom<ISelectionItemProvider>(secondPeer.GetPattern(PatternInterface.SelectionItem));
        Assert.False(item.IsSelected);
        // WPF materializes the raw SelectionContainer provider only in a connected presentation source.
        // The control-only test checks the selection pattern and its behavior without opening a Window.
        item.Select();
        Drain();

        Assert.Equal(1, f.Page.Selected);
        var selected = list.Items[1];
        var selectedPeer = UIElementAutomationPeer.CreatePeerForElement(selected);
        Assert.True(Assert.IsAssignableFrom<ISelectionItemProvider>(selectedPeer.GetPattern(PatternInterface.SelectionItem)).IsSelected);
        Assert.False(selected.Focusable);
        Assert.Equal(2, AutomationProperties.GetPositionInSet(selected));
        Assert.Equal(f.Page.Shown.Count, AutomationProperties.GetSizeOfSet(selected));
        Assert.Contains(f.Selected.Verb, selectedPeer.GetName(), StringComparison.Ordinal);
        Assert.Contains($"2 of {f.Page.Shown.Count}", AutomationProperties.GetHelpText(f.Page.Field), StringComparison.Ordinal);
        Assert.IsAssignableFrom<IInvokeProvider>(selectedPeer.GetPattern(PatternInterface.Invoke));
    });

    [Fact]
    public void File_results_expose_the_full_path_in_the_tooltip_and_accessible_name() => WpfTestThread.Run(() =>
    {
        using var f = new Fixture();
        f.Pc.Find = (_, _) => Task.FromResult<IReadOnlyList<FoundFile>>([Budget]);
        f.Page.Field.Text = "budget";
        FindFiles(f.Page);
        var row = Assert.Single(Elements(f.Page).OfType<SearchResultButton>(), result =>
            AutomationProperties.GetName(result).StartsWith(Budget.Name, StringComparison.Ordinal));

        Assert.Equal(Budget.Path, row.ToolTip);
        Assert.Contains(Budget.Path, AutomationProperties.GetName(row), StringComparison.Ordinal);
        Assert.Contains("Open", AutomationProperties.GetName(row), StringComparison.Ordinal);
    });

    [Fact]
    public void A_queued_accessibility_action_cannot_run_an_old_query_with_the_same_result_key() => WpfTestThread.Run(() =>
    {
        using var f = new Fixture();
        f.Page.Field.Text = "budget";
        var web = Assert.Single(Elements(f.Page).OfType<SearchResultButton>());
        var peer = UIElementAutomationPeer.CreatePeerForElement(web);
        Assert.IsAssignableFrom<IInvokeProvider>(peer.GetPattern(PatternInterface.Invoke)).Invoke();

        f.Page.Field.Text = "Chrome"; // The Web key still exists, but its destination has changed.
        Drain();

        Assert.Empty(f.Pc.Opened);
        Assert.Equal(0, f.Closed);
        Assert.Equal("Chrome", f.Page.Field.Text);
    });

    private static readonly FoundFile Budget = new(@"C:\Work\Reports\Budget.xlsx", "Budget.xlsx", "Reports", false);

    private static void FindFiles(CommandBarPage page) => Invoke(page, "FindFiles");

    private static void Invoke(object owner, string name) => owner.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(owner, null);

    private static void Drain()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ContextIdle, () => frame.Continue = false);
        Dispatcher.PushFrame(frame);
    }

    private static void Press(CommandBarPage page, Key key, ModifierKeys modifiers = ModifierKeys.None)
    {
        page.Modifiers = () => modifiers;
        page.Field.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, new FakeSource(page), 0, key) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
    }

    private static IEnumerable<DependencyObject> Elements(DependencyObject root)
    {
        yield return root;
        foreach (object child in LogicalTreeHelper.GetChildren(root))
            if (child is DependencyObject element)
                foreach (var descendant in Elements(element))
                    yield return descendant;
    }

    private static IReadOnlyList<string> Texts(DependencyObject root) => [.. Elements(root).OfType<TextBlock>().Select(block => block.Text)];

    private sealed class FakeSource(Visual root) : PresentationSource
    {
        public override Visual RootVisual { get => root; set { } }
        public override bool IsDisposed => false;
        protected override CompositionTarget GetCompositionTargetCore() => null!;
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "chippi-tests", "search-page-" + Guid.NewGuid().ToString("N"));
        public FakePc Pc { get; } = new();
        public FakeSettings Settings { get; } = new();
        public CommandBarModule Module { get; }
        public CommandBarPage Page { get; }
        public Func<Task> Alpha { get; set; } = () => Task.CompletedTask;
        public int Closed { get; private set; }
        public Result Selected => Page.Shown[Page.Selected];

        public Fixture(CommandBarSettings? settings = null)
        {
            Settings.Value = settings ?? new CommandBarSettings();
            var ui = new FakeUi();
            Module = new CommandBarModule(() => Pc, () => new FakeHotkey());
            Module.StartAsync(new ModuleContext(CommandBarModule.Id, Settings, NullLogger.Instance, new IslandActivityHub().CreatePublisher(CommandBarModule.Id), ui)
            {
                Commands = () =>
                [
                    new ChippiCommand("alpha", "Alpha", "Folder", () => Alpha()) { Kind = ChippiCommandKind.Page, Detail = "Chippi" },
                    new ChippiCommand("beta", "Beta", "Folder", () => Task.CompletedTask) { Kind = ChippiCommandKind.Page, Detail = "Chippi" },
                    new ChippiCommand("gamma", "Gamma", "Folder", () => Task.CompletedTask) { Kind = ChippiCommandKind.Page, Detail = "Chippi" },
                ],
                DataDirectory = _directory,
                CloseIsland = () => Closed++,
            }, default).GetAwaiter().GetResult();
            Page = (CommandBarPage)Assert.Single(ui.Tiles).CreateSection();
            Invoke(Page, "Opened");
        }

        public void Dispose()
        {
            Page.Deactivate();
            if (Module.Search is { } search)
                foreach (var field in search.GetType().GetFields(BindingFlags.Instance | BindingFlags.NonPublic))
                    if (field.GetValue(search) is Uses uses)
                        uses.Saved.GetAwaiter().GetResult();
            Module.StopAsync(default).GetAwaiter().GetResult();
            if (Directory.Exists(_directory))
                Directory.Delete(_directory, recursive: true);
        }
    }

    private sealed class FakePc : IPc
    {
        public Func<string, CancellationToken, Task<IReadOnlyList<FoundFile>>> Find { get; set; } = (_, _) => Task.FromResult<IReadOnlyList<FoundFile>>([]);
        public int FindCalls { get; private set; }
        public Exception? SwitchError { get; set; }
        public BitmapSource? Icon { get; set; }
        public List<string> AppIconRequests { get; } = [];
        public List<string> Opened { get; } = [];
        public List<nint> WindowIconRequests { get; } = [];
        public IReadOnlyList<StartApp> Apps { get; } = [new("Chrome", "Chrome")];
        public event Action? AppsChanged { add { } remove { } }
        public void RefreshApps() { }
        public IReadOnlyList<OpenWindow> Windows() => [new(42, "Project notes", "Chrome")];
        public Task<IReadOnlyList<FoundFile>> FindFilesAsync(string text, int count, CancellationToken ct)
        {
            FindCalls++;
            return Find(text, ct);
        }
        public void Open(string target) => Opened.Add(target);
        public void Switch(nint window)
        {
            if (SwitchError is not null)
                throw SwitchError;
        }
        public Task<ImageSource?> AppIconAsync(StartApp app, int pixels)
        {
            AppIconRequests.Add(app.ParsingName);
            return Task.FromResult<ImageSource?>(Icon);
        }
        public Task<ImageSource?> WindowIconAsync(OpenWindow window, int pixels)
        {
            WindowIconRequests.Add(window.Handle);
            return Task.FromResult<ImageSource?>(Icon);
        }
        public void Dispose() { }
    }

    private sealed class FakeSettings : IModuleSettings
    {
        public CommandBarSettings Value { get; set; } = new();
        public bool Fail { get; set; }
        public T Get<T>() where T : class, new() => (T)(object)Value;
        public void Save<T>(T value) where T : class
        {
            if (Fail)
                throw new IOException("Disk unavailable");
            Value = (CommandBarSettings)(object)value;
        }
    }

    private sealed class FakeUi : IModuleUi
    {
        public List<IslandTileSpec> Tiles { get; } = [];
        public IIslandTile AddIslandTile(IslandTileSpec spec)
        {
            Tiles.Add(spec);
            return new IslandTileContribution(CommandBarModule.Id, spec);
        }
        public void SetSettingsPage(Func<string?, object> page) { }
    }

    private sealed class FakeHotkey : IMicHotkey
    {
        public bool Register(MicShortcut shortcut, Action pressed) => true;
        public void Dispose() { }
    }
}
