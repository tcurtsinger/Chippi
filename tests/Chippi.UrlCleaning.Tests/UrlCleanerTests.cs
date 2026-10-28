// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

namespace Chippi.UrlCleaning.Tests;

public sealed class UrlCleanerTests
{
    [Theory]
    [InlineData("https://example.org/p?utm_source=a&item=7#part", "https://example.org/p?item=7#part")]
    [InlineData("https://youtu.be/abc?si=share&t=30", "https://youtu.be/abc?t=30")]
    [InlineData("https://example.org/?si=keep&ref=keep", "https://example.org/?si=keep&ref=keep")]
    [InlineData("https://notyoutube.com/?si=keep", "https://notyoutube.com/?si=keep")]
    [InlineData("https://example.org/?%75tm_source=a&x=a%2Fb", "https://example.org/?x=a%2Fb")]
    public void Approved_examples(string input, string expected) => Assert.Equal(expected, UrlCleaner.Clean(input)?.Url);

    // Independent inventory from behavior-spec section 5; every named rule is exercised on its own.
    public static TheoryData<string, string> EveryRule
    {
        get
        {
            var data = new TheoryData<string, string>();
            Add("example.org", "fbclid gclid dclid gbraid wbraid msclkid yclid mc_cid mc_eid igshid twclid ttclid li_fat_id " +
                "mkt_tok _hsenc _hsmi __twitter_impression fb_action_ids fb_action_types fb_source mibextid");
            Add("example.org", "utm_source utm_medium utm_campaign utm_term utm_content utm_id utm_name utm_reader " +
                "utm_viz_id utm_pubreferrer utm_future_tracker utm_");
            Add("youtube.com", "si pp feature kw");
            Add("youtu.be", "si pp feature kw");
            Add("twitter.com", "s t cn src refsrc ref_src ref_url");
            Add("x.com", "s t cn src refsrc ref_src ref_url");
            Add("instagram.com", "igsh");
            Add("spotify.com", "si");
            Add("reddit.com", "correlation_id ref_campaign ref_source rdt share_id _branch_match_id $deep_link $3p $original_url");
            Add("tiktok.com", "u_code preview_pb _d _t _r timestamp user_id share_app_name share_iid");
            Add("bilibili.com", "spm_id_from from_spmid from_source share_source share_from share_medium share_plat " +
                "share_tag share_session_id msource refer_from seid unique_k vd_source plat_id buvid bbid up_id " +
                "is_story_h5 timestamp ts visit_id session_id broadcast_type is_room_feed");
            Add("xiaohongshu.com", "xhsshare author_share xsec_source share_from_user_hidden shareredid share_id " +
                "exsource app_version app_platform apptime appuid");
            return data;

            void Add(string host, string names)
            {
                foreach (var name in names.Split(' '))
                    data.Add(host, name);
            }
        }
    }

    [Theory]
    [MemberData(nameof(EveryRule))]
    public void Removes_each_rule_on_its_host_and_a_real_subdomain(string host, string name)
    {
        foreach (var address in new[] { host, "sub." + host })
        {
            var result = Assert.IsType<CleanUrlResult>(UrlCleaner.Clean($"https://{address}/p?keep=1&{name}=fake&keep=2#part"));
            Assert.Equal($"https://{address}/p?keep=1&keep=2#part", result.Url);
            Assert.Equal(new[] { name }, result.RemovedParameters);
        }
    }

    [Theory]
    [MemberData(nameof(EveryRule))]
    public void Matches_encoded_names_and_mixed_case_without_reencoding_the_url(string host, string name)
    {
        var upper = name.ToUpperInvariant();
        var encoded = string.Concat(upper.Select(c => "%" + ((int)c).ToString("X2")));
        var result = Assert.IsType<CleanUrlResult>(UrlCleaner.Clean($"HTTPS://{host.ToUpperInvariant()}/%2fp?{encoded}=fake&v=a%2fb#%2F"));
        Assert.Equal($"HTTPS://{host.ToUpperInvariant()}/%2fp?v=a%2fb#%2F", result.Url);
        Assert.Equal(new[] { upper }, result.RemovedParameters);
    }

