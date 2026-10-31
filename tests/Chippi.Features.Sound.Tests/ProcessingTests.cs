// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Chippi.Audio;
using Chippi.Audio.Processing;
using Chippi.Audio.Tests;
using Chippi.Core.Island;
using Chippi.Core.Modules;
using Chippi.Features.Sound.Processing;
using Microsoft.Extensions.Logging.Abstractions;
using static Chippi.Audio.Tests.FakeDeviceSystem;
using static Chippi.Features.Sound.Tests.RoutingFixture;

namespace Chippi.Features.Sound.Tests;

public sealed class ProcessingFixture : IAsyncDisposable
{
    public FakeDeviceSystem System { get; } = new();
    public FakePipelines Pipelines { get; } = new();
    public AudioService Audio { get; }
    public MicProcessingModule Module { get; }
    public MemorySettings<ProcessingSettings> Settings { get; } = new();
    public RoutingNotifier Notices { get; } = new();
    public RoutingUiFake Ui { get; } = new();
    public IslandActivityHub Hub { get; } = new();
    public FakeSetup Setup { get; } = new();
    public string Data { get; } = Path.Combine(Path.GetTempPath(), "chippi-s3-tests", Guid.NewGuid().ToString("N"));
    public List<PriorityEntry> Pinned { get; } = [];

    public ProcessingFixture()
    {
        Audio = new AudioService(() => System, null, options: new AudioServiceOptions { Debounce = TimeSpan.FromMilliseconds(10), PipelineFactory = Pipelines.Factory });
        Module = new MicProcessingModule(Audio, () => Pinned.ToList(), Data, Setup);
    }

    public string MarkerPath => Path.Combine(Data, MicProcessingModule.MarkerFile);

    public Task Start() => Module.StartAsync(new ModuleContext(MicProcessingModule.Id, Settings, NullLogger.Instance,
        Hub.CreatePublisher(MicProcessingModule.Id), Ui) { Notifier = Notices, DataDirectory = Data }, CancellationToken.None);

    public async ValueTask DisposeAsync()
    {
        await Module.StopAsync(CancellationToken.None);
        try { Directory.Delete(Data, true); } catch (DirectoryNotFoundException) { }
    }
}

public sealed class MemorySettings<T> : IModuleSettings where T : class, new()
{
    public T Value { get; set; } = new();
    public int Saves { get; private set; }
    public TValue Get<TValue>() where TValue : class, new() => (TValue)(object)Value;
    public void Save<TValue>(TValue value) where TValue : class { Value = (T)(object)value; Saves++; }
}

public sealed class FakeSetup : IDriverSetup
{
    public List<string> Runs { get; } = [];
    public TaskCompletionSource<int?> Result { get; set; } = new();
    public Task<int?> RunAsync(string setupPath, CancellationToken ct)
    {
        Runs.Add(setupPath);
        return Result.Task;
    }
}

public sealed class ProcessingModuleTests
{
    private static readonly AudioRole[] All = [AudioRole.Console, AudioRole.Multimedia, AudioRole.Communications];

    [Fact]
    public async Task Installing_the_feature_alone_never_starts_processing()
    {
        await using var f = new ProcessingFixture();
        f.System.With(Mic("Real"), CableInput(), CableOutput()).WithDefault("Real");
        await f.Start();

        Assert.Empty(f.Pipelines.Created);
        Assert.Equal(ProcessingState.Off, f.Module.Status.State);
        Assert.Equal("Real", f.System.DefaultOf(AudioFlow.Capture, AudioRole.Multimedia));
        var tile = Assert.Single(f.Ui.Tiles);
        Assert.Equal((SoundTile.Key, SoundTile.Microphone, 30, 10), (tile.Spec.Key, tile.Spec.Column, tile.Spec.SectionOrder, tile.Spec.StatusPriority));
        Assert.Equal("", tile.Status); // the Sound tile shows the output until Mic processing stops
    }

