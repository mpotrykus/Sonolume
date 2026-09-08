<#
.SYNOPSIS
  Phase 0/1 probe for the SignalRGB Canvas API: accepted HTTP verbs, payload ceiling, sustained-rate
  soak at both the tiny cell-protocol size and image-sized (base64 PNG-like) payloads.
.DESCRIPTION
  Requires SignalRGB running. Results are printed and appended to docs\protocol.md when -Record is set.
  Delivery into the effect can only be observed inside SignalRGB (enable "Show Debug Text" in the
  Sonolume effect and watch the frame counter); this script measures the server side.

  -ImageSizesKB controls the Phase 1 image-payload soak (raw byte size before base64, sent as an I1
  message alongside the existing S1 soak). Each request gets freshly randomized bytes (not a fixed
  blob resent every time) since failure rate turned out to depend on payload size and per-request
  content, not just raw length -- a fixed low-entropy payload (repeated 'A', as Phase 0's ceiling
  test used) is unrepresentative and reliably hides the failures a real varying image would hit. Each
  size gets its own fresh HttpClient/connection so one size's failures can't bleed into the next.
#>
param(
    [string]$Endpoint = 'http://localhost:16034/canvas/event',
    [string]$Sender = 'SonolumeProbe',
    [int[]]$Rates = @(30, 60, 120),
    [int]$SecondsPerRate = 20,
    [int[]]$ImageSizesKB = @(10, 20, 50, 90, 150, 256, 400),
    [int]$ImageRate = 60,
    [int]$ImageSecondsPerSize = 30,
    [switch]$SkipCellSoak,
    [switch]$SkipImageSoak,
    [switch]$Record
)

$ErrorActionPreference = 'Continue'
$handler = [System.Net.Http.SocketsHttpHandler]::new()
$handler.PooledConnectionLifetime = [TimeSpan]::FromMinutes(10)
$client = [System.Net.Http.HttpClient]::new($handler)
$client.Timeout = [TimeSpan]::FromMilliseconds(500)
$lines = New-Object System.Collections.Generic.List[string]
function Out($s) { Write-Host $s; $lines.Add($s) }

function Try-Request($label, $task) {
    try {
        $r = $task.GetAwaiter().GetResult()
        $body = $r.Content.ReadAsStringAsync().Result
        Out ("{0,-28} HTTP {1} {2}" -f $label, [int]$r.StatusCode, $body.Substring(0, [Math]::Min(60, $body.Length)))
        $r.Dispose()
    } catch { Out ("{0,-28} EXC {1}" -f $label, $_.Exception.GetBaseException().Message) }
}

Out "# Canvas API probe $(Get-Date -Format s)"
Out "## Verbs"
Try-Request 'GET query' $client.GetAsync("$Endpoint`?sender=$Sender&event=P1|hello")
Try-Request 'POST query, empty body' $client.PostAsync("$Endpoint`?sender=$Sender&event=P1|hello", [System.Net.Http.StringContent]::new(''))
Try-Request 'POST json body' $client.PostAsync($Endpoint, [System.Net.Http.StringContent]::new('{"sender":"' + $Sender + '","event":"P1|hello"}', [System.Text.Encoding]::UTF8, 'application/json'))
Try-Request 'GET missing event' $client.GetAsync("$Endpoint`?sender=$Sender")

Out "## Payload ceiling (GET query)"
foreach ($n in 1024, 8192, 65536, 262144) {
    Try-Request "event length $n" $client.GetAsync("$Endpoint`?sender=$Sender&event=P1|$('A' * $n)")
}

