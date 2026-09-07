<#
.SYNOPSIS
  Builds the Sonolume VST3 plugin and copies it to the system VST3 folder.
.DESCRIPTION
  The AudioPlugSharp bridge (SonolumeBridge.vst3) plus all managed DLLs must live in the same folder.
  Writing to "C:\Program Files\Common Files\VST3" needs an elevated shell; pass -Destination to use
  a user-writable folder and add that folder to your DAW's VST3 scan paths instead.
  After installing, rescan plugins in the DAW (REAPER: Options > Preferences > Plug-ins > VST > Re-scan).
#>
param(
    [string]$Configuration = 'Release',
    [string]$Destination = 'C:\Program Files\Common Files\VST3\Sonolume',
    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'src\Sonolume.Plugin\Sonolume.Plugin.csproj'

if (-not $SkipBuild) {
    dotnet build $project -c $Configuration --nologo
    if ($LASTEXITCODE -ne 0) { throw "Build failed." }
}

$out = Join-Path $root "src\Sonolume.Plugin\bin\$Configuration\net10.0-windows"
if (-not (Test-Path (Join-Path $out 'SonolumeBridge.vst3'))) {
    throw "SonolumeBridge.vst3 not found in $out. Did the AudioPlugSharpVst3 package copy its files?"
}

New-Item -ItemType Directory -Force $Destination | Out-Null
Copy-Item (Join-Path $out '*') $Destination -Recurse -Force
Write-Host "Installed to $Destination"
Get-ChildItem $Destination -Filter '*.vst3' | ForEach-Object { Write-Host "  $($_.Name)" }
Write-Host "Rescan plugins in your DAW, then insert 'Sonolume' as an instrument."