    [Fact]
    public async Task Switching_on_goes_live_writes_the_marker_and_switching_off_removes_it()
    {
        await using var f = new ProcessingFixture();
        f.System.With(Mic("Real"), CableInput(), CableOutput()).WithDefault("Real");
        await f.Start();

        await f.Module.SetEnabledAsync(true);

        Assert.Equal(ProcessingState.Live, f.Module.Status.State);
        Assert.Equal("", f.Ui.Tiles[0].Status); // live is the normal state; the Sound tile keeps showing the output
        Assert.True(f.Settings.Value.Enabled);
        var marker = JsonSerializer.Deserialize<ProcessingMarker>(File.ReadAllText(f.MarkerPath))!;
        Assert.Equal("CableOut", marker.CableCaptureId);

        await f.Module.SetEnabledAsync(false);

        Assert.False(File.Exists(f.MarkerPath));
        Assert.Equal("Real", f.System.DefaultOf(AudioFlow.Capture, AudioRole.Multimedia));
        Assert.Equal("", f.Ui.Tiles[0].Status);
    }

    [Fact]
    public async Task A_crash_marker_is_restored_at_start_and_removed()
    {
        await using var f = new ProcessingFixture();
        f.System.With(Mic("Real"), CableInput(), CableOutput()).WithDefault("CableOut");
        Directory.CreateDirectory(f.Data);
        File.WriteAllText(f.MarkerPath, JsonSerializer.Serialize(new ProcessingMarker("CableOut", All.ToDictionary(r => r, _ => "Real"))));

        await f.Start();

        Assert.All(All, r => Assert.Equal("Real", f.System.DefaultOf(AudioFlow.Capture, r)));
        Assert.False(File.Exists(f.MarkerPath));
    }

    [Fact]
    public async Task Stopping_the_module_restores_the_mic_and_keeps_the_switch()
    {
        var f = new ProcessingFixture();
        f.System.With(Mic("Real"), CableInput(), CableOutput()).WithDefault("Real");
        f.Settings.Value = new() { Enabled = true };
        await f.Start();
        Assert.Equal("CableOut", f.System.DefaultOf(AudioFlow.Capture, AudioRole.Console));

        await f.DisposeAsync();

        Assert.Equal("Real", f.System.DefaultOf(AudioFlow.Capture, AudioRole.Console));
        Assert.True(f.Pipelines.Created[0].Disposed);
        Assert.True(f.Settings.Value.Enabled); // reinstalling resumes
    }

    [Fact]
    public async Task Losing_the_driver_while_live_raises_stopped_once()
    {
        await using var f = new ProcessingFixture();
        f.System.With(Mic("Real"), CableInput(), CableOutput()).WithDefault("Real");
        f.Settings.Value = new() { Enabled = true };
        await f.Start();

        f.System.Remove("CableIn", "CableOut");

        await Until(() => f.Module.Status.Reason == StopReason.NoDriver);
        await f.Audio.RefreshAsync();
        var notice = Assert.Single(f.Notices.Sent);
        Assert.Equal(("stopped", "No VB-CABLE"), (notice.Kind, notice.Body));
        Assert.Equal("Stopped · No VB-CABLE", f.Module.StatusText);
        Assert.Equal(("Stopped · No VB-CABLE", ProcessingFix.Install), (f.Module.Problem, f.Module.Fix)); // the warning line installs it
        Assert.Equal("Real", f.System.DefaultOf(AudioFlow.Capture, AudioRole.Multimedia));
        Assert.True(f.Settings.Value.Enabled); // the switch stays; status says what's real
        Assert.Equal("Mic stopped", f.Ui.Tiles[0].Status);
        var stopped = Assert.Single(f.Hub.Activities); // the Sound tile shows it in Caution
        Assert.Equal((true, SoundTile.Key), (stopped.NeedsAttention, stopped.TileKey));

        await f.Module.SetEnabledAsync(false);
        Assert.Equal("", f.Ui.Tiles[0].Status);
        Assert.Empty(f.Hub.Activities);
    }

    [Fact]
    public async Task Two_cables_read_as_more_than_one_cable()
    {
        await using var f = new ProcessingFixture();
        const string second = @"{2}.\\?\root#media#0001#{6994ad04-93ef-11d0-a3cc-00a0c9223196}";
        f.System.With(Mic("Real"), CableInput(), CableOutput(), CableInput("In2", second), CableOutput("Out2", second));
        f.Settings.Value = new() { Enabled = true };
        await f.Start();
        Assert.Equal("Stopped · More than one VB-CABLE", f.Module.StatusText);
    }

