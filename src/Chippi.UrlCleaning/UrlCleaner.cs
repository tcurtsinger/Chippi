// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

namespace Chippi.UrlCleaning;

/// <summary>A valid web link, with the distinct parameter names removed in encounter order.</summary>
public sealed record CleanUrlResult(string Url, IReadOnlyList<string> RemovedParameters);

/// <summary>
/// Pure, offline tracking-parameter removal (docs/phase-5/behavior-spec.md section 5).
/// Clipboard access, automatic-mode exclusions and history belong to the caller.
/// </summary>
public static class UrlCleaner
{
    private static readonly HashSet<string> GlobalNames = Names(
        "fbclid gclid dclid gbraid wbraid msclkid yclid mc_cid mc_eid igshid twclid ttclid li_fat_id " +
        "mkt_tok _hsenc _hsmi __twitter_impression fb_action_ids fb_action_types fb_source mibextid");

    private static readonly Dictionary<string, HashSet<string>> SiteNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["youtube.com"] = Names("si pp feature kw"),
        ["youtu.be"] = Names("si pp feature kw"),
        ["twitter.com"] = Names("s t cn src refsrc ref_src ref_url"),
        ["x.com"] = Names("s t cn src refsrc ref_src ref_url"),
        ["instagram.com"] = Names("igsh"),
        ["spotify.com"] = Names("si"),
        ["reddit.com"] = Names("correlation_id ref_campaign ref_source rdt share_id _branch_match_id $deep_link $3p $original_url"),
        ["tiktok.com"] = Names("u_code preview_pb _d _t _r timestamp user_id share_app_name share_iid"),
        ["bilibili.com"] = Names("spm_id_from from_spmid from_source share_source share_from share_medium share_plat " +
            "share_tag share_session_id msource refer_from seid unique_k vd_source plat_id buvid bbid up_id " +
            "is_story_h5 timestamp ts visit_id session_id broadcast_type is_room_feed"),
        ["xiaohongshu.com"] = Names("xhsshare author_share xsec_source share_from_user_hidden shareredid share_id " +
            "exsource app_version app_platform apptime appuid"),
    };

    /// <summary>
    /// Returns null unless the trimmed input is one absolute HTTP(S) URL without internal whitespace.
    /// Only removed query pairs change; the original path, kept pairs and fragment retain their spelling.
    /// A valid unchanged URL returns an empty <see cref="CleanUrlResult.RemovedParameters"/> list.
    /// </summary>
    public static CleanUrlResult? Clean(string? input)
    {
        if (input is null)
            return null;
        var text = input.Trim();
        if (text.Length == 0 || text.Any(char.IsWhiteSpace) || text.Contains('\\') ||
            !Uri.TryCreate(text, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
            uri.Host.Length == 0 || !uri.IsWellFormedOriginalString())
            return null;

        var fragment = text.IndexOf('#');
        if (fragment < 0)
            fragment = text.Length;
        var query = text.IndexOf('?');
        if (query < 0 || query >= fragment)
            return new(text, Array.Empty<string>());

        var kept = new List<string>();
        var removed = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in text[(query + 1)..fragment].Split('&'))
        {
            var equals = pair.IndexOf('=');
            var name = Uri.UnescapeDataString(equals < 0 ? pair : pair[..equals]);
            if (IsTracker(uri.Host, name))
            {
                if (seen.Add(name))
                    removed.Add(name);
            }
            else
                kept.Add(pair);
        }

        if (removed.Count == 0)
            return new(text, Array.Empty<string>());
        var cleaned = text[..query] + (kept.Count == 0 ? "" : "?" + string.Join('&', kept)) + text[fragment..];
        return new(cleaned, removed.AsReadOnly());
    }

    private static bool IsTracker(string host, string name) =>
        name.StartsWith("utm_", StringComparison.OrdinalIgnoreCase) || GlobalNames.Contains(name) ||
        SiteNames.Any(site =>
            (host.Equals(site.Key, StringComparison.OrdinalIgnoreCase) ||
             host.EndsWith("." + site.Key, StringComparison.OrdinalIgnoreCase)) && site.Value.Contains(name));

    private static HashSet<string> Names(string names) => new(names.Split(' '), StringComparer.OrdinalIgnoreCase);
}
