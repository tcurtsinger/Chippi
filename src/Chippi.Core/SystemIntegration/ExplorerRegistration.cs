// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using Microsoft.Win32;

namespace Chippi.Core.SystemIntegration;

/// <summary>The per-user classic Explorer folder and folder-background Organize verbs.</summary>
public sealed class ExplorerRegistration
{
    public const string DefaultClassesRoot = @"Software\Classes";
    public const string ProfileArgument = "--dev-profile";
    private readonly string _classesRoot;
    private readonly string? _profile;
    private readonly string? _executable;

    public ExplorerRegistration(string? profile = null, string classesRoot = DefaultClassesRoot, string? executable = null)
    {
        _profile = string.IsNullOrWhiteSpace(profile) ? null : ValidateProfile(profile);
        _classesRoot = classesRoot;
        _executable = executable;
    }

    public string VerbName => _profile is null ? "ChippiOrganize" : $"ChippiOrganize-dev-{_profile}";
    public IReadOnlyList<string> KeyPaths => [ $@"{_classesRoot}\Directory\shell\{VerbName}", $@"{_classesRoot}\Directory\Background\shell\{VerbName}" ];

    public void SetEnabled(bool enabled)
    {
        string? executable = enabled ? StableExecutable(_executable ?? Environment.ProcessPath ?? throw new InvalidOperationException("Executable not found")) : null;
        foreach (string path in KeyPaths)
        {
            if (!enabled)
            {
                Registry.CurrentUser.DeleteSubKeyTree(path, throwOnMissingSubKey: false);
                continue;
            }
            using var key = Registry.CurrentUser.CreateSubKey(path);
            key.SetValue("MUIVerb", "Organize", RegistryValueKind.String);
            key.SetValue("Icon", $"\"{executable}\",0", RegistryValueKind.String);
            using var command = key.CreateSubKey("command");
            string profile = _profile is null ? "" : $" {ProfileArgument} \"{_profile}\"";
            command.SetValue("", $"\"{executable}\" --organize \"%V\"{profile}", RegistryValueKind.String);
        }
    }

    public static string StableExecutable(string path)
    {
        if (!Path.IsPathFullyQualified(path) || path.Contains('"'))
            throw new ArgumentException("Expected a fully qualified executable path", nameof(path));
        string install = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ChippiApp");
        return Path.GetFullPath(path).StartsWith(install + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            ? Path.Combine(install, "current", "Chippi.exe") : path;
    }

    /// <summary>Resolve a dev menu click before selecting its settings, instance and module data.</summary>
    public static string? ResolveDevProfile(IReadOnlyList<string> arguments)
    {
        for (int i = 0; i < arguments.Count; i++)
        {
            if (!string.Equals(arguments[i], ProfileArgument, StringComparison.OrdinalIgnoreCase)) continue;
            if (i + 1 >= arguments.Count) throw new ArgumentException("Dev profile is missing");
            string profile = ValidateProfile(arguments[i + 1]);
            Environment.SetEnvironmentVariable("CHIPPI_DEV_PROFILE", profile);
            return profile;
        }
        return EnvironmentProfile();
    }

    /// <summary>CHIPPI_DEV_PROFILE, held to the same rule as the argument, so it can't name a folder outside Chippi-dev.</summary>
    public static string? EnvironmentProfile() =>
        Environment.GetEnvironmentVariable("CHIPPI_DEV_PROFILE") is { } profile && !string.IsNullOrWhiteSpace(profile) ? ValidateProfile(profile) : null;

    private static string ValidateProfile(string profile)
    {
        if (profile.Length is < 1 or > 80 || profile.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_'))
            throw new ArgumentException("Dev profiles use letters, digits, hyphens and underscores", nameof(profile));
        return profile;
    }
}
