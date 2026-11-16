// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using Chippi.UI.Admin;
using Chippi.Elevated.Protocol;
using Microsoft.Extensions.Logging.Abstractions;

if (Environment.UserName != "WDAGUtilityAccount" || args.Length != 1 || args[0] != "--sandbox") return 2;
string report = @"C:\results\helper.json";
try
{
    if (PeerIdentity.CurrentIsElevated()) throw new InvalidOperationException("Probe must run at medium integrity.");
    using var helper = new AdminHelper(new WindowsAdminSession(true), NullLogger.Instance);
    var watch = Stopwatch.StartNew();
    var results = await helper.UpdatePackagesAsync(["7zip.7zip", "Chippi.AdminHelperMissingFixture"]);
    bool passed = results.Count == 2 && results[0].State == "Done" && results[1].State == "Failed";
    File.WriteAllText(report, JsonSerializer.Serialize(new { passed, milliseconds = watch.Elapsed.TotalMilliseconds, results }));
    return passed ? 0 : 1;
}
catch (Exception ex) { File.WriteAllText(report, JsonSerializer.Serialize(new { passed = false, error = ex.ToString() })); return 1; }
