param([int]$Port = 5188)
$ErrorActionPreference = 'Stop'
$dotnetCmd = if ($env:DOTNET_PATH) { $env:DOTNET_PATH } else { 'dotnet' }
if (!(Test-Path (Join-Path $PSScriptRoot 'RiderForm/node_modules/typescript/bin/tsc'))) {
  & npm ci --prefix (Join-Path $PSScriptRoot 'RiderForm')
  if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:ASPNETCORE_URLS = "http://127.0.0.1:$Port"
& $dotnetCmd run --project (Join-Path $PSScriptRoot 'Payroll.Api') --no-launch-profile
exit $LASTEXITCODE
