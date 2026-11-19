// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors
using Chippi.Elevated.Protocol;

namespace Chippi.Elevated.Tests;

/// <summary>Where the owner's environment, a weak key and a hostile path could steer what the elevated helper runs or touches.</summary>
public sealed class UninstallHardeningTests
{
    private const string AppExe = @"C:\Program Files\Fixture App\unins000.exe";

    [Theory]
    [InlineData(@"""%ProgramFiles%\App\unins000.exe"" /S")]
    [InlineData(@"%ProgramFiles%\App\unins000.exe /S")]
    public void A_variable_that_brings_in_a_quote_cant_reshape_the_command(string text)
    {
        var host = new FakeHost();
        host.Variables["ProgramFiles"] = @"C:\Windows\System32\cmd.exe"" /c payload """;
        host.Files.Add(@"C:\Windows\System32\cmd.exe");
        Assert.Null(UninstallCommand.Parse(text, host.Expand, host.FileExists));
    }

    [Fact]
    public void A_variable_that_stays_unexpanded_refuses_the_path()
    {
        var host = new FakeHost();
        host.Files.Add(@"%Nope%\a.exe");
        Assert.Null(UninstallCommand.Parse(@"""%Nope%\a.exe"" /S", host.Expand, host.FileExists));
        Assert.Null(UninstallCommand.Parse(@"%Nope%\a.exe /S", host.Expand, host.FileExists));
    }

    [Fact]
    public void Only_the_application_is_expanded_the_arguments_are_passed_as_written()
    {
        var host = new FakeHost();
        host.Files.Add(AppExe);
        var parsed = UninstallCommand.Parse(@"%ProgramFiles%\Fixture App\unins000.exe /dir=%ProgramFiles%", host.Expand, host.FileExists);
        Assert.Equal((AppExe, $"\"{AppExe}\" /dir=%ProgramFiles%"), parsed);
    }

    [Theory]
    [InlineData(@"\host\share\x.exe /S")]
    [InlineData(@"\?\C:\x.exe /S")]
    [InlineData(@"C:\a\..\x.exe /S")]
    [InlineData(@"x.exe /S")]
    public void A_candidate_that_breaks_the_path_rules_is_never_probed(string text)
    {
        var probed = new List<string>();
        Assert.Null(UninstallCommand.Parse(text, new FakeHost().Expand, path => { probed.Add(path); return true; }));
        Assert.Empty(probed);
    }

    [Fact]
    public async Task A_key_the_standard_user_could_change_is_refused_and_nothing_runs()
    {
        var registry = new FakeRegistry();
        registry.Keys[(64, "Weak")] = new UninstallKey(false, null, null, null, null, null, null, null, null, Untrusted: true);
        var probe = new FakeProbe();
        var launcher = new FakeLauncher(probe, registry);
        var records = new FakeRecords();
        var actions = new UninstallActions(registry, records, probe, launcher, new FakeHost());
        var reply = await actions.RunAsync(new AdminRequest(Guid.NewGuid(), Messages.Uninstall, View: 64, KeyName: "Weak"), CancellationToken.None);
        Assert.Equal((Messages.UninstallRefused, UninstallActions.UntrustedReason), (reply.State, reply.Reason));
        Assert.Empty(launcher.Started);
        Assert.Empty(records.All);
        Assert.Empty(probe.Opened);
    }

    [Fact]
    public void The_fixed_variables_are_windows_own_folders_not_the_environment()
    {
        string saved = Environment.GetEnvironmentVariable("ProgramFiles")!;
        try
        {
            Environment.SetEnvironmentVariable("ProgramFiles", @"D:\Decoy");
            var map = SystemEnvironment.FixedVariables();
            Assert.Equal(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles).TrimEnd(System.IO.Path.DirectorySeparatorChar), map["ProgramFiles"]);
            Assert.Equal(map["ProgramFiles"] + @"\App", SystemEnvironment.Expand(@"%programfiles%\App", map));
        }
        finally { Environment.SetEnvironmentVariable("ProgramFiles", saved); }
    }

    [Fact]
    public void Expand_replaces_known_names_and_leaves_the_rest()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["A"] = "x" };
        Assert.Equal(@"x\%B%\x 100% %", SystemEnvironment.Expand(@"%a%\%B%\%A% 100% %", map));
    }

    [Fact]
    public void The_uninstallers_environment_drops_runtime_hooks_and_pins_PATH()
    {
        KeyValuePair<string, string> V(string n, string v = "1") => new(n, v);
        var scrubbed = SystemEnvironment.Scrub(
        [
            V("SystemRoot", @"C:\Windows"), V("Path", @"C:\evil"), V("DOTNET_STARTUP_HOOKS"), V("COR_PROFILER"), V("CORECLR_ENABLE_PROFILING"), V("COMPLUS_Version"),
            V("JAVA_TOOL_OPTIONS"), V("_JAVA_OPTIONS"), V("JDK_JAVA_OPTIONS"), V("NODE_OPTIONS"), V("PYTHONPATH"), V("PERL5OPT"), V("__COMPAT_LAYER"), V("TEMP", @"C:\Windows\TEMP"),
        ], @"C:\Windows\System32").ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(["PATH", "SystemRoot", "TEMP"], scrubbed.Keys.Order(StringComparer.OrdinalIgnoreCase));
        Assert.Equal(@"C:\Windows\System32;C:\Windows;C:\Windows\System32\Wbem", scrubbed["PATH"]);
    }

    [Fact]
    public void The_block_ends_each_entry_with_a_null_and_the_list_with_a_second()
    {
        var block = SystemEnvironment.Block([new("A", "1"), new("B", "2")]);
        Assert.Equal("A=1\0B=2\0\0", new string(block));
    }

    [Fact]
    public void Protected_roots_come_from_windows_folders_not_the_helpers_environment()
    {
        string saved = Environment.GetEnvironmentVariable("ProgramFiles")!;
        string profile = Path.Combine(Path.GetTempPath(), "chippi-hardening-" + Guid.NewGuid().ToString("N"), "Owner");
        try
        {
            Environment.SetEnvironmentVariable("ProgramFiles", @"D:\Decoy");
            var roots = new NativeLeftoverSystem(profile).ProtectedRoots;
            Assert.Contains(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles).TrimEnd(System.IO.Path.DirectorySeparatorChar), roots, StringComparer.OrdinalIgnoreCase);
            Assert.Contains(Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFiles).TrimEnd(System.IO.Path.DirectorySeparatorChar), roots, StringComparer.OrdinalIgnoreCase);
            Assert.DoesNotContain(@"D:\Decoy", roots, StringComparer.OrdinalIgnoreCase);
        }
        finally { Environment.SetEnvironmentVariable("ProgramFiles", saved); }
    }
}
