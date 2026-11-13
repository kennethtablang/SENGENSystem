<#
.SYNOPSIS
    Installs the ClassInsight student agent as an auto-start Windows service (FR-AGENT-02).

.DESCRIPTION
    Registers ClassInsight.Agent.exe as a Windows service that starts automatically at boot,
    before any user logs in, so a managed lab PC joins the classroom with no student action and
    no per-machine configuration (discovery finds the server). Also configures restart-on-failure
    so the agent comes back if it exits unexpectedly (FR-AGENT-07, within the lab trust model).

    Run from an elevated (Administrator) PowerShell. Intended for IT to run directly, via GPO
    startup script, or baked into a machine image.

.PARAMETER BinaryPath
    Full path to ClassInsight.Agent.exe. Defaults to a published 'agent' folder next to this
    script, then to the exe beside this script.

.PARAMETER ServiceName
    Windows service key name. Default: ClassInsightAgent.

.EXAMPLE
    .\Install-Agent.ps1 -BinaryPath 'C:\Program Files\ClassInsight\ClassInsight.Agent.exe'
#>
[CmdletBinding()]
param(
    [string]$BinaryPath,
    [string]$ServiceName = 'ClassInsightAgent',
    [string]$DisplayName = 'ClassInsight Agent'
)

$ErrorActionPreference = 'Stop'

# --- must be elevated -------------------------------------------------------
$principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'This script must be run as Administrator (needed to register a Windows service).'
}

# --- resolve the agent executable ------------------------------------------
if (-not $BinaryPath) {
    $here = Split-Path -Parent $MyInvocation.MyCommand.Path
    $candidates = @(
        (Join-Path $here 'agent\ClassInsight.Agent.exe'),
        (Join-Path $here 'ClassInsight.Agent.exe')
    )
    $BinaryPath = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
}
if (-not $BinaryPath -or -not (Test-Path $BinaryPath)) {
    throw "Could not find ClassInsight.Agent.exe. Pass -BinaryPath explicitly. Tried: $($candidates -join '; ')"
}
$BinaryPath = (Resolve-Path $BinaryPath).Path
Write-Host "Agent executable: $BinaryPath"

# --- remove any prior install ----------------------------------------------
$existing = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($existing) {
    Write-Host "Service '$ServiceName' already exists; stopping and removing it first."
    if ($existing.Status -ne 'Stopped') { Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue }
    & sc.exe delete $ServiceName | Out-Null
    Start-Sleep -Seconds 2
}

# --- register: auto-start, runs as LocalSystem -----------------------------
# LocalSystem is required so the agent runs before/independent of a student login and can
# capture the active desktop session for screen streaming.
Write-Host "Creating service '$ServiceName' (auto-start)…"
New-Service -Name $ServiceName `
            -BinaryPathName "`"$BinaryPath`"" `
            -DisplayName $DisplayName `
            -Description 'ClassInsight classroom monitoring agent for institution-owned lab PCs.' `
            -StartupType Automatic | Out-Null

# Restart on failure: 5s, 5s, then every 10s; reset the failure count daily (FR-AGENT-07).
& sc.exe failure $ServiceName reset= 86400 actions= restart/5000/restart/5000/restart/10000 | Out-Null
# Treat a non-zero exit as a failure so the recovery actions above apply even on a clean crash.
& sc.exe failureflag $ServiceName 1 | Out-Null

# --- start -----------------------------------------------------------------
Write-Host "Starting '$ServiceName'…"
Start-Service -Name $ServiceName
$svc = Get-Service -Name $ServiceName
Write-Host "Done. '$($svc.DisplayName)' is $($svc.Status) and set to start automatically at boot." -ForegroundColor Green
