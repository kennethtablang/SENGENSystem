# Builds SEN-GEN for SmarterASP.NET and zips it, ready to upload.
#
#   1. Copy SENGENSystem.Server\appsettings.Production.example.json to
#      SENGENSystem.Server\appsettings.Production.json and fill it in (it is git-ignored).
#   2. Run:  .\publish-smarterasp.ps1
#   3. Upload publish.zip with the SmarterASP File Manager and unzip it into the site folder.
#
# See sengensystem.client\src\assets\plan.md for the full steps.

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$out = Join-Path $root 'publish'
$zip = Join-Path $root 'publish.zip'
$prodSettings = Join-Path $root 'SENGENSystem.Server\appsettings.Production.json'

if (-not (Test-Path $prodSettings)) {
    throw "Missing SENGENSystem.Server\appsettings.Production.json - copy appsettings.Production.example.json and fill it in first."
}

if (Test-Path $out) { Remove-Item $out -Recurse -Force }
if (Test-Path $zip) { Remove-Item $zip -Force }

# The server publish also builds the React client and puts it in wwwroot.
dotnet publish (Join-Path $root 'SENGENSystem.Server') -c Release -r win-x64 --self-contained false -o $out
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed.' }

# Sanity checks: the client is inside, and no development settings went out.
if (-not (Test-Path (Join-Path $out 'wwwroot\index.html'))) { throw 'The client build is missing from wwwroot.' }
Remove-Item (Join-Path $out 'appsettings.Development.json') -ErrorAction SilentlyContinue
Copy-Item $prodSettings (Join-Path $out 'appsettings.Production.json') -Force

Compress-Archive -Path (Join-Path $out '*') -DestinationPath $zip
Write-Host ''
Write-Host "Done: $zip" -ForegroundColor Green
Write-Host 'Upload it with the SmarterASP File Manager and unzip it into your site folder.'
