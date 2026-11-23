# Builds SEN-GEN for SmarterASP.NET and zips it, ready to upload.
#
#   1. Copy deploy\appsettings.Production.example.json to
#      deploy\appsettings.Production.json and fill it in (it is git-ignored).
#   2. Run from the repository root:  .\deploy\publish-smarterasp.ps1
#   3. Upload deploy\publish.zip with the SmarterASP File Manager and unzip it into the site folder.
#
# See deploy\README.md for the full steps.

$ErrorActionPreference = 'Stop'
$deploy = $PSScriptRoot                  # this folder: the settings go in, the build comes out
$root = Split-Path $deploy -Parent       # the repository
$out = Join-Path $deploy 'publish'
$zip = Join-Path $deploy 'publish.zip'
$prodSettings = Join-Path $deploy 'appsettings.Production.json'

if (-not (Test-Path $prodSettings)) {
    throw "Missing deploy\appsettings.Production.json - copy deploy\appsettings.Production.example.json and fill it in first."
}

# Refuse to build while any placeholder from the example file is still in place.
$text = Get-Content $prodSettings -Raw
try { $settings = $text | ConvertFrom-Json } catch { throw "appsettings.Production.json is not valid JSON: $($_.Exception.Message)" }
$placeholders = [ordered]@{
    'SQLxxxx'                 = 'ConnectionStrings:DefaultConnection (database server)'
    'db_xxxxx'                = 'ConnectionStrings:DefaultConnection (database name / user)'
    'YOUR-DATABASE-PASSWORD'  = 'ConnectionStrings:DefaultConnection (database password)'
    'PASTE-A-LONG-RANDOM'     = 'Jwt:Key'
    'your-school.edu.ph'      = 'Seed:SuperAdminEmail / Seed:AdminEmail'
    'A-STRONG-FIRST-PASSWORD' = 'Seed:SuperAdminPassword / Seed:AdminPassword'
    'GMAIL-APP-PASSWORD'      = 'Email:Password'
    'your-site-address'       = 'Email:ClientBaseUrl'
}
$unfilled = @($placeholders.Keys | Where-Object { $text.Contains($_) } | ForEach-Object { $placeholders[$_] } | Select-Object -Unique)
if ($unfilled.Count -gt 0) {
    throw ("appsettings.Production.json still has placeholders. Fill in:`n  - " + ($unfilled -join "`n  - "))
}
if ("$($settings.Jwt.Key)".Length -lt 32) { throw 'Jwt:Key must be at least 32 characters.' }

# The two admin accounts are created once, on the first start, from these addresses. A typo here
# leaves an account nobody can sign in to or receive mail at, so catch it before it ships.
# Lowercase matters too: sign-in lowercases what is typed, the seeder stores what is written.
foreach ($key in 'SuperAdminEmail', 'AdminEmail') {
    $email = "$($settings.Seed.$key)"
    if ($email -notmatch '^[^@\s]+@[^@\s]+\.[^@\s]+$') { throw "Seed:$key is not a valid email address." }
    if ($email -cne $email.ToLowerInvariant()) { throw "Seed:$key must be all lowercase." }
}
if ("$($settings.Seed.SuperAdminEmail)" -eq "$($settings.Seed.AdminEmail)") {
    throw 'Seed:SuperAdminEmail and Seed:AdminEmail must be different addresses.'
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
