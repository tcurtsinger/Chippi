# WS5 integration proposals

Approved in the tracker on 2026-10-21. WS5 applied the lead-owned package/project/startup edits in a separate commit, as requested.

## Root tool manifest

`.config/dotnet-tools.json`, installed using `dotnet tool install vpk --version 1.2.161 --local --allow-roll-forward` so the .NET 8-targeted tool can run on this .NET 10-only build host. This is a build tool, not an app runtime dependency.

## Central version and App reference

```xml
<!-- Directory.Packages.props, existing ItemGroup -->
<PackageVersion Include="Velopack" Version="1.2.161" />

<!-- src/Chippi.App/Chippi.App.csproj, existing package ItemGroup -->
<PackageReference Include="Velopack" />
```

## Notices next to the executable

Add to Chippi.App.csproj:

```xml
<ItemGroup>
  <Content Include="..\..\LICENSE" Link="LICENSE" CopyToOutputDirectory="PreserveNewest" CopyToPublishDirectory="PreserveNewest" ExcludeFromSingleFile="true" />
  <Content Include="..\..\THIRD-PARTY-NOTICES.md" Link="THIRD-PARTY-NOTICES.md" CopyToOutputDirectory="PreserveNewest" CopyToPublishDirectory="PreserveNewest" ExcludeFromSingleFile="true" />
</ItemGroup>
```

## Startup hook

Put `Velopack.VelopackApp.Build().Run();` in `OnStartup` immediately after `EnsureWindowsDirectoryEnvironment`, before `base.OnStartup` and single-instance acquisition, as approved in the tracker. No update manager, feed URL or network request is added. Official guidance permits App.xaml.cs; a custom Main is recommended to avoid loading WPF during updates but would require more application-definition changes. https://docs.velopack.io/getting-started/csharp

The pack script retains Velopack's builder validation and never uses skipVeloAppCheck. Installer execution is not part of the build script; it would install on the user's machine. Setup and the ARM64 app need appropriate manual launch checks at integration.
