// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Text.RegularExpressions;

namespace Chippi.Audio;

public enum CableState
{
    /// <summary>No VB-CABLE endpoint at all.</summary>
    Missing,

    /// <summary>VB-CABLE's endpoints are there but not active (disabled in Sound settings, or the driver is off).</summary>
    Disabled,

    /// <summary>Exactly one active CABLE Input and CABLE Output pair.</summary>
    Ready,

    /// <summary>More than one active pair: Chippi won't guess which to use.</summary>
    Ambiguous,
}

/// <summary>CABLE Input (where Mic processing renders) and CABLE Output (what apps record from).</summary>
public sealed record CablePair(string RenderId, string CaptureId);

/// <summary>
/// Recognizes VB-CABLE by device identity, never by endpoint names (which users can rename, and a
/// real mic can be called "Cable Room Mic"). VB-CABLE's INF (vbMmeCable64_win10.inf, pack 45)
/// registers its KS filters with fixed reference strings: <c>VBAudioVACWDM2022_Out1_Topo_...</c>
/// for CABLE Input and <c>VBAudioVACWDM2022_In1_Topo_...</c> for CABLE Output. Each endpoint's
/// <see cref="AudioEndpoint.Filter"/> ends in one of those. "CABLE In 16ch" has its own reference
/// string, so it never matches the pair.
/// </summary>
public static partial class VbCable
{
    /// <summary>Any endpoint of VB-Audio's cable driver, including CABLE In 16ch.</summary>
    public static bool IsCable(AudioEndpoint endpoint) => AnyCable().IsMatch(endpoint.Filter);

    public static bool IsCableInput(AudioEndpoint endpoint) => endpoint.Flow == AudioFlow.Render && CableInput().IsMatch(endpoint.Filter);

    public static bool IsCableOutput(AudioEndpoint endpoint) => endpoint.Flow == AudioFlow.Capture && CableOutput().IsMatch(endpoint.Filter);

    /// <summary>Where VB-CABLE stands among <paramref name="endpoints"/> (both flows, any state).</summary>
    public static (CableState State, CablePair? Pair) Find(IReadOnlyList<AudioEndpoint> endpoints)
    {
        var inputs = endpoints.Where(IsCableInput).ToList();
        var outputs = endpoints.Where(IsCableOutput).ToList();
        if (inputs.Count == 0 && outputs.Count == 0)
            return (CableState.Missing, null);

        var pairs = (
            from input in inputs.Where(e => e.IsActive)
            from output in outputs.Where(e => e.IsActive)
            where PriorityPolicy.Eq(Adapter(input.Filter), Adapter(output.Filter))
            select new CablePair(input.Id, output.Id)).ToList();
        return pairs.Count switch
        {
            0 => (CableState.Disabled, null),
            1 => (CableState.Ready, pairs[0]),
            _ => (CableState.Ambiguous, null),
        };
    }

    // Both endpoints of one install hang off the same device: the filter path up to its reference string.
    private static string Adapter(string filter) => filter[..Math.Max(0, filter.LastIndexOf('\\'))];

    [GeneratedRegex(@"\\vbaudiovacwdm[^\\]*$", RegexOptions.IgnoreCase)]
    private static partial Regex AnyCable();

    [GeneratedRegex(@"\\vbaudiovacwdm[^\\_]*_out1_[^\\]*$", RegexOptions.IgnoreCase)]
    private static partial Regex CableInput();

    [GeneratedRegex(@"\\vbaudiovacwdm[^\\_]*_in1_[^\\]*$", RegexOptions.IgnoreCase)]
    private static partial Regex CableOutput();
}
