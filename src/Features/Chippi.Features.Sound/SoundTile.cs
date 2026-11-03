// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Windows;
using System.Windows.Controls;
using Chippi.Audio;
using Chippi.Core.Modules;
using Chippi.Features.Sound.Mute;
using Chippi.Features.Sound.Processing;
using Chippi.Features.Sound.Routing;

namespace Chippi.Features.Sound;

/// <summary>
/// The island's Audio destination. Its Devices tab combines independent routing, mute and processing modules.
/// </summary>
public static class SoundTile
{
    public const string Key = "sound";

    public const string Output = "Output";

    public const string Microphone = "Microphone";

    /// <summary>A section of the Sound page, in <paramref name="column"/>, at <paramref name="order"/> (lowest first).</summary>
    public static IslandTileSpec Section(int order, string column, Func<object> create) =>
        new(Key, "Audio", "Speaker", Order: 10, SectionOrder: order, create) { Column = column };

    /// <summary>Stacks the available device cards and sizes the processing strip to its controls and any warning.</summary>
    public static Grid DevicesBody(IReadOnlyList<FrameworkElement> sections)
    {
        var body = new Grid();
        var capture = sections.OfType<RoutingQuickView>().FirstOrDefault(v => v.Flow == AudioFlow.Capture);
        var microphone = sections.OfType<MicQuickView>().FirstOrDefault();
        microphone?.SetRouting(capture?.Module);
        foreach (var section in sections)
        {
            // When both modules contribute, the mute card includes routing's microphone picker.
            if (microphone is not null && ReferenceEquals(section, capture)) continue;
            int row = body.RowDefinitions.Count;
            body.RowDefinitions.Add(new RowDefinition { Height = section is ProcessingQuickView ? GridLength.Auto : new GridLength(1, GridUnitType.Star) });
            section.Margin = new Thickness(0, row == 0 ? 0 : IslandPage.Gap, 0, 0);
            Grid.SetRow(section, row);
            body.Children.Add(section);
        }
        return body;
    }
}
