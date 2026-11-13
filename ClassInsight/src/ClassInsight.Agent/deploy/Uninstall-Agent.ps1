<#
.SYNOPSIS
    Stops and removes the ClassInsight student agent Windows service.

.DESCRIPTION
    Reverses Install-Agent.ps1. Run from an elevated (Administrator) PowerShell.

.PARAMETER ServiceName
    Windows service key name. Default: ClassInsightAgent.
#>
[CmdletBinding()]
param(
    [string]$ServiceName = 'ClassInsightAgent',
    [string]$InstallDir = (Join-Path $env:ProgramFiles 'ClassInsight'),
    [switch]$KeepFiles
)

$ErrorActionPreference = 'Stop'

$principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'This script must be run as Administrator (needed to remove a Windows service).'
}

$svc = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if (-not $svc) {
    Write-Host "Service '$ServiceName' is not installed; nothing to do."
    return
}

if ($svc.Status -ne 'Stopped') {
    Write-Host "Stopping '$ServiceName'..."
    Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
}

Write-Host "Removing '$ServiceName'..."
& sc.exe delete $ServiceName | Out-Null
Start-Sleep -Seconds 1
Write-Host "Removed '$ServiceName'." -ForegroundColor Green

if (-not $KeepFiles -and (Test-Path $InstallDir)) {
    Write-Host "Removing installed files at $InstallDir..."
    Remove-Item -LiteralPath $InstallDir -Recurse -Force -ErrorAction SilentlyContinue
    if (Test-Path $InstallDir) { Write-Warning "Could not fully remove $InstallDir (files may be in use)." }
    else { Write-Host "Removed $InstallDir." -ForegroundColor Green }
}
