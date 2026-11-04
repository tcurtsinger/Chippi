// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.IO;
using System.Net;
using System.Net.Http;
using Chippi.Features.Sound.Soundboard.Library;
using NAudio.Wave;

namespace Chippi.Features.Sound.Tests;

/// <summary>Temporary files, managed WAV decoding and fake HTTP/processes only. No endpoints or network.</summary>
public sealed class SoundboardImportTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "Chippi-tests", "soundboard-import-" + Guid.NewGuid().ToString("N"));

    private string Source()
    {
        Directory.CreateDirectory(_root);
        string source = Path.Combine(_root, "air-horn.wav");
        File.WriteAllBytes(source, [1, 2, 3]);
        return source;
    }

    [Fact]
    public async Task File_import_owns_only_its_draft_and_keeps_original_untouched()
    {
        string source = Source();
        using var importer = new SoundboardImporter(Path.Combine(_root, "data"), new Decoder());
        var draft = await importer.PrepareAsync(new(source));
        Assert.Equal("air-horn", draft.Title);
        Assert.Equal("File", draft.Source);
        Assert.Equal(2, draft.DurationSeconds);
        Assert.True(File.Exists(draft.LocalAudioPath));
        draft.Dispose();
        draft.Dispose();
        Assert.False(File.Exists(draft.LocalAudioPath));
        Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(source));
    }

    [Fact]
    public async Task Cancellation_cleans_partial_draft_and_preserves_original()
    {
        string source = Source();
        using var cancellation = new CancellationTokenSource();
        using var importer = new SoundboardImporter(Path.Combine(_root, "data"), new Decoder(() => cancellation.Cancel()));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => importer.PrepareAsync(new(source), cancellationToken: cancellation.Token));
        Assert.Empty(Directory.GetDirectories(Path.Combine(_root, "data", "imports")));
        Assert.True(File.Exists(source));
    }

    [Fact]
    public async Task Oversized_source_is_rejected_before_decode()
    {
        string source = Source();
        using (var file = File.OpenWrite(source)) file.SetLength(SoundboardImporter.MaxSourceBytes + 1);
        var decoder = new Decoder();
        using var importer = new SoundboardImporter(Path.Combine(_root, "data"), decoder);
        await Assert.ThrowsAsync<SoundboardException>(() => importer.PrepareAsync(new(source)));
        Assert.Equal(0, decoder.Calls);
    }

    [Theory]
    [InlineData("file:///C:/Windows/win.ini")]
    [InlineData("ftp://example.test/clip.mp3")]
    [InlineData("https://name:password@example.test/clip.mp3")]
    [InlineData("not a link")]
    public void Unsupported_link_schemes_and_embedded_credentials_are_rejected(string url) =>
        Assert.Throws<SoundboardException>(() => SoundboardLinkDownloader.ValidateUrl(url));

    [Fact]
    public async Task Direct_download_checks_redirects_and_uses_an_owned_filename()
    {
        Directory.CreateDirectory(_root);
        int requests = 0;
        using var http = new HttpClient(new Handler(_ =>
        {
            requests++;
            if (requests == 1) return new(HttpStatusCode.Redirect) { Headers = { Location = new("https://cdn.example.test/clip.wav") } };
            return new(HttpStatusCode.OK) { Content = new ByteArrayContent([1, 2, 3]) { Headers =
                { ContentDisposition = new("attachment") { FileName = "../../outside.wav" } } } };
        }));
        using var download = new SoundboardLinkDownloader(http);
        var result = await download.DownloadAsync(new("https://example.test/sound"), _root, null, default);
        Assert.Equal(2, requests);
        Assert.Equal(Path.Combine(_root, "source.wav"), result.LocalPath);
        Assert.Equal([1, 2, 3], File.ReadAllBytes(result.LocalPath));
    }

    [Fact]
    public async Task Direct_download_rejects_html_and_unsafe_redirects()
    {
        Directory.CreateDirectory(_root);
        using var html = new HttpClient(new Handler(_ => new(HttpStatusCode.OK) { Content = new StringContent("<html>page</html>") }));
        using var download = new SoundboardLinkDownloader(html);
        await Assert.ThrowsAsync<SoundboardException>(() => download.DownloadAsync(new("https://example.test/page"), _root, null, default));
        using var redirect = new HttpClient(new Handler(_ => new(HttpStatusCode.Redirect) { Headers = { Location = new("file:///C:/private.wav") } }));
        using var redirected = new SoundboardLinkDownloader(redirect);
        await Assert.ThrowsAsync<SoundboardException>(() => redirected.DownloadAsync(new("https://example.test/sound"), _root, null, default));
        Assert.Empty(Directory.GetFiles(_root));
    }

    [Fact]
    public async Task Site_import_is_one_clip_with_isolated_arguments_and_bundled_runtime()
    {
        Directory.CreateDirectory(_root);
        var runner = new Runner();
        using var download = new SoundboardLinkDownloader(runner: runner, toolResolver: name => Path.Combine(_root, name));
        var result = await download.DownloadAsync(new("https://www.youtube.com/watch?v=sample&list=sample"), _root, null, default);
        Assert.Equal("Air horn", result.Title);
        Assert.Equal("YouTube", result.Source);
        Assert.Contains("--ignore-config", runner.Arguments);
        Assert.Contains("--no-plugin-dirs", runner.Arguments);
        Assert.Contains("--no-remote-components", runner.Arguments);
        Assert.Contains("--no-playlist", runner.Arguments);
        Assert.Contains("node:" + Path.Combine(_root, "node.exe"), runner.Arguments);
        Assert.Equal("--", runner.Arguments[^2]);
        Assert.Equal("https://www.youtube.com/watch?v=sample&list=sample", runner.Arguments[^1]);
        Assert.DoesNotContain(runner.Arguments, a => a.Contains("cookies", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Media_site_matching_cannot_be_spoofed_by_suffix_or_userinfo()
    {
        Assert.True(SoundboardLinkDownloader.IsMediaSite(new("https://music.youtube.com/watch?v=x")));
        Assert.True(SoundboardLinkDownloader.IsMediaSite(new("https://on.soundcloud.com/x")));
        Assert.False(SoundboardLinkDownloader.IsMediaSite(new("https://youtube.com.attacker.test/x")));
        Assert.False(SoundboardLinkDownloader.IsMediaSite(new("https://notyoutube.com/x")));
    }

    [Fact]
    public async Task Managed_wav_decode_produces_bounded_canonical_audio_and_waveform()
    {
        Directory.CreateDirectory(_root);
        string source = Path.Combine(_root, "mono.wav");
        using (var writer = new WaveFileWriter(source, new WaveFormat(24000, 16, 1)))
            writer.WriteSamples(Enumerable.Repeat(.25f, 2400).ToArray(), 0, 2400);
        string output = Path.Combine(_root, "canonical.wav");
        var decoded = await new SoundboardDecoder().DecodeAsync(source, output, null, default);
        using var read = new WaveFileReader(output);
        Assert.Equal(48000, read.WaveFormat.SampleRate);
        Assert.Equal(2, read.WaveFormat.Channels);
        Assert.Equal(16, read.WaveFormat.BitsPerSample);
        Assert.InRange(decoded.DurationSeconds, .09, .11);
        Assert.NotEmpty(decoded.Waveform);
        Assert.All(decoded.Waveform, peak => Assert.InRange(peak, 0, 1));
    }

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }

    private sealed class Decoder(Action? during = null) : ISoundboardDecoder
    {
        public int Calls;
        public Task<SoundDecodedAudio> DecodeAsync(string inputPath, string wavePath, IProgress<SoundImportProgress>? progress, CancellationToken cancellationToken)
        {
            Calls++;
            File.Copy(inputPath, wavePath);
            during?.Invoke();
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new SoundDecodedAudio(2, [.2f, .8f]));
        }
    }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(response(request));
    }
    private sealed class Runner : ISoundboardProcessRunner
    {
        public IReadOnlyList<string> Arguments = [];
        public Task<SoundboardProcessResult> RunAsync(string executable, IReadOnlyList<string> arguments, string workingDirectory, CancellationToken cancellationToken)
        {
            Arguments = arguments;
            File.WriteAllBytes(Path.Combine(workingDirectory, "source.m4a"), [1, 2, 3]);
            return Task.FromResult(new SoundboardProcessResult(0, "\"Air horn\"\n", ""));
        }
    }
}
