// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Globalization;
using System.Text.Json;

namespace Chippi.Features.AgentUsage.Readers;

/// <summary>
/// Claude Code's limits, as <c>/usage</c> shows them: <c>claude</c> starts for a moment in its headless control mode with
/// no tools, MCP servers, hooks or settings, answers <c>initialize</c> and <c>get_usage</c>, and ends. Claude Code signs
/// itself in, and nothing goes to a model. None of this is documented (checked on 2.1.314, 2026-11-01): a request it
/// doesn't know is Too old, and no sign-in (<c>tokenSource: "none"</c>, no limits) is Signed out. Signed in, its
/// <c>rate_limits</c> lists the plan's limits (<c>limits</c>: <c>session</c>, <c>weekly_all</c>, a <c>weekly_scoped</c> one per
/// model) and also keys them (<c>five_hour</c>, <c>seven_day</c>, <c>seven_day_opus</c>…), beside spend and credits, which
/// aren't shown.
/// </summary>
internal static class ClaudeLimits
{
    public static IReadOnlyList<string> Arguments { get; } =
    [
        "--print", "--input-format", "stream-json", "--output-format", "stream-json", "--verbose", "--no-session-persistence",
        "--tools", "", "--strict-mcp-config", "--mcp-config", """{"mcpServers":{}}""", "--setting-sources", "",
        "--settings", """{"disableAllHooks":true}""",
    ];

    private const string InitializeId = "chippi-initialize";
    private const string UsageId = "chippi-usage";

    public static async Task<CheckResult> CheckAsync(IAgentConversation claude, DateTimeOffset now, CancellationToken ct)
    {
        await claude.SendAsync(Request(InitializeId, """{"subtype":"initialize"}"""), ct).ConfigureAwait(false);
        using var init = await ResponseAsync(claude, InitializeId, ct).ConfigureAwait(false);
        if (init is null)
            return new(CheckState.Failed, Error: "initialize: no answer");
        if (ErrorOf(init.RootElement) is { } initError)
            return new(CheckState.Failed, Error: "initialize: " + initError);
        string? tokenSource = Body(init.RootElement) is { } body && body.TryGetProperty("account", out var account)
            && account.ValueKind == JsonValueKind.Object && account.TryGetProperty("tokenSource", out var source) && source.ValueKind == JsonValueKind.String
            ? source.GetString() : null;

        await claude.SendAsync(Request(UsageId, """{"subtype":"get_usage","skip_behaviors":true}"""), ct).ConfigureAwait(false);
        using var usage = await ResponseAsync(claude, UsageId, ct).ConfigureAwait(false);
        if (usage is null)
            return new(CheckState.Failed, Error: "get_usage: no answer");
        if (ErrorOf(usage.RootElement) is { } error)
            return new(Unknown(error) ? CheckState.TooOld : CheckState.Failed, Error: "get_usage: " + error);
        return Parse(Body(usage.RootElement), tokenSource, now);
    }

    private static string Request(string id, string request) =>
        "{\"type\":\"control_request\",\"request_id\":\"" + id + "\",\"request\":" + request + "}";