    [Fact]
    public async Task No_microphone_reads_as_the_brief_says()
    {
        await using var f = new ProcessingFixture();
        f.System.With(CableInput(), CableOutput());
        f.Settings.Value = new() { Enabled = true };
        await f.Start();
        Assert.Equal("Stopped · No microphone", f.Module.StatusText);
        Assert.Equal(("Stopped · No microphone", ProcessingFix.None), (f.Module.Problem, f.Module.Fix)); // plugging one in is the fix
    }

    [Fact]
    public async Task A_failed_start_says_failed_and_why_and_retry_starts_again()
    {
        await using var f = new ProcessingFixture();
        f.System.With(Mic("Real"), CableInput(), CableOutput()).WithDefault("Real");
        f.Pipelines.FailOnStart.Add("Real");
        f.Settings.Value = new() { Enabled = true };
        await f.Start();
        Assert.Equal(("Failed · Device in use", ProcessingFix.Retry), (f.Module.Problem, f.Module.Fix));
        await f.Audio.RefreshAsync();
        Assert.Equal(ProcessingState.Stopped, f.Module.Status.State); // a failure isn't retried on its own

        f.Pipelines.FailOnStart.Clear();
        await f.Module.RetryAsync();
        Assert.Equal(ProcessingState.Live, f.Module.Status.State);
        Assert.Equal((null, ProcessingFix.None), (f.Module.Problem, f.Module.Fix));
    }

    [Fact]
    public async Task Off_and_live_have_no_warning_line()
    {
        await using var f = new ProcessingFixture();
        f.System.With(Mic("Real"), CableInput(), CableOutput()).WithDefault("Real");
        await f.Start();
        Assert.Equal((null, ProcessingFix.None), (f.Module.Problem, f.Module.Fix));
        await f.Module.SetEnabledAsync(true);
        Assert.Equal((null, ProcessingFix.None), (f.Module.Problem, f.Module.Fix));
    }

    [Fact]
    public async Task Pinned_mic_follows_audio_routings_list_and_a_chosen_mic_is_used_as_is()
    {
        await using var f = new ProcessingFixture();
        f.System.With(Mic("Desk"), Mic("Headset"), CableInput(), CableOutput());
        f.Pinned.AddRange([new("Headset", "Headset"), new("Desk", "Desk")]);
        f.Settings.Value = new() { Enabled = true };
        await f.Start();
        Assert.Equal("Headset", f.Pipelines.Live?.Source);

        await f.Module.SetSourceAsync("Desk");

        Assert.Equal("Desk", f.Pipelines.Live?.Source);
        Assert.Equal("Desk", f.Settings.Value.Source);
    }

    [Fact]
    public async Task Stage_edits_are_heard_at_once_and_saved_after_a_pause()
    {
        await using var f = new ProcessingFixture();
        f.System.With(Mic("Real"), CableInput(), CableOutput());
        f.Settings.Value = new() { Enabled = true };
        await f.Start();
        var saves = f.Settings.Saves;
        var live = f.Pipelines.Live!.Settings;

        await f.Module.SetStagesAsync(s => s with { GateDb = -50, Agc = false });

        Assert.Equal(MicSettings.FromDecibels(-50), live.GateThreshold, 5);
        Assert.False(live.Agc);
        Assert.Equal(saves, f.Settings.Saves);
        await Until(() => f.Settings.Saves > saves);
        Assert.Equal(-50, f.Settings.Value.GateDb);
        Assert.Single(f.Pipelines.Created); // no rebuild for a level change
    }

    [Fact]
    public async Task Saved_levels_out_of_range_are_clamped()
    {
        await using var f = new ProcessingFixture();
        f.Settings.Value = new() { GateDb = -500, TargetDb = double.NaN, MaxGainDb = 99 };
        await f.Start();
        Assert.Equal((-70, -6, 30), (f.Module.Settings.GateDb, f.Module.Settings.TargetDb, f.Module.Settings.MaxGainDb));
    }

    [Fact]
    public async Task The_source_list_never_offers_the_cable()
    {
        await using var f = new ProcessingFixture();
        f.System.With(Mic("Real"), CableInput(), CableOutput());
        await f.Start();
        Assert.Equal(["Real"], f.Module.Sources.Select(e => e.Id));
    }
}

