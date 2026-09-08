$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$fixture = Join-Path $PSScriptRoot "fixtures"
$logDir = Join-Path $root "artifacts\tunnel-test"
New-Item -ItemType Directory -Path $logDir -Force | Out-Null
$stdout = Join-Path $logDir "cloudflared.out.log"
$stderr = Join-Path $logDir "cloudflared.err.log"
$server = Start-Process -FilePath "python" -ArgumentList "-m", "http.server", "18765", "--bind", "127.0.0.1" -WorkingDirectory $fixture -WindowStyle Hidden -PassThru
$tunnel = Start-Process -FilePath (Join-Path $root "src\PlatformTools.App\tools\cloudflared.exe") -ArgumentList "tunnel", "--no-autoupdate", "--url", "http://127.0.0.1:18765" -RedirectStandardOutput $stdout -RedirectStandardError $stderr -WindowStyle Hidden -PassThru
try {
    $publicUrl = $null
    for ($i = 0; $i -lt 30 -and -not $publicUrl; $i++) {
        Start-Sleep -Seconds 1
        $logs = (Get-Content -LiteralPath $stdout -Raw -ErrorAction SilentlyContinue) + (Get-Content -LiteralPath $stderr -Raw -ErrorAction SilentlyContinue)
        if ($logs -match "https://[a-z0-9-]+\.trycloudflare\.com") { $publicUrl = $Matches[0] }
    }
    if (-not $publicUrl) { throw "Quick Tunnel did not produce a public URL." }
    $rawResponse = & curl.exe -4 -fsS --retry 15 --retry-delay 1 --retry-all-errors --max-time 10 $publicUrl
    $response = if ($null -eq $rawResponse) { $null } else { $rawResponse.Trim() }
    if (-not $response) { throw "Quick Tunnel URL was created but did not become reachable." }
    if ($response -ne "Platform Tools tunnel test OK") { throw "Unexpected public response: $response" }
    [pscustomobject]@{ PublicUrl = $publicUrl; Response = $response }
}
finally {
    Stop-Process -Id $tunnel.Id -Force -ErrorAction SilentlyContinue
    Stop-Process -Id $server.Id -Force -ErrorAction SilentlyContinue
}
