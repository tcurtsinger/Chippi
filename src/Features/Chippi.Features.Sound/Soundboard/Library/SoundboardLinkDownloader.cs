// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Chippi.Features.Sound.Soundboard.Library;

public sealed record SoundboardProcessResult(int ExitCode, string Output, string Error);
public interface ISoundboardProcessRunner
{
    Task<SoundboardProcessResult> RunAsync(string executable, IReadOnlyList<string> arguments, string workingDirectory, CancellationToken cancellationToken);
}

/// <summary>Direct links use bounded HTTP; supported media sites use the pinned, isolated bundled extractor.</summary>
public sealed class SoundboardLinkDownloader : ISoundboardLinkDownloader, IDisposable
{
    private readonly HttpClient _http;
    private readonly bool _ownsHttp;
    private readonly ISoundboardProcessRunner _runner;
    private readonly string _toolRoot;
    private readonly Func<string, string>? _toolResolver;

    public SoundboardLinkDownloader(HttpClient? http = null, ISoundboardProcessRunner? runner = null, string? toolRoot = null, Func<string, string>? toolResolver = null)
    {
        _ownsHttp = http is null;
        _http = http ?? new(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false }) { Timeout = Timeout.InfiniteTimeSpan };
        _runner = runner ?? new SoundboardProcessRunner();
        _toolRoot = toolRoot ?? Path.Combine(AppContext.BaseDirectory, "vendor", "soundboard");
        _toolResolver = toolResolver;
    }

    public static Uri ValidateUrl(string value)
    {
        if (value.Length > 4096 || !Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri)
            || uri.Scheme is not ("https" or "http") || !string.IsNullOrEmpty(uri.UserInfo)
            || string.IsNullOrWhiteSpace(uri.Host))
            throw new SoundboardException("Enter an http or https audio link without a username or password");
        return uri;
    }

    public static bool IsMediaSite(Uri uri) => Host(uri, "youtube.com") || Host(uri, "youtu.be")
        || Host(uri, "soundcloud.com") || Host(uri, "on.soundcloud.com");
    private static bool Host(Uri uri, string host) => uri.IdnHost.Equals(host, StringComparison.OrdinalIgnoreCase)
        || uri.IdnHost.EndsWith("." + host, StringComparison.OrdinalIgnoreCase);

    public async Task<SoundDownload> DownloadAsync(Uri url, string directory, IProgress<SoundImportProgress>? progress, CancellationToken cancellationToken)
    {
        url = ValidateUrl(url.AbsoluteUri);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(10));
        try
        {
            return IsMediaSite(url)
                ? await DownloadSiteAsync(url, directory, progress, timeout.Token).ConfigureAwait(false)
                : await DownloadDirectAsync(url, directory, progress, timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { throw new SoundboardException("Import timed out. Try again or choose a file."); }
        catch (HttpRequestException ex)
        { throw new SoundboardException("Download failed. Check the link and try again.", ex); }
    }

    private async Task<SoundDownload> DownloadDirectAsync(Uri url, string directory, IProgress<SoundImportProgress>? progress, CancellationToken ct)
    {
        for (int redirect = 0; redirect <= 5; redirect++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.UserAgent.ParseAdd("Chippi/0.1 Soundboard");
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            if ((int)response.StatusCode is >= 300 and < 400 && response.Headers.Location is { } location)
            {
                url = ValidateUrl(new Uri(url, location).AbsoluteUri);
                if (IsMediaSite(url)) return await DownloadSiteAsync(url, directory, progress, ct).ConfigureAwait(false);
                continue;
            }
            response.EnsureSuccessStatusCode();
            string type = response.Content.Headers.ContentType?.MediaType ?? "";
            if (type.StartsWith("text/", StringComparison.OrdinalIgnoreCase) || type.Contains("json", StringComparison.OrdinalIgnoreCase)
                || Path.GetExtension(url.AbsolutePath).ToLowerInvariant() is ".m3u" or ".m3u8" or ".pls" or ".url")
                throw new SoundboardException("This link opens a page. Choose a direct audio link, YouTube or SoundCloud.");
            if (response.Content.Headers.ContentLength > SoundboardImporter.MaxSourceBytes)
                throw new SoundboardException("Choose a sound under 250 MB");
            string name = response.Content.Headers.ContentDisposition?.FileNameStar
                ?? response.Content.Headers.ContentDisposition?.FileName?.Trim('"') ?? Uri.UnescapeDataString(url.AbsolutePath.Split('/').Last());
            // The server's filename is display data only; it never controls the destination path.
            string extension = Path.GetExtension(name).ToLowerInvariant();
            extension = extension is ".wav" or ".mp3" or ".m4a" or ".aac" or ".wma" or ".flac" or ".aif" or ".aiff" or ".mp4" ? extension : ".audio";
            string target = Path.Combine(directory, "source" + extension);
            using var input = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, useAsync: true);
            await SoundboardImporter.CopyBoundedAsync(input, output, response.Content.Headers.ContentLength, progress, ct).ConfigureAwait(false);
            return new(target, Path.GetFileNameWithoutExtension(name), "Audio link");
        }
        throw new SoundboardException("The link redirects too many times. Choose a direct audio link.");
    }

    private async Task<SoundDownload> DownloadSiteAsync(Uri url, string directory, IProgress<SoundImportProgress>? progress, CancellationToken ct)
    {
        progress?.Report(new("Downloading"));
        // Hashing the bundled runtime can read 100 MB. Keep it off the dispatcher on a cold import.
        var (extractor, runtime) = await Task.Run(() => (ResolveTool("yt-dlp.exe"), ResolveTool("node.exe")), ct).ConfigureAwait(false);
        // No user config/plugins/cookies, remote components, playlists, shell, or postprocessing.
        // m4a/mp3/mp4 can be decoded locally by Windows, without a second FFmpeg distribution.
        string[] arguments = ["--ignore-config", "--no-plugin-dirs", "--no-cache-dir", "--no-remote-components",
            "--no-js-runtimes", "--js-runtimes", "node:" + runtime,
            "--no-playlist", "--playlist-items", "1", "--no-progress", "--no-warnings", "--no-colors",
            "--socket-timeout", "30", "--retries", "2", "--fragment-retries", "2", "--concurrent-fragments", "1",
            "--max-filesize", SoundboardImporter.MaxSourceBytes.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "--match-filters", "!is_live & duration <= 1800", "--fixup", "never", "--no-part",
            "--format", "bestaudio[ext=m4a]/bestaudio[ext=mp3]/best[ext=mp4]",
            "--output", Path.Combine(directory, "source.%(ext)s"),
            "--print", "after_move:%(title)j", "--", url.AbsoluteUri];
        var result = await _runner.RunAsync(extractor, arguments, directory, ct).ConfigureAwait(false);
        if (result.ExitCode != 0)
            throw new SoundboardException("This link could not be imported. It may be unavailable or require sign-in. Try another link or choose a file.");
        var files = Directory.EnumerateFiles(directory, "source.*", SearchOption.TopDirectoryOnly)
            .Where(p => Path.GetExtension(p).ToLowerInvariant() is ".m4a" or ".mp3" or ".mp4").ToList();
        if (files.Count != 1 || new FileInfo(files[0]).Length == 0)
            throw new SoundboardException("No audio was found. Choose a single sound under 30 minutes.");
        if (new FileInfo(files[0]).Length > SoundboardImporter.MaxSourceBytes)
            throw new SoundboardException("Choose a sound under 250 MB");
        string title = "Sound";
        foreach (string line in result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Reverse())
        {
            try { title = JsonSerializer.Deserialize<string>(line.Trim()) ?? title; break; }
            catch (JsonException) { }
        }
        return new(files[0], title, Host(url, "soundcloud.com") ? "SoundCloud" : "YouTube");
    }

    private string ResolveTool(string name)
    {
        if (_toolResolver is not null) return _toolResolver(name);
        string path = Path.Combine(_toolRoot, name);
        string manifest = Path.Combine(_toolRoot, "tools.json");
        if (!File.Exists(path) || !File.Exists(manifest))
            throw new SoundboardException("Link import tools are missing. Reinstall Chippi or choose a file.");
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllBytes(manifest));
            string rid = RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "win-arm64" : "win-x64";
            var tool = doc.RootElement.GetProperty("runtimes").GetProperty(rid).GetProperty(name);
            using var stream = File.OpenRead(path);
            if (stream.Length == tool.GetProperty("bytes").GetInt64()
                && Convert.ToHexString(SHA256.HashData(stream)).Equals(tool.GetProperty("sha256").GetString(), StringComparison.OrdinalIgnoreCase))
                return path;
        }
        catch (Exception ex) when (ex is JsonException or IOException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            throw new SoundboardException("Link import tools could not be verified. Reinstall Chippi or choose a file.", ex);
        }
            throw new SoundboardException("Link import tools could not be verified. Reinstall Chippi or choose a file.");
    }

    public void Dispose() { if (_ownsHttp) _http.Dispose(); }
}

