<#
.SYNOPSIS
    Installs the ClassInsight student agent as an auto-start Windows service (FR-AGENT-02).

.DESCRIPTION
    Copies the agent into a protected program folder, locks its ACL so only administrators and
    SYSTEM can modify it, and registers ClassInsight.Agent.exe as a Windows service that starts
    automatically at boot - before any user logs in - so a managed lab PC joins the classroom
    with no student action and no per-machine configuration (discovery finds the server). Also
    configures restart-on-failure so the agent recovers if it exits (FR-AGENT-07, within the lab
    trust model).

    Run from an elevated (Administrator) PowerShell. Intended for IT to run directly, via GPO
    startup script, or baked into a machine image.

    SECURITY: the service runs as LocalSystem, so the folder it runs from MUST NOT be writable by
    standard users - otherwise a student could replace the exe and run arbitrary code as SYSTEM
    at boot. This script therefore refuses to leave the installed binary writable by non-admins.

.PARAMETER BinaryPath
    Full path to the SOURCE ClassInsight.Agent.exe to install. Defaults to a published 'agent'
    folder next to this script, then to the exe beside this script. The whole folder containing
    this exe is copied into -InstallDir, so point it at a clean published output.

.PARAMETER InstallDir
    Protected destination folder. Default: %ProgramFiles%\ClassInsight.

.PARAMETER ServiceName
    Windows service key name. Default: ClassInsightAgent.

.EXAMPLE
    .\Install-Agent.ps1
.EXAMPLE
    .\Install-Agent.ps1 -BinaryPath 'C:\build\agent\ClassInsight.Agent.exe'
#>
[CmdletBinding()]
param(
    [string]$BinaryPath,
    [string]$InstallDir = (Join-Path $env:ProgramFiles 'ClassInsight'),
    [string]$ServiceName = 'ClassInsightAgent',
    [string]$DisplayName = 'ClassInsight Agent'
)

$ErrorActionPreference = 'Stop'

# --- must be elevated -------------------------------------------------------
$principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'This script must be run as Administrator (needed to register a Windows service).'
}

# --- helpers ----------------------------------------------------------------
# SIDs of broad, non-admin principals. If any of these is granted write/modify to the installed
# binary, a standard student could tamper with a SYSTEM service - so we treat it as fatal.
$NonAdminSids = @{
    'S-1-1-0'     = 'Everyone'
    'S-1-5-11'    = 'Authenticated Users'
    'S-1-5-32-545'= 'Users'
    'S-1-5-32-546'= 'Guests'
    'S-1-5-7'     = 'Anonymous'
}
# Atomic mutate bits only. NOTE: do NOT use composite rights like Modify/FullControl here --
# those bundle read/execute bits, so AND-ing them would flag even a correct read-only grant.
$WriteMask = [System.Security.AccessControl.FileSystemRights]'WriteData,AppendData,WriteExtendedAttributes,WriteAttributes,Delete,DeleteSubdirectoriesAndFiles,ChangePermissions,TakeOwnership'

function Test-NonAdminWritable {
    param([Parameter(Mandatory)][string]$Path)
    $acl = Get-Acl -LiteralPath $Path
    foreach ($ace in $acl.Access) {
        if ($ace.AccessControlType -ne [System.Security.AccessControl.AccessControlType]::Allow) { continue }
        try { $sid = $ace.IdentityReference.Translate([Security.Principal.SecurityIdentifier]).Value }
        catch { $sid = $ace.IdentityReference.Value }
        if ($NonAdminSids.ContainsKey($sid) -and ($ace.FileSystemRights -band $WriteMask)) {
            Write-Warning "  $Path grants '$($ace.FileSystemRights)' to $($NonAdminSids[$sid])."
            return $true
        }
    }
    return $false
}

# --- resolve the source agent executable ------------------------------------
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
if (-not $BinaryPath) {
    $candidates = @(
        (Join-Path $here 'agent\ClassInsight.Agent.exe'),
        (Join-Path $here 'ClassInsight.Agent.exe')
    )
    $BinaryPath = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
}
if (-not $BinaryPath -or -not (Test-Path $BinaryPath)) {
    throw "Could not find a source ClassInsight.Agent.exe. Pass -BinaryPath explicitly."
}
$BinaryPath = (Resolve-Path $BinaryPath).Path
$sourceDir  = Split-Path -Parent $BinaryPath
Write-Host "Source bundle:  $sourceDir"
Write-Host "Install target: $InstallDir"