public sealed class VbCableRequirementTests : IDisposable
{
    private readonly FakeDeviceSystem _sys = new();
    private readonly AudioService _audio;
    private readonly FakeSetup _setup = new();
    private readonly string _data = Path.Combine(Path.GetTempPath(), "chippi-s3-tests", Guid.NewGuid().ToString("N"));
    private int _prepared;

    public VbCableRequirementTests() => _audio = new AudioService(() => _sys, null, options: new AudioServiceOptions { Debounce = TimeSpan.FromMilliseconds(10) });

    public void Dispose()
    {
        VbCableRequirement.BootTime = () => DateTimeOffset.UtcNow - TimeSpan.FromMilliseconds(Environment.TickCount64);
        try { Directory.Delete(_data, true); } catch (DirectoryNotFoundException) { }
    }

    private VbCableRequirement Create() => new(_audio, _data, () => { _prepared++; return @"C:\fake\VBCABLE_Setup_x64.exe"; }, _setup, () => { });

    [Fact]
    public void No_cable_is_missing_and_a_ready_pair_is_ready()
    {
        _sys.With(Mic("Real"));
        using (var missing = Create())
            Assert.Equal(RequirementState.Missing, missing.State);

        _sys.With(CableInput(), CableOutput());
        using var ready = Create();
        Assert.Equal(RequirementState.Ready, ready.State);
    }

    [Fact]
    public void Present_but_disabled_is_driver_disabled_never_install_again()
    {
        _sys.With(CableInput() with { State = EndpointState.Disabled }, CableOutput() with { State = EndpointState.Disabled });
        using var requirement = Create();
        Assert.Equal(RequirementState.Disabled, requirement.State);
    }

    [Fact]
    public async Task Install_is_busy_while_setup_runs_then_waits_for_a_restart()
    {
        using var requirement = Create();
        var states = new List<RequirementState>();
        requirement.StateChanged += (_, _) => states.Add(requirement.State);

        var install = requirement.InstallAsync(CancellationToken.None);
        await Until(() => _setup.Runs.Count == 1);
        Assert.Equal(RequirementState.Installing, requirement.State);
        _setup.Result.SetResult(0);
        await install;

        Assert.Equal(RequirementState.RestartNeeded, requirement.State);
        Assert.Equal([RequirementState.Installing, RequirementState.RestartNeeded], states);
        Assert.Equal(1, _prepared);
    }

    [Fact]
    public async Task A_cancelled_admin_prompt_goes_back_to_install_driver()
    {
        using var requirement = Create();
        _setup.Result.SetResult(null);
        await requirement.InstallAsync(CancellationToken.None);
        Assert.Equal(RequirementState.Missing, requirement.State);
    }

    [Fact]
    public async Task Restart_to_finish_survives_a_chippi_restart_but_not_a_pc_restart()
    {
        using (var first = Create())
        {
            _setup.Result.SetResult(0);
            await first.InstallAsync(CancellationToken.None);
        }
        using (var again = Create())
            Assert.Equal(RequirementState.RestartNeeded, again.State);

        VbCableRequirement.BootTime = () => DateTimeOffset.UtcNow; // the PC restarted
        using var rebooted = Create();
        Assert.Equal(RequirementState.Missing, rebooted.State);
    }

    [Fact]
    public async Task The_devices_appearing_make_it_ready_and_never_install_the_feature()
    {
        using var requirement = Create();
        _setup.Result.SetResult(0);
        await requirement.InstallAsync(CancellationToken.None);

        _sys.Connect(CableInput());
        _sys.Connect(CableOutput());

        await Until(() => requirement.State == RequirementState.Ready);
        Assert.False(File.Exists(Path.Combine(_data, "restart-pending.txt")));
    }

    [Fact]
    public async Task Install_is_ignored_unless_missing()
    {
        _sys.With(CableInput(), CableOutput());
        using var requirement = Create();
        await requirement.InstallAsync(CancellationToken.None);
        Assert.Empty(_setup.Runs);
    }
}

