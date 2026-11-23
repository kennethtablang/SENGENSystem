# Builds SEN-GEN and pushes it to SmarterASP with Web Deploy (no zip upload needed).
#
#   1. Fill in deploy\appsettings.Production.json (see README.md, Step 3).
#   2. Copy deploy\webdeploy.example.json to deploy\webdeploy.local.json and fill it in
#      (SmarterASP control panel -> your site -> Web Deploy). It is git-ignored.
#   3. Run from the repository root:   .\deploy\deploy-webdeploy.ps1
#      Dry run (shows what would change, changes nothing):   .\deploy\deploy-webdeploy.ps1 -DryRun
#      Re-deploy the last build without rebuilding:           .\deploy\deploy-webdeploy.ps1 -SkipBuild
#
# The Web Deploy password is asked for each time and never written to disk.

param(
    [switch]$DryRun,
    [switch]$SkipBuild,
    # Only if the host's certificate is rejected. Leave off unless the first run fails on the certificate.
    [switch]$AllowUntrusted
)

$ErrorActionPreference = 'Stop'
$deploy = $PSScriptRoot
$out = Join-Path $deploy 'publish'
$localSettings = Join-Path $deploy 'webdeploy.local.json'

if (-not (Test-Path $localSettings)) {
    throw "Missing deploy\webdeploy.local.json - copy deploy\webdeploy.example.json and fill it in first."
}
$target = Get-Content $localSettings -Raw | ConvertFrom-Json
foreach ($field in 'ServiceUrl', 'SiteName', 'UserName', 'SiteUrl') {
    if ("$($target.$field)".Trim() -eq '' -or "$($target.$field)".Contains('xxxx')) {
        throw "deploy\webdeploy.local.json: '$field' is not filled in."
    }
}

$msdeploy = @(
    "$env:ProgramFiles\IIS\Microsoft Web Deploy V3\msdeploy.exe",
    "${env:ProgramFiles(x86)}\IIS\Microsoft Web Deploy V3\msdeploy.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $msdeploy) { throw 'Web Deploy (msdeploy.exe) is not installed. Install "Web Deploy 3.6" from Microsoft.' }

# 1. Build with every check publish-smarterasp.ps1 already makes (placeholders, client, settings).
if (-not $SkipBuild) {
    & (Join-Path $deploy 'publish-smarterasp.ps1')
}
if (-not (Test-Path (Join-Path $out 'appsettings.Production.json'))) {
    throw 'deploy\publish has no appsettings.Production.json - run without -SkipBuild.'
}

# 2. Credentials: asked for, never stored.
$secure = Read-Host "Web Deploy password for $($target.UserName)" -AsSecureString
$password = [System.Net.NetworkCredential]::new('', $secure).Password
if ($password -eq '') { throw 'No password entered.' }
if ($password.Contains('"')) { throw 'The password contains a double quote, which msdeploy cannot take on the command line. Change it in the control panel.' }

# 3. Sync the build folder to the site.
#    AppOffline   - takes the site offline during the copy, so IIS releases the DLLs it holds.
#    DoNotDelete  - leaves files that exist only on the server (logs) alone.
$arguments = @(
    '-verb:sync',
    "-source:contentPath=""$out""",
    ("-dest:contentPath=""{0}"",computerName=""{1}"",userName=""{2}"",password=""{3}"",authType=""Basic""" -f
        $target.SiteName, $target.ServiceUrl, $target.UserName, $password),
    '-enableRule:AppOffline',
    '-enableRule:DoNotDeleteRule',
    '-retryAttempts:3'
)
if ($AllowUntrusted) { $arguments += '-allowUntrusted' }
if ($DryRun) { $arguments += '-whatif' }

# Start-Process passes the argument string through as written; PowerShell's own argument
# handling mangles msdeploy's quoted provider settings.
$process = Start-Process -FilePath $msdeploy -ArgumentList ($arguments -join ' ') -NoNewWindow -Wait -PassThru
$password = $null

if ($process.ExitCode -ne 0) {
    throw "Web Deploy failed (exit code $($process.ExitCode)). Check the message above. A certificate error means re-run with -AllowUntrusted; a 401 means a wrong user name or password."
}

Write-Host ''
if ($DryRun) {
    Write-Host 'Dry run finished - nothing was changed on the server.' -ForegroundColor Yellow
} else {
    Write-Host "Deployed. Open $($target.SiteUrl) - the first visit can take up to a minute." -ForegroundColor Green
}
