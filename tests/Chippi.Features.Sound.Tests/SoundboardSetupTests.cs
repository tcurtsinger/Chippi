// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Chippi.Core.Modules;
using Chippi.Features.Sound.Soundboard;

namespace Chippi.Features.Sound.Tests;

/// <summary>Unhosted setup controls with fake requirements. No driver, endpoint query or Settings launch.</summary>
public sealed class SoundboardSetupTests
{
    [Fact]
    public Task Status_failure_can_retry_without_leaving_setup() => SoundUiThread.Sta(() =>
    {
        var requirement = new Requirement();
        int attempts = 0;
        var view = new SoundboardSetupView(() => ++attempts == 1 ? throw new InvalidOperationException("Unavailable") : requirement,
            () => null, () => new Lease());
        view.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
        var action = Assert.Single(view.Children.OfType<Button>());
        Assert.Equal("Retry", action.Content);
        Assert.True(action.IsEnabled);
        SoundboardUiTests.Click(action);
        Assert.Equal("Install", action.Content);
        Assert.Contains(view.Children.OfType<TextBlock>(), text => text.Text.Contains("Local preview is already available"));
        view.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
        Assert.True(requirement.Disposed);
        Assert.Null(PresentationSource.FromVisual(view));
    });

    [Fact]
    public Task Pending_setup_is_single_flight_and_old_errors_cannot_replace_reopened_state() => SoundUiThread.Sta(async () =>
    {
        var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var old = new Requirement { Install = pending.Task };
        var current = new Requirement { State = RequirementState.Ready };
        int created = 0;
        var view = new SoundboardSetupView(() => ++created == 1 ? old : current, () => "Choose one VB-CABLE pair in Windows", () => new Lease());
        view.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
        var action = Assert.Single(view.Children.OfType<Button>());
        SoundboardUiTests.Click(action);
        SoundboardUiTests.Click(action); // Routed invocation bypasses disabled UI; the handler still guards reentry.
        Assert.Equal(1, old.Installs);
        Assert.False(action.IsEnabled);
        view.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
        Assert.True(old.Token.IsCancellationRequested);
        view.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
        pending.SetException(new InvalidOperationException("Old installation failed"));
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        Assert.Equal("Sound settings", action.Content);
        Assert.True(action.IsEnabled);
        Assert.DoesNotContain(view.Children.OfType<TextBlock>(), text => text.Text.Contains("Old installation failed"));
        Assert.Contains(view.Children.OfType<TextBlock>(), text => text.Text == "Choose one VB-CABLE pair in Windows");
        view.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
    });

    [Theory]
    [InlineData(RequirementState.Installing)]
    [InlineData(RequirementState.RestartNeeded)]
    [InlineData(RequirementState.Unsupported)]
    public Task Setup_unavailable_states_disable_the_action(RequirementState state) => SoundUiThread.Sta(() =>
    {
        var view = new SoundboardSetupView(() => new Requirement { State = state }, () => null, () => new Lease());
        view.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
        Assert.False(Assert.Single(view.Children.OfType<Button>()).IsEnabled);
        view.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
    });

    private sealed class Requirement : IFeatureRequirement
    {
        public RequirementState State { get; set; } = RequirementState.Missing;
        public event EventHandler? StateChanged { add { } remove { } }
        public Task Install { get; init; } = Task.CompletedTask;
        public int Installs;
        public bool Disposed;
        public CancellationToken Token;
        public Task InstallAsync(CancellationToken ct) { Installs++; Token = ct; return Install; }
        public void OpenSettings() => throw new InvalidOperationException("Native Settings launch is not allowed in this test");
        public void Dispose() => Disposed = true;
    }
    private sealed class Lease : IDisposable { public void Dispose() { } }
}