public sealed class VbCablePackageTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "chippi-s3-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (DirectoryNotFoundException) { }
    }

    private string Vendor(byte[] zip, string? sha = null, long? bytes = null, string? setupSha = null, string? signer = null)
    {
        var vendor = Path.Combine(_root, "vendor");
        Directory.CreateDirectory(vendor);
        File.WriteAllBytes(Path.Combine(vendor, "pack.zip"), zip);
        var pin = new VbCablePin("https://example.invalid/pack.zip", "pack.zip", bytes ?? zip.Length, sha ?? Convert.ToHexString(SHA256.HashData(zip)),
            "setup.exe", setupSha ?? new string('0', 64), signer ?? new string('0', 40), "Nobody");
        File.WriteAllText(Path.Combine(vendor, "vb-cable.json"), JsonSerializer.Serialize(pin, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
        return vendor;
    }

    private static byte[] Zip(params (string Name, byte[] Data)[] files)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, data) in files)
            {
                using var entry = archive.CreateEntry(name).Open();
                entry.Write(data);
            }
        }
        return stream.ToArray();
    }

    [Fact]
    public void A_missing_zip_is_reported_as_missing()
    {
        var vendor = Vendor(Zip(("setup.exe", [1])));
        File.Delete(Path.Combine(vendor, "pack.zip"));
        Assert.Equal("Driver package missing", Assert.Throws<DriverPackageException>(() => VbCablePackage.Prepare(vendor, Path.Combine(_root, "x"))).Message);
    }

    [Fact]
    public void A_zip_that_differs_from_its_pin_never_extracts()
    {
        var vendor = Vendor(Zip(("setup.exe", [1])), sha: new string('A', 64));
        Assert.Throws<DriverPackageException>(() => VbCablePackage.Prepare(vendor, Path.Combine(_root, "x")));
        Assert.False(Directory.Exists(Path.Combine(_root, "x")));
    }

    [Fact]
    public void A_setup_with_the_wrong_hash_is_refused_after_extraction()
    {
        var vendor = Vendor(Zip(("setup.exe", [1, 2, 3])));
        Assert.Equal("Driver package damaged", Assert.Throws<DriverPackageException>(() => VbCablePackage.Prepare(vendor, Path.Combine(_root, "x"))).Message);
    }

    // Karl's S3 review: setup reads the INF beside it, so a damaged earlier extraction must never reach it.
    [Fact]
    public void Every_prepare_extracts_the_whole_package_again()
    {
        var signed = File.ReadAllBytes(typeof(object).Assembly.Location); // Authenticode-signed stand-in for the setup
#pragma warning disable SYSLIB0057
        var thumbprint = new X509Certificate2(X509Certificate.CreateFromSignedFile(typeof(object).Assembly.Location)).Thumbprint;
#pragma warning restore SYSLIB0057
        byte[] inf = [1, 2, 3];
        var vendor = Vendor(Zip(("setup.exe", signed), ("cable.inf", inf)), setupSha: Convert.ToHexString(SHA256.HashData(signed)), signer: thumbprint);
        var extract = Path.Combine(_root, "x");
        var setup = VbCablePackage.Prepare(vendor, extract);
        var installed = Path.Combine(Path.GetDirectoryName(setup)!, "cable.inf");

        File.WriteAllBytes(installed, [9]);
        File.WriteAllBytes(Path.Combine(Path.GetDirectoryName(setup)!, "planted.dll"), [9]);
        Assert.Equal(setup, VbCablePackage.Prepare(vendor, extract));
        Assert.Equal(inf, File.ReadAllBytes(installed));
        Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(setup)!, "planted.dll")));

        File.Delete(installed);
        VbCablePackage.Prepare(vendor, extract);
        Assert.Equal(inf, File.ReadAllBytes(installed));
    }

    [Fact]
    public void Signature_check_needs_a_valid_signature_from_the_pinned_certificate()
    {
        var signed = typeof(object).Assembly.Location; // System.Private.CoreLib, Authenticode-signed by Microsoft
#pragma warning disable SYSLIB0057
        var thumbprint = new X509Certificate2(X509Certificate.CreateFromSignedFile(signed)).Thumbprint;
#pragma warning restore SYSLIB0057
        Assert.True(VbCablePackage.SignedBy(signed, thumbprint));
        Assert.False(VbCablePackage.SignedBy(signed, new string('0', 40)));

        var unsigned = Path.Combine(_root, "unsigned.exe");
        Directory.CreateDirectory(_root);
        File.WriteAllBytes(unsigned, [0x4D, 0x5A, 0, 0]);
        Assert.False(VbCablePackage.SignedBy(unsigned, thumbprint));
    }
}