# --- stop/remove any prior install so files aren't locked -------------------
$existing = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($existing) {
    Write-Host "Service '$ServiceName' already exists; stopping and removing it first."
    if ($existing.Status -ne 'Stopped') { Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue }
    & sc.exe delete $ServiceName | Out-Null
    Start-Sleep -Seconds 2
}

# --- copy into the protected program folder ---------------------------------
if (-not (Test-Path $InstallDir)) {
    New-Item -ItemType Directory -Path $InstallDir -Force | Out-Null
}
Write-Host "Copying agent files into $InstallDir..."
Copy-Item -Path (Join-Path $sourceDir '*') -Destination $InstallDir -Recurse -Force

$targetExe = Join-Path $InstallDir 'ClassInsight.Agent.exe'
if (-not (Test-Path $targetExe)) {
    throw "Copy did not produce $targetExe - check the source bundle."
}

# --- lock the ACL: Admins/SYSTEM full, Users read+execute, no inheritance ---
Write-Host "Securing $InstallDir (Administrators/SYSTEM full control; Users read & execute only)..."
$admins = New-Object Security.Principal.SecurityIdentifier('S-1-5-32-544')
$system = New-Object Security.Principal.SecurityIdentifier('S-1-5-18')
$users  = New-Object Security.Principal.SecurityIdentifier('S-1-5-32-545')
$inherit = [System.Security.AccessControl.InheritanceFlags]'ContainerInherit,ObjectInherit'
$none    = [System.Security.AccessControl.PropagationFlags]::None
$allow   = [System.Security.AccessControl.AccessControlType]::Allow

$sd = New-Object System.Security.AccessControl.DirectorySecurity
$sd.SetAccessRuleProtection($true, $false)   # break inheritance, drop inherited ACEs
$sd.SetOwner($admins)
$sd.AddAccessRule((New-Object System.Security.AccessControl.FileSystemAccessRule($admins,'FullControl',$inherit,$none,$allow)))
$sd.AddAccessRule((New-Object System.Security.AccessControl.FileSystemAccessRule($system,'FullControl',$inherit,$none,$allow)))
$sd.AddAccessRule((New-Object System.Security.AccessControl.FileSystemAccessRule($users,'ReadAndExecute',$inherit,$none,$allow)))
Set-Acl -LiteralPath $InstallDir -AclObject $sd

# --- verify the installed binary is NOT writable by non-admins --------------
Write-Host "Verifying the installed binary is not writable by standard users..."
if ((Test-NonAdminWritable -Path $InstallDir) -or (Test-NonAdminWritable -Path $targetExe)) {
    throw "Refusing to install: '$InstallDir' is writable by non-administrators. A standard user " +
          "could replace the agent and run code as SYSTEM. Fix the folder ACL and re-run."
}

# --- register: auto-start, runs as LocalSystem ------------------------------
# LocalSystem is used so the agent runs before/independent of a student login and can reach the
# active desktop session for screen streaming. The protected ACL above is what keeps that safe.
Write-Host "Creating service '$ServiceName' (auto-start)..."
New-Service -Name $ServiceName `
            -BinaryPathName "`"$targetExe`"" `
            -DisplayName $DisplayName `
            -Description 'ClassInsight classroom monitoring agent for institution-owned lab PCs.' `
            -StartupType Automatic | Out-Null

# Restart on failure: 5s, 5s, then every 10s; reset the failure count daily (FR-AGENT-07).
& sc.exe failure $ServiceName reset= 86400 actions= restart/5000/restart/5000/restart/10000 | Out-Null
# Treat a non-zero exit as a failure so the recovery actions above apply even on a clean crash.
& sc.exe failureflag $ServiceName 1 | Out-Null

# --- start ------------------------------------------------------------------
Write-Host "Starting '$ServiceName'..."
Start-Service -Name $ServiceName
$svc = Get-Service -Name $ServiceName
Write-Host "Done. '$($svc.DisplayName)' is $($svc.Status), runs from $targetExe, and starts automatically at boot." -ForegroundColor Green