/// <summary>Hidden child process with argument-list isolation, bounded output and bounded download files.</summary>
public sealed class SoundboardProcessRunner : ISoundboardProcessRunner
{
    public async Task<SoundboardProcessResult> RunAsync(string executable, IReadOnlyList<string> arguments, string workingDirectory, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = workingDirectory,
        };
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        start.Environment.Remove("NODE_OPTIONS");
        start.Environment.Remove("NODE_PATH");
        using var process = new Process { StartInfo = start };
        if (!process.Start()) throw new SoundboardException("Link import could not start");
        Task<string> output = ReadBounded(process.StandardOutput);
        Task<string> error = ReadBounded(process.StandardError);
        try
        {
            Task exit = process.WaitForExitAsync(cancellationToken);
            while (!exit.IsCompleted)
            {
                await Task.WhenAny(exit, Task.Delay(250, cancellationToken)).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                long bytes = 0;
                foreach (string file in Directory.EnumerateFiles(workingDirectory, "*", SearchOption.AllDirectories))
                {
                    try { bytes += new FileInfo(file).Length; }
                    catch (FileNotFoundException) { }
                    if (bytes > SoundboardImporter.MaxSourceBytes)
                        throw new SoundboardException("Choose a sound under 250 MB");
                }
            }
            await exit.ConfigureAwait(false);
            return new(process.ExitCode, await output.ConfigureAwait(false), await error.ConfigureAwait(false));
        }
        catch
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            await Task.WhenAll(output, error).ConfigureAwait(false);
            throw;
        }
    }

    private static async Task<string> ReadBounded(StreamReader reader)
    {
        var text = new StringBuilder();
        char[] buffer = new char[2048];
        int read;
        while ((read = await reader.ReadAsync(buffer).ConfigureAwait(false)) > 0)
            if (text.Length < 65536) text.Append(buffer, 0, Math.Min(read, 65536 - text.Length));
        return text.ToString();
    }
}