    [Theory]
    [InlineData("youtube.com", "si")]
    [InlineData("youtu.be", "kw")]
    [InlineData("twitter.com", "t")]
    [InlineData("x.com", "s")]
    [InlineData("instagram.com", "igsh")]
    [InlineData("spotify.com", "si")]
    [InlineData("reddit.com", "share_id")]
    [InlineData("tiktok.com", "timestamp")]
    [InlineData("bilibili.com", "session_id")]
    [InlineData("xiaohongshu.com", "share_id")]
    public void Site_rules_do_not_leak_to_lookalike_or_unrelated_hosts(string site, string name)
    {
        foreach (var host in new[] { "not" + site, site + ".example.org", "example.org" })
        {
            var input = $"https://{host}/?{name}=keep";
            var result = Assert.IsType<CleanUrlResult>(UrlCleaner.Clean(input));
            Assert.Equal(input, result.Url);
            Assert.Empty(result.RemovedParameters);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("example.org/?utm_source=x")]
    [InlineData("ftp://example.org/?utm_source=x")]
    [InlineData("file:///C:/fake.txt")]
    [InlineData("javascript:alert(1)")]
    [InlineData("https://")]
    [InlineData("https:///bad")]
    [InlineData("https://example.org/a b")]
    [InlineData("https://example.org/\tbad")]
    [InlineData("https://example.org/\u2003bad")]
    [InlineData("https://example.org/\nhttps://another.example.org/")]
    [InlineData("https://example.org\\bad")]
    public void Rejects_non_web_links_or_more_than_one_input(string? input) => Assert.Null(UrlCleaner.Clean(input));

    [Theory]
    [InlineData("https://example.org/no-query#?utm_source=fragment", "https://example.org/no-query#?utm_source=fragment")]
    [InlineData("https://example.org/?", "https://example.org/?")]
    [InlineData("https://example.org/?utm_source=x#?utm_source=fragment", "https://example.org/#?utm_source=fragment")]
    [InlineData("http://example.org/?gclid#", "http://example.org/#")]
    [InlineData("https://example.org/?utm_source=&fbclid=x", "https://example.org/")]
    [InlineData("https://example.org/?utm_source=x&&a=1&", "https://example.org/?&a=1&")]
    [InlineData("https://example.org/?x=utm_source%3Dkeep&%75tm_campaign=x&a=", "https://example.org/?x=utm_source%3Dkeep&a=")]
    [InlineData("https://youtube.com@fake.example.org/?si=keep", "https://youtube.com@fake.example.org/?si=keep")]
    [InlineData("https://fake.example.org@youtube.com:443/?si=remove&t=1", "https://fake.example.org@youtube.com:443/?t=1")]
    [InlineData("https://example.org/路径?utm_source=x&名字=值#片段", "https://example.org/路径?名字=值#片段")]
    public void Preserves_structure_and_spelling(string input, string expected) => Assert.Equal(expected, UrlCleaner.Clean(input)?.Url);

    [Fact]
    public void Reports_distinct_decoded_names_in_first_seen_order_and_cleans_idempotently()
    {
        var result = Assert.IsType<CleanUrlResult>(UrlCleaner.Clean(
            "https://x.com/?UTM_SOURCE=1&utm_source=2&%74=3&t=4&keep=5&fbclid=6"));
        Assert.Equal("https://x.com/?keep=5", result.Url);
        Assert.Equal(new[] { "UTM_SOURCE", "t", "fbclid" }, result.RemovedParameters);
        var again = Assert.IsType<CleanUrlResult>(UrlCleaner.Clean(result.Url));
        Assert.Equal(result.Url, again.Url);
        Assert.Empty(again.RemovedParameters);
    }

    [Fact]
    public void Trims_only_surrounding_whitespace_and_preserves_nontracking_parameters()
    {
        const string input = "HTTPS://EXAMPLE.org:443/a/../b?auth=fake%2Bvalue&ref=keep&a=1&a=2#utm_source=keep";
        var result = Assert.IsType<CleanUrlResult>(UrlCleaner.Clean(" \t" + input + "\r\n"));
        Assert.Equal(input, result.Url);
        Assert.Empty(result.RemovedParameters);
    }
}
