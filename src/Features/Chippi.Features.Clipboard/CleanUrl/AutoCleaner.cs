// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Chippi.Core.Clipboard;
using Chippi.UrlCleaning;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Chippi.Features.Clipboard.CleanUrl;

/// <summary>
/// Automatic cleaning (design notes #27, #28): a copied web link with trackers becomes its clean link, with
/// no status, notice or sound. Listens only while it's on. Never touches a copy marked private, a picture
/// or files, or a link on a site to skip. Logs never carry the link.
/// </summary>
public sealed class AutoCleaner : IDisposable
{
    private readonly IClipboardHub _hub;
    private readonly ILogger _log;
    private readonly Lock _gate = new();
    private IDisposable? _lease;
    private IReadOnlyList<string> _skip = [];
    private bool _on, _disposed;

    public AutoCleaner(IClipboardHub hub, ILogger? log = null)
    {
        _hub = hub;
        _log = log ?? NullLogger.Instance;
    }

    public bool Listening { get { lock (_gate) return _lease is not null; } }

    /// <summary>The last rewrite, for tests.</summary>
    internal Task Rewriting { get; private set; } = Task.CompletedTask;

    public void Apply(bool on, IReadOnlyList<string> sitesToSkip)
    {
        lock (_gate)
        {
            if (_disposed)
                return;
            _on = on;
            _skip = [.. sitesToSkip.Select(Sites.Normalize).OfType<string>()];
            if (on && _lease is null)
                _lease = _hub.Listen(OnCopied);
            else if (!on && _lease is not null)
            {
                _lease.Dispose();
                _lease = null;
            }
        }
    }

    /// <summary>The clean link automatic cleaning puts in place of <paramref name="text"/>, or null when it leaves it alone.</summary>
    public string? Cleaned(string text)
    {
        IReadOnlyList<string> skip;
        lock (_gate)
        {
            if (!_on)
                return null;
            skip = _skip;
        }
        if (UrlCleaner.Clean(text) is not { RemovedParameters.Count: > 0 } result || !Uri.TryCreate(result.Url, UriKind.Absolute, out var uri))
            return null;
        return Sites.Covers(skip, uri.Host) ? null : result.Url;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _lease?.Dispose();
            _lease = null;
        }
    }

    private void OnCopied(ClipCopy copy)
    {
        if (copy.Privacy != ClipPrivacy.None || copy.Content is not { Text: { } text, Files: null, Image: null } || Cleaned(text) is not { } clean)
            return;
        Rewriting = RewriteAsync(clean);
    }

    private async Task RewriteAsync(string clean)
    {
        if (await _hub.WriteAsync(ClipContent.FromText(clean)).ConfigureAwait(false) != ClipStatus.Ok)
            _log.LogInformation("Clean URL: clipboard busy, copy left as it was");
    }
}

/// <summary>Sites to skip: hosts, each covering its subdomains (notes #54).</summary>
public static class Sites
{
    /// <summary>The host to keep for an entry ("Example.org", "https://www.example.org/x" → "example.org" / "www.example.org"), or null when it isn't one.</summary>
    public static string? Normalize(string entry)
    {
        var text = entry.Trim().TrimEnd('.');
        if (text.Length == 0 || text.Any(char.IsWhiteSpace))
            return null;
        if (!text.Contains("://", StringComparison.Ordinal))
            text = "https://" + text;
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || uri.HostNameType is not (UriHostNameType.Dns or UriHostNameType.IPv4 or UriHostNameType.IPv6) || !uri.Host.Contains('.'))
            return null;
        return uri.IdnHost.ToLowerInvariant();
    }

    public static bool Covers(IEnumerable<string> sites, string host)
    {
        host = host.TrimEnd('.');
        return sites.Any(site => host.Equals(site, StringComparison.OrdinalIgnoreCase) || host.EndsWith("." + site, StringComparison.OrdinalIgnoreCase));
    }
}
