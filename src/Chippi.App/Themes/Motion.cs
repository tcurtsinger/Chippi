// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.ComponentModel;
using System.Windows;

namespace Chippi.App.Themes;

/// <summary>
/// Reduced motion for the pet, island and flyout transitions (DESIGN.md §7, §9). Follows the
/// Windows "Animation effects" setting (<see cref="SystemParameters.ClientAreaAnimation"/>) unless
/// the user overrides it in Settings.
/// </summary>
public static class Motion
{
    private static bool? _override;

    static Motion() =>
        SystemParameters.StaticPropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SystemParameters.ClientAreaAnimation) && _override is null)
                Changed?.Invoke(null, EventArgs.Empty);
        };

    /// <summary>Raised when <see cref="IsReduced"/> may have changed.</summary>
    public static event EventHandler? Changed;

    /// <summary>True means no blink, no squish and instant mood and state swaps.</summary>
    public static bool IsReduced => _override ?? !SystemParameters.ClientAreaAnimation;

    /// <summary>The user's setting (AppSettings.ReduceMotion): null follows Windows.</summary>
    public static bool? Override
    {
        get => _override;
        set
        {
            if (_override == value)
                return;
            _override = value;
            Changed?.Invoke(null, EventArgs.Empty);
        }
    }

    /// <summary>For XAML bindings: <c>{Binding Source={x:Static t:Motion.Instance}, Path=IsReduced}</c>.</summary>
    public static MotionState Instance { get; } = new();

    public sealed class MotionState : INotifyPropertyChanged
    {
        internal MotionState() => Changed += (_, _) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsReduced)));

        public event PropertyChangedEventHandler? PropertyChanged;

        public bool IsReduced => Motion.IsReduced;
    }
}
