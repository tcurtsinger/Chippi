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
    private readonly SoundboardModule _module;
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Margin = new(0, 12, 0, 8) };
    private readonly TextBlock _error = new() { TextWrapping = TextWrapping.Wrap, Margin = new(0, 8, 0, 0) };
    private readonly Button _action = new() { HorizontalAlignment = HorizontalAlignment.Left, MinWidth = 80 };
    private IFeatureRequirement? _requirement;
    private CancellationTokenSource? _lifetime;

    public SoundboardSetupView(SoundboardModule module)
    {
        _module = module;
        MaxWidth = 560;
        HorizontalAlignment = HorizontalAlignment.Left;
        Children.Add(new TextBlock { Text = "Virtual mic", FontSize = 16, FontWeight = FontWeights.SemiBold });
        Children.Add(new TextBlock { Text = "Choose CABLE Output as the microphone in your game or call app. Chippi sends your voice and sounds there.",
            TextWrapping = TextWrapping.Wrap, Margin = new(0, 8, 0, 0) });
        Children.Add(new TextBlock { Text = "Mute mic silences your voice. Stop ends the sound. Hear changes only what you hear locally.",
            TextWrapping = TextWrapping.Wrap, Margin = new(0, 8, 0, 0) });
        _status.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        _error.SetResourceReference(TextBlock.ForegroundProperty, "WarningTextBrush");
        AutomationProperties.SetLiveSetting(_error, AutomationLiveSetting.Polite);
        Children.Add(_status);
        Children.Add(_action);
        Children.Add(_error);
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        _action.Click += async (_, _) =>
        {
            if (_requirement is null || _lifetime is null) return;
            _error.Text = "";
            try
            {
                using var hold = _module.HoldOpen();
                if (_requirement.State == RequirementState.Missing)
                    await _requirement.InstallAsync(_lifetime.Token);
                else _requirement.OpenSettings();
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { _error.Text = $"Setup failed · {ex.Message}"; }
            Refresh();
        };
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_requirement is not null) return;
        try
        {
            _lifetime = new();
            _requirement = _module.CreateRequirement();
            _requirement.StateChanged += StateChanged;
            Refresh();
        }
        catch (Exception ex) { _error.Text = $"Setup unavailable · {ex.Message}"; _action.IsEnabled = false; }
    }

    private void StateChanged(object? sender, EventArgs e)
    {
        if (Dispatcher.CheckAccess()) Refresh();
        else if (!Dispatcher.HasShutdownStarted) Dispatcher.BeginInvoke(Refresh);
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
            RequirementState.Ready => _module.SendUnavailableReason ?? "Virtual mic is ready",
            _ => "Virtual mic is unavailable on this PC",
        };
        _action.Content = state == RequirementState.Missing ? "Install" : "Sound settings";
        _action.IsEnabled = state is RequirementState.Missing or RequirementState.Ready or RequirementState.Disabled;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _lifetime?.Cancel();
        _lifetime?.Dispose(); _lifetime = null;
        if (_requirement is not null) { _requirement.StateChanged -= StateChanged; _requirement.Dispose(); _requirement = null; }
    }
}