    /// <summary>
    /// <c>get_usage</c>'s answer: <c>subscription_type</c> ("team"), <c>rate_limits_available</c> and <c>rate_limits</c>.
    /// Signed out when there's no sign-in to read limits with.
    /// </summary>
    internal static CheckResult Parse(JsonElement? body, string? tokenSource, DateTimeOffset now)
    {
        if (body is not { ValueKind: JsonValueKind.Object } usage)
            return new(CheckState.Failed, Error: "get_usage: no body");
        string? plan = usage.TryGetProperty("subscription_type", out var type) && type.ValueKind == JsonValueKind.String ? AgentText.Plan(type.GetString()) : null;
        bool available = usage.TryGetProperty("rate_limits_available", out var flag) && flag.ValueKind == JsonValueKind.True;
        var limits = usage.TryGetProperty("rate_limits", out var rl) ? rl : default;
        if (!available || limits.ValueKind is not (JsonValueKind.Object or JsonValueKind.Array))
        {
            // No sign-in at all (the CLI not logged in), or one without a plan's limits (an API key): nothing to show.
            if (string.Equals(tokenSource, "none", StringComparison.OrdinalIgnoreCase) || plan is null)
                return new(CheckState.SignedOut, Error: $"tokenSource {tokenSource ?? "?"}, no rate limits");
            return new(CheckState.Ok, new UsageReading(plan, [], now));
        }
        var windows = limits.ValueKind == JsonValueKind.Object && limits.TryGetProperty("limits", out var list) && list.ValueKind == JsonValueKind.Array
            && Listed(list) is { Count: > 0 } listed ? listed : Keyed(limits);
        return new(CheckState.Ok, new UsageReading(plan, windows, now));
    }

    // The 5-hour limit, then the weekly one, then a weekly limit per model when the plan has one (Travis): the list names its
    // model ("Fable"). Spend, credits and anything else aren't shown: tokens and cost weren't chosen.
    private static List<UsageWindow> Listed(JsonElement list)
    {
        var windows = new List<(int Order, UsageWindow Window)>();
        foreach (var limit in list.EnumerateArray())
        {
            if (limit.ValueKind != JsonValueKind.Object || Number(limit, "percent") is not { } used
                || !limit.TryGetProperty("kind", out var kind) || kind.ValueKind != JsonValueKind.String)
                continue;
            string? label = kind.GetString() switch
            {
                "session" => "5-hour",
                "weekly_all" => "Weekly",
                "weekly_scoped" => Model(limit) is { } model ? $"{model} weekly" : null,
                _ => null,
            };
            if (label is not null)
                windows.Add((label == "5-hour" ? 0 : label == "Weekly" ? 1 : 2 + windows.Count, new UsageWindow(label, used, Time(limit, "resets_at"))));
        }
        return [.. windows.OrderBy(w => w.Order).Select(w => w.Window)];
    }

    // {"scope":{"model":{"display_name":"Fable"}}}
    private static string? Model(JsonElement limit) =>
        limit.TryGetProperty("scope", out var scope) && scope.ValueKind == JsonValueKind.Object
        && scope.TryGetProperty("model", out var model) && model.ValueKind == JsonValueKind.Object
        && model.TryGetProperty("display_name", out var name) && name.ValueKind == JsonValueKind.String && name.GetString() is { Length: > 0 } shown
            ? shown : null;

    // Without the list: the keyed windows, each {utilization, resets_at}, null when the plan doesn't have it.
    private static List<UsageWindow> Keyed(JsonElement limits)
    {
        var windows = new List<UsageWindow>();
        if (limits.ValueKind != JsonValueKind.Object)
            return windows;
        foreach (var (key, label) in new[] { ("five_hour", "5-hour"), ("seven_day", "Weekly"), ("seven_day_opus", "Opus weekly"), ("seven_day_sonnet", "Sonnet weekly") })
            if (limits.TryGetProperty(key, out var window) && window.ValueKind == JsonValueKind.Object && Number(window, "utilization") is { } used)
                windows.Add(new UsageWindow(label, used, Time(window, "resets_at")));
        return windows;
    }

