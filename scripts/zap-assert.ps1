param(
  [string]$ZapApiUrl = $(if ($env:ZAP_PROXY_API_URL) { $env:ZAP_PROXY_API_URL } else { 'http://127.0.0.1:8080' }),
  [int]$AppPort = $(if ($env:ZAP_APP_PORT) { [int]$env:ZAP_APP_PORT } else { 8085 }),
  [string]$ReportsDir = $(if ($env:ZAP_REPORTS_DIR) { $env:ZAP_REPORTS_DIR } else { 'zap-reports' })
)

$ErrorActionPreference = 'Stop'
$api = $ZapApiUrl.TrimEnd('/')

# Passive alerts are raised on a background queue. recordsToScan is 0 only
# after that queue has drained; asserting earlier can miss High alerts.
$deadline = (Get-Date).AddSeconds(60)
$remaining = -1
while ((Get-Date) -lt $deadline) {
  $payload = Invoke-RestMethod "$api/JSON/pscan/view/recordsToScan/"
  if ($null -eq $payload.recordsToScan) {
    throw "ZAP recordsToScan was missing: $($payload | ConvertTo-Json -Compress)"
  }
  $remaining = [int]$payload.recordsToScan
  if ($remaining -eq 0) { break }
  Start-Sleep -Seconds 1
}

if ($remaining -ne 0) {
  throw "ZAP passive scan still has $remaining record(s) to scan after 60s."
}

New-Item -ItemType Directory -Force -Path $ReportsDir | Out-Null

Invoke-WebRequest -Uri "$api/OTHER/core/other/htmlreport/" -OutFile (Join-Path $ReportsDir 'zap-report.html') -UseBasicParsing
Invoke-WebRequest -Uri "$api/OTHER/core/other/jsonreport/" -OutFile (Join-Path $ReportsDir 'zap-report.json') -UseBasicParsing

$sites = @((Invoke-RestMethod "$api/JSON/core/view/sites/").sites)
$appSites = @($sites | Where-Object {
    try { ([uri]$_).Port -eq $AppPort } catch { $false }
  })

if ($appSites.Count -eq 0) {
  $recorded = if ($sites.Count) { $sites -join ', ' } else { '(none)' }
  throw "ZAP saw no traffic on port $AppPort. Sites recorded: $recorded. Use curl.exe -x http://127.0.0.1:8080 so the request is proxied."
}

$failed = $false
foreach ($site in $appSites) {
  $encoded = [uri]::EscapeDataString($site)
  $summary = (Invoke-RestMethod "$api/JSON/alert/view/alertsSummary/?baseurl=$encoded").alertsSummary
  Write-Host "$site High=$($summary.High) Medium=$($summary.Medium) Low=$($summary.Low) Informational=$($summary.Informational)"
  if ([int]$summary.High -gt 0) {
    Write-Error "$site has $($summary.High) High alert(s). See zap-reports/zap-report.html - fix the app rather than weakening this gate."
    $failed = $true
  }
}

if ($failed) { exit 1 }
