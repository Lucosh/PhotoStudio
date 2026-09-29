<#
.SYNOPSIS
    Builds the PhotoStudio release files into .\artifacts:
      - PhotoStudio-<version>-win-x64-setup.exe     installer (needs Inno Setup 6)
      - PhotoStudio-<version>-win-x64-portable.zip  single exe, no installation
      - SHA256SUMS.txt                              checksums of the files above

    The same script runs on GitHub Actions, so anyone can rebuild a release from the source.

.EXAMPLE
    .\build.ps1 -Version 1.2.0
#>
param(
    [string]$Version = '1.0.0',
    [string]$RepoUrl = '',
    # Fail instead of skipping the installer when Inno Setup is not installed.
    [switch]$RequireInstaller
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$artifacts = Join-Path $root 'artifacts'
$publish = Join-Path $artifacts 'publish'

# "1.2.0-beta.1" -> "1.2.0": Windows file versions must be numeric.
$numeric = ($Version -split '[-+]')[0]
if ($numeric -notmatch '^\d+\.\d+\.\d+$') { throw "Version must look like 1.2.3 or 1.2.3-beta.1 (got '$Version')." }

if (Test-Path $artifacts) { Remove-Item -Recurse -Force $artifacts }

Write-Host "== Publishing PhotoStudio $Version" -ForegroundColor Cyan
dotnet publish (Join-Path $root 'PhotoStudio.csproj') `
    -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:DebugType=none `
    -p:Version=$Version -p:FileVersion="$numeric.0" -p:AssemblyVersion="$numeric.0" `
    -o $publish
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed.' }

Copy-Item (Join-Path $root 'LICENSE') (Join-Path $publish 'LICENSE.txt')
Copy-Item (Join-Path $root 'THIRD-PARTY-NOTICES.md') $publish

Write-Host '== Creating portable zip' -ForegroundColor Cyan
Compress-Archive -Path (Join-Path $publish '*') -DestinationPath (Join-Path $artifacts "PhotoStudio-$Version-win-x64-portable.zip")

Write-Host '== Building installer' -ForegroundColor Cyan
$iscc = (Get-Command iscc.exe -ErrorAction SilentlyContinue).Source
if (-not $iscc) {
    $iscc = @(
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
        "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
    ) | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
}
if ($iscc) {
    & $iscc /Qp "/DAppVersion=$Version" "/DFileVersion=$numeric" "/DSourceDir=$publish" `
        "/DOutputDir=$artifacts" "/DAppUrl=$RepoUrl" (Join-Path $root 'installer\PhotoStudio.iss')
    if ($LASTEXITCODE -ne 0) { throw 'Inno Setup failed.' }
}
elseif ($RequireInstaller) { throw 'Inno Setup 6 not found.' }
else { Write-Warning 'Inno Setup 6 not found: installer skipped (https://jrsoftware.org/isdl.php).' }

Write-Host '== Computing checksums' -ForegroundColor Cyan
$lines = Get-ChildItem $artifacts -File | Sort-Object Name | ForEach-Object {
    '{0}  {1}' -f (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $_.Name
}
$lines | Set-Content (Join-Path $artifacts 'SHA256SUMS.txt') -Encoding ascii
$lines | ForEach-Object { Write-Host "   $_" }
Write-Host "Done: $artifacts" -ForegroundColor Green