    internal static double? Number(JsonElement value, params string[] names)
    {
        foreach (string name in names)
            if (value.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out double d) && double.IsFinite(d))
                return d;
        return null;
    }

    /// <summary>A time as an ISO string or Unix seconds (or milliseconds, past 10¹¹).</summary>
    internal static DateTimeOffset? Time(JsonElement value, params string[] names)
    {
        foreach (string name in names)
        {
            if (!value.TryGetProperty(name, out var v))
                continue;
            if (v.ValueKind == JsonValueKind.String && DateTimeOffset.TryParse(v.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
                return parsed;
            if (v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out long unix) && unix > 0 && unix <= DateTimeOffset.MaxValue.ToUnixTimeMilliseconds())
                return unix > 100_000_000_000 ? DateTimeOffset.FromUnixTimeMilliseconds(unix) : DateTimeOffset.FromUnixTimeSeconds(unix);
        }
        return null;
    }

    private static bool Unknown(string error) =>
        error.Contains("unknown", StringComparison.OrdinalIgnoreCase) || error.Contains("unsupported", StringComparison.OrdinalIgnoreCase)
        || error.Contains("not supported", StringComparison.OrdinalIgnoreCase) || error.Contains("invalid", StringComparison.OrdinalIgnoreCase);

    // {"type":"control_response","response":{"subtype":"success","request_id":…,"response":{…}}}
    private static JsonElement? Body(JsonElement root) =>
        root.TryGetProperty("response", out var outer) && outer.TryGetProperty("response", out var body) ? body : null;

    // {"type":"control_response","response":{"subtype":"error","request_id":…,"error":"…"}}
    private static string? ErrorOf(JsonElement root) =>
        root.TryGetProperty("response", out var outer) && outer.TryGetProperty("subtype", out var subtype) && subtype.GetString() == "error"
            ? outer.TryGetProperty("error", out var e) ? e.ToString() : "error"
            : null;

    /// <summary>The control response to <paramref name="id"/>, skipping everything else it writes; null if it closes first.</summary>
    private static async Task<JsonDocument?> ResponseAsync(IAgentConversation claude, string id, CancellationToken ct)
    {
        while (await claude.ReadLineAsync(ct).ConfigureAwait(false) is { } line)
        {
            if (!line.Contains(id, StringComparison.Ordinal))
                continue;
            JsonDocument doc;
            try { doc = JsonDocument.Parse(line); }
            catch (JsonException) { continue; }
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("type", out var t) && t.GetString() == "control_response"
                && root.TryGetProperty("response", out var r) && r.TryGetProperty("request_id", out var rid) && rid.GetString() == id)
                return doc;
            doc.Dispose();
        }
        return null;
    }
}

/// <summary>
/// The Claude app's own record of its plan checks, <c>%APPDATA%\Claude\plan-usage-history.json</c> (version 2): samples of
/// <c>{t, org, u:{fh, sd, so, sn}}</c>, the 5-hour, weekly, Opus weekly and Sonnet weekly shares. Only as fresh as the app's
/// last check, and with no reset times. Read when Claude Code can't say itself.
/// </summary>
internal static class ClaudeHistory
{
    public static UsageReading? Read(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var doc = JsonDocument.Parse(stream);
            return Parse(doc.RootElement);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            return null;
        }
    }

    internal static UsageReading? Parse(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("samples", out var samples) || samples.ValueKind != JsonValueKind.Array)
            return null;
        JsonElement? newest = null;
        long newestAt = 0;
        foreach (var sample in samples.EnumerateArray())
        {
            if (sample.ValueKind == JsonValueKind.Object && sample.TryGetProperty("t", out var t) && t.ValueKind == JsonValueKind.Number && t.TryGetInt64(out long at)
                && at > newestAt && at <= DateTimeOffset.MaxValue.ToUnixTimeMilliseconds() && sample.TryGetProperty("u", out var u) && u.ValueKind == JsonValueKind.Object)
            {
                newest = u;
                newestAt = at;
            }
        }
        if (newest is not { } shares)
            return null;
        var windows = new List<UsageWindow>();
        foreach (var (key, label) in new[] { ("fh", "5-hour"), ("sd", "Weekly"), ("so", "Opus weekly"), ("sn", "Sonnet weekly") })
            if (ClaudeLimits.Number(shares, key) is { } used)
                windows.Add(new UsageWindow(label, used, null));
        return new UsageReading(null, windows, DateTimeOffset.FromUnixTimeMilliseconds(newestAt));
    }
}
