param([string]$OutputPath = (Join-Path $PSScriptRoot 'artifacts/monsterasp'), [string]$Runtime = 'win-x64')
$ErrorActionPreference = 'Stop'
$dotnetCmd = if ($env:DOTNET_PATH) { $env:DOTNET_PATH } else { 'dotnet' }
if (!(Test-Path (Join-Path $PSScriptRoot 'RiderForm/node_modules/typescript/bin/tsc'))) {
  & npm ci --prefix (Join-Path $PSScriptRoot 'RiderForm')
  if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
& $dotnetCmd publish (Join-Path $PSScriptRoot 'Payroll.Api') -c Release -r $Runtime --self-contained true -o $OutputPath --nologo
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
if ($Runtime -eq 'win-x64') {
  & python (Join-Path $PSScriptRoot 'scripts/check_publish.py') $OutputPath
  if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