function Run-Soak($label, $httpClient, $payloadBuilder, $rate, $seconds) {
    $proc = Get-Process SignalRgb -ErrorAction SilentlyContinue | Sort-Object CPU -Descending | Select-Object -First 1
    $interval = 1000.0 / $rate
    $cpu0 = if ($proc) { (Get-Process -Id $proc.Id).TotalProcessorTime } else { [TimeSpan]::Zero }
    $sockets0 = (Get-NetTCPConnection -RemotePort 16034 -ErrorAction SilentlyContinue | Measure-Object).Count
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $sent = 0; $fail = 0; $next = 0.0
    $streak = 0; $maxStreak = 0
    $lat = New-Object System.Collections.Generic.List[double]
    while ($sw.Elapsed.TotalSeconds -lt $seconds) {
        $t0 = $sw.Elapsed.TotalMilliseconds
        $event = & $payloadBuilder $sent
        $ok = $false
        try { $r = $httpClient.GetAsync("$Endpoint`?sender=$Sender&event=" + [Uri]::EscapeDataString($event)).GetAwaiter().GetResult(); $ok = ($r.StatusCode -eq 200); $r.Dispose() } catch { $ok = $false }
        if ($ok) { $sent++; $streak = 0 } else { $fail++; $streak++; if ($streak -gt $maxStreak) { $maxStreak = $streak } }
        $lat.Add($sw.Elapsed.TotalMilliseconds - $t0)
        $next += $interval
        $wait = $next - $sw.Elapsed.TotalMilliseconds
        if ($wait -gt 0) { [System.Threading.Thread]::Sleep([int]$wait) }
    }
    $cpu1 = if ($proc) { (Get-Process -Id $proc.Id).TotalProcessorTime } else { [TimeSpan]::Zero }
    $sockets1 = (Get-NetTCPConnection -RemotePort 16034 -ErrorAction SilentlyContinue | Measure-Object).Count
    $sorted = $lat | Sort-Object
    $cpuPct = ($cpu1 - $cpu0).TotalSeconds / $seconds * 100 / [Environment]::ProcessorCount
    $total = $sent + $fail
    $failPct = if ($total -gt 0) { $fail * 100.0 / $total } else { 0 }
    Out ("{0} rate={1}/s sent={2} failed={3} ({4:F1}%) maxStreak={5} p50={6:F2}ms p99={7:F2}ms max={8:F2}ms SignalRgb_cpu={9:F1}% sockets {10}->{11}" -f $label, $rate, $sent, $fail, $failPct, $maxStreak, $sorted[[int]($sorted.Count * 0.5)], $sorted[[int]($sorted.Count * 0.99)], $sorted[-1], $cpuPct, $sockets0, $sockets1)
}

if (-not $SkipCellSoak) {
    Out "## Cell-protocol soak (S1, ~90 bytes)"
    $payload = 'S1|probe000|0|0:0101FF0000;1:01010000FF;2:0101FFFF00'
    foreach ($rate in $Rates) {
        Run-Soak 'cell' $client { param($sent) "$payload|$sent" } $rate $SecondsPerRate
    }
}

if (-not $SkipImageSoak) {
    Out "## Image-payload soak (I1, base64 PNG-like, fresh random bytes per request)"
    $rng = [System.Random]::new(42)
    foreach ($kb in $ImageSizesKB) {
        $bytes = New-Object byte[] ($kb * 1024)
        $imgHandler = [System.Net.Http.SocketsHttpHandler]::new()
        $imgHandler.PooledConnectionLifetime = [TimeSpan]::FromMinutes(10)
        $imgClient = [System.Net.Http.HttpClient]::new($imgHandler)
        $imgClient.Timeout = [TimeSpan]::FromSeconds(5)
        Run-Soak "image ${kb}KB" $imgClient { param($sent) $rng.NextBytes($bytes); "I1|$sent|64|64|" + [Convert]::ToBase64String($bytes) } $ImageRate $ImageSecondsPerSize
        $imgClient.Dispose()
    }
}

if ($Record) {
    $doc = Join-Path (Split-Path -Parent $PSScriptRoot) 'docs\protocol.md'
    Add-Content $doc ("`n" + ($lines -join "`n"))
    Write-Host "Appended to $doc"
}
