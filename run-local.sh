#!/usr/bin/env bash
set -euo pipefail
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
DOTNET_CMD="${DOTNET_PATH:-dotnet}"
command -v "$DOTNET_CMD" >/dev/null || { echo 'Install .NET 10 SDK or set DOTNET_PATH.' >&2; exit 1; }
command -v npm >/dev/null || { echo 'Node.js/npm are required to build the frontend.' >&2; exit 1; }
if [ ! -f "$SCRIPT_DIR/RiderForm/node_modules/typescript/bin/tsc" ]; then
  npm ci --prefix "$SCRIPT_DIR/RiderForm"
fi
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export ASPNETCORE_ENVIRONMENT=Development
export ASPNETCORE_URLS="http://127.0.0.1:${1:-5188}"
exec "$DOTNET_CMD" run --project "$SCRIPT_DIR/Payroll.Api" --no-launch-profile
