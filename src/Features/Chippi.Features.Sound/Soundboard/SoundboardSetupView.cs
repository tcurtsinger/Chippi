// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Chippi.Core.Modules;

namespace Chippi.Features.Sound.Soundboard;

/// <summary>Driver setup is an explicit user action; adding and previewing sounds never requires it.</summary>
internal sealed class SoundboardSetupView : StackPanel
{
    private readonly Func<IFeatureRequirement> _createRequirement;
    private readonly Func<string?> _sendUnavailable;
    private readonly Func<IDisposable> _holdOpen;
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Margin = new(0, 12, 0, 8) };
    private readonly TextBlock _error = new() { TextWrapping = TextWrapping.Wrap, Margin = new(0, 8, 0, 0) };
    private readonly Button _action = new() { HorizontalAlignment = HorizontalAlignment.Left, MinWidth = 80 };
    private IFeatureRequirement? _requirement;
    private CancellationTokenSource? _lifetime;
    private bool _busy;

    public SoundboardSetupView(SoundboardModule module) : this(module.CreateRequirement, () => module.SendUnavailableReason, module.HoldOpen) { }

    internal SoundboardSetupView(Func<IFeatureRequirement> createRequirement, Func<string?> sendUnavailable, Func<IDisposable> holdOpen)
    {
        _createRequirement = createRequirement;
        _sendUnavailable = sendUnavailable;
        _holdOpen = holdOpen;
        MaxWidth = 560;
        HorizontalAlignment = HorizontalAlignment.Left;
        Children.Add(new TextBlock { Text = "Virtual mic", FontSize = 16, FontWeight = FontWeights.SemiBold });
        Children.Add(new TextBlock { Text = "Choose CABLE Output as the microphone in your game or call app. Chippi sends your voice and sounds there.",
            TextWrapping = TextWrapping.Wrap, Margin = new(0, 8, 0, 0) });
        Children.Add(new TextBlock { Text = "Mute mic silences your voice. Stop ends the sound. Hear changes only what you hear locally.",
            TextWrapping = TextWrapping.Wrap, Margin = new(0, 8, 0, 0) });
        _status.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        AutomationProperties.SetLiveSetting(_status, AutomationLiveSetting.Polite);
        _error.SetResourceReference(TextBlock.ForegroundProperty, "WarningTextBrush");
        AutomationProperties.SetLiveSetting(_error, AutomationLiveSetting.Polite);
        Children.Add(_status);
        Children.Add(_action);
        Children.Add(_error);
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        _action.Click += async (_, _) =>
        {
            if (_lifetime is null || _busy) return;
            if (_requirement is null) { Connect(); return; }
            var lifetime = _lifetime;
            var requirement = _requirement;
            _busy = true;
            Refresh();
            _error.Text = "";
            try
            {
                using var hold = _holdOpen();
                if (requirement.State == RequirementState.Missing)
                    await requirement.InstallAsync(lifetime.Token);
                else requirement.OpenSettings();
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                if (ReferenceEquals(lifetime, _lifetime)) _error.Text = $"Setup failed · {ex.Message}";
            }
            finally
            {
                if (ReferenceEquals(lifetime, _lifetime)) { _busy = false; Refresh(); }
            }
        };
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_lifetime is not null) return;
        _lifetime = new();
        Connect();
    }

    private void Connect()
    {
        _error.Text = "";
        try
        {
            _requirement = _createRequirement();
            _requirement.StateChanged += StateChanged;
            Refresh();
        }
        catch (Exception ex)
        {
            if (_requirement is not null) { _requirement.StateChanged -= StateChanged; _requirement.Dispose(); _requirement = null; }
            _error.Text = $"Setup failed · {ex.Message}";
            _status.Text = "Virtual mic status is unavailable";
            _action.Content = "Retry";
            _action.IsEnabled = true;
        }
    }

    private void StateChanged(object? sender, EventArgs e)
    {
        if (!ReferenceEquals(sender, _requirement)) return;
        if (Dispatcher.CheckAccess()) Refresh();
        else if (!Dispatcher.HasShutdownStarted) Dispatcher.BeginInvoke(() => { if (ReferenceEquals(sender, _requirement)) Refresh(); });
    }

    private void Refresh()
    {
        if (_requirement is null) return;
        var state = _requirement.State;
        _status.Text = state switch
        {
            RequirementState.Missing => "Install VB-CABLE to send sounds. Local preview is already available.",
            RequirementState.Installing => "Installing virtual mic…",
            RequirementState.RestartNeeded => "Restart Windows to finish setup",
            RequirementState.Disabled => "Enable VB-CABLE in Windows Sound settings",
            RequirementState.Ready => _sendUnavailable() ?? "Virtual mic is ready",
            _ => "Virtual mic is unavailable on this PC",
        };
        _action.Content = state == RequirementState.Missing ? "Install" : "Sound settings";
        _action.IsEnabled = !_busy && state is RequirementState.Missing or RequirementState.Ready or RequirementState.Disabled;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _lifetime?.Cancel();
        _lifetime?.Dispose(); _lifetime = null;
        _busy = false;
        if (_requirement is not null) { _requirement.StateChanged -= StateChanged; _requirement.Dispose(); _requirement = null; }
    }
}
