<#
.SYNOPSIS
  Phase 0 probe for the SignalRGB Canvas API: accepted HTTP verbs, payload ceiling, sustained-rate soak.
.DESCRIPTION
  Requires SignalRGB running. Results are printed and appended to docs\protocol.md when -Record is set.
  Delivery into the effect can only be observed inside SignalRGB (enable "Show Debug Text" in the
  Sonolume effect and watch the frame counter); this script measures the server side.
#>
param(
    [string]$Endpoint = 'http://localhost:16034/canvas/event',
    [string]$Sender = 'SonolumeProbe',
    [int[]]$Rates = @(30, 60, 120),
    [int]$SecondsPerRate = 20,
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

Out "## Soak"
$proc = Get-Process SignalRgb -ErrorAction SilentlyContinue | Sort-Object CPU -Descending | Select-Object -First 1
$payload = 'S1|probe000|0|0:0101FF0000;1:01010000FF;2:0101FFFF00'
foreach ($rate in $Rates) {
    $interval = 1000.0 / $rate
    $cpu0 = if ($proc) { (Get-Process -Id $proc.Id).TotalProcessorTime } else { [TimeSpan]::Zero }
    $sockets0 = (Get-NetTCPConnection -RemotePort 16034 -ErrorAction SilentlyContinue | Measure-Object).Count
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $sent = 0; $fail = 0; $next = 0.0
    $lat = New-Object System.Collections.Generic.List[double]
    while ($sw.Elapsed.TotalSeconds -lt $SecondsPerRate) {
        $t0 = $sw.Elapsed.TotalMilliseconds
        try { $r = $client.GetAsync("$Endpoint`?sender=$Sender&event=" + [Uri]::EscapeDataString("$payload|$sent")).GetAwaiter().GetResult(); $r.Dispose(); $sent++ } catch { $fail++ }
        $lat.Add($sw.Elapsed.TotalMilliseconds - $t0)
        $next += $interval
        $wait = $next - $sw.Elapsed.TotalMilliseconds
        if ($wait -gt 0) { [System.Threading.Thread]::Sleep([int]$wait) }
    }
    $cpu1 = if ($proc) { (Get-Process -Id $proc.Id).TotalProcessorTime } else { [TimeSpan]::Zero }
    $sockets1 = (Get-NetTCPConnection -RemotePort 16034 -ErrorAction SilentlyContinue | Measure-Object).Count
    $sorted = $lat | Sort-Object
    $cpuPct = ($cpu1 - $cpu0).TotalSeconds / $SecondsPerRate * 100 / [Environment]::ProcessorCount
    Out ("rate={0}/s sent={1} failed={2} p50={3:F2}ms p99={4:F2}ms max={5:F2}ms SignalRgb_cpu={6:F1}% sockets {7}->{8}" -f $rate, $sent, $fail, $sorted[[int]($sorted.Count * 0.5)], $sorted[[int]($sorted.Count * 0.99)], $sorted[-1], $cpuPct, $sockets0, $sockets1)
}

if ($Record) {
    $doc = Join-Path (Split-Path -Parent $PSScriptRoot) 'docs\protocol.md'
    Add-Content $doc ("`n" + ($lines -join "`n"))
    Write-Host "Appended to $doc"
}
