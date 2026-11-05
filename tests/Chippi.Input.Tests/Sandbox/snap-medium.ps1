# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) Chippi contributors

# Windows Sandbox's logon command runs elevated. Run Snap and its target apps as an ordinary user,
# preserving the production rule that elevated windows cannot be moved. This runs only inside Sandbox.
if ($env:USERNAME -ne 'WDAGUtilityAccount' -or -not (Test-Path -LiteralPath 'C:\chippi\Chippi.exe')) {
    throw 'This launcher runs only inside the Windows Sandbox check'
}
$localRoot = Join-Path ([Environment]::GetFolderPath("CommonDocuments")) 'ChippiSnapSandbox'
$localApp = Join-Path $localRoot 'app'
$localProbe = Join-Path $localRoot 'probe'
$localResults = Join-Path $localRoot 'results'
New-Item -ItemType Directory -Force -Path $localRoot,$localResults | Out-Null
# The guest's inherited Public ACL can be noncanonical. This is a new test-only directory:
# give it a fresh, canonical ACL before copying any files.
$acl = New-Object Security.AccessControl.DirectorySecurity
$acl.SetAccessRuleProtection($true,$false)
foreach ($entry in @(@('S-1-5-32-545','Modify'),@('S-1-5-32-544','FullControl'),@('S-1-5-18','FullControl'),
    @([Security.Principal.WindowsIdentity]::GetCurrent().User.Value,'FullControl'))) {
    $sid = New-Object Security.Principal.SecurityIdentifier $entry[0]
    $rule = New-Object Security.AccessControl.FileSystemAccessRule $sid, $entry[1], 'ContainerInherit,ObjectInherit', 'None', 'Allow'
    $acl.AddAccessRule($rule)
}
Set-Acl -LiteralPath $localRoot -AclObject $acl
Copy-Item -LiteralPath C:\chippi -Destination $localApp -Recurse
Copy-Item -LiteralPath C:\probe -Destination $localProbe -Recurse
Copy-Item -LiteralPath C:\results\checks.txt -Destination (Join-Path $localResults 'checks.txt')
# Restricted processes cannot use the host's mapped folders on every Sandbox image. Local user files
# inherit the account SID's permissions; only the elevated wrapper copies results back to the mapping.
Get-ChildItem -LiteralPath $localProbe -Filter *.ps1 | ForEach-Object {
    $script = [IO.File]::ReadAllText($_.FullName).Replace('C:\results',$localResults).Replace('C:\chippi',$localApp).Replace('C:\probe',$localProbe)
    [IO.File]::WriteAllText($_.FullName,$script)
}
$env:CHIPPI_SNAP_LOCAL = $localRoot
# Use Windows' Basic User launch instead of constructing a GUI token ourselves.
$command = 'powershell.exe -NoProfile -ExecutionPolicy Bypass -File "' + (Join-Path $localProbe 'probe.ps1') + '" -SnapMedium'
try {
    $launchOutput = & "$env:WINDIR\System32\runas.exe" /trustlevel:0x20000 $command 2>&1
    $launchCode = $LASTEXITCODE
    $launchOutput | Set-Content -LiteralPath (Join-Path $localResults 'medium-launch.txt')
    if ($launchCode -ne 0) { throw "Basic User launch failed ($launchCode): $launchOutput" }
    $deadline = (Get-Date).AddMinutes(5)
    while (-not (Test-Path -LiteralPath (Join-Path $localResults 'done.txt'))) {
        if ((Get-Date) -gt $deadline) { throw 'Basic User Snap probe timed out' }
        Start-Sleep -Milliseconds 200
    }
}
finally {
    Get-ChildItem -LiteralPath $localResults | Copy-Item -Destination C:\results -Recurse -Force
}
shutdown.exe /s /t 5