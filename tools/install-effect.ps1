<#
.SYNOPSIS
  Builds the SignalRGB effect (inlines region-renderer.js into the template) and installs it.
.DESCRIPTION
  Output: signalrgb-effect\dist\Sonolume.html and a copy in the SignalRGB user effects folder.
  SignalRGB only discovers new effect files on restart. After the first install: restart SignalRGB,
  then select "Sonolume" in its effect library. Later installs of the same file are picked up on
  the next effect reload.
.PARAMETER EffectsDir
  SignalRGB user effects folder. Default: D:\Documents\WhirlwindFX\Effects (falls back to Documents\WhirlwindFX\Effects).
#>
param(
    [string]$EffectsDir
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$src = Join-Path $root 'signalrgb-effect'
$template = Get-Content (Join-Path $src 'Sonolume.template.html') -Raw
$renderer = Get-Content (Join-Path $src 'region-renderer.js') -Raw
$html = $template.Replace('/*__RENDERER__*/', $renderer)

$dist = Join-Path $src 'dist'
New-Item -ItemType Directory -Force $dist | Out-Null
$distFile = Join-Path $dist 'Sonolume.html'
Set-Content -Path $distFile -Value $html -Encoding UTF8 -NoNewline
Write-Host "Built $distFile ($($html.Length) chars)"

if (-not $EffectsDir) {
    $candidates = @('D:\Documents\WhirlwindFX\Effects', (Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'WhirlwindFX\Effects'))
    $EffectsDir = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
    if (-not $EffectsDir) { $EffectsDir = $candidates[1] }
}
New-Item -ItemType Directory -Force $EffectsDir | Out-Null
$target = Join-Path $EffectsDir 'Sonolume.html'
$isNew = -not (Test-Path $target)
Copy-Item $distFile $target -Force
Write-Host "Installed $target"
if ($isNew) {
    Write-Host "New effect file: restart SignalRGB, then select 'Sonolume' in the effect library." -ForegroundColor Yellow
}
