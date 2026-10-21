#!/usr/bin/env bash
set -euo pipefail
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
DOTNET_CMD="${DOTNET_PATH:-dotnet}"
OUTPUT_PATH="${1:-$SCRIPT_DIR/artifacts/monsterasp}"
RUNTIME="${2:-win-x64}"
command -v "$DOTNET_CMD" >/dev/null || { echo 'Install .NET 10 SDK or set DOTNET_PATH.' >&2; exit 1; }
if [ ! -f "$SCRIPT_DIR/RiderForm/node_modules/typescript/bin/tsc" ]; then
  npm ci --prefix "$SCRIPT_DIR/RiderForm"
fi
python3 "$SCRIPT_DIR/scripts/fetch_fonts.py"
export DOTNET_CLI_TELEMETRY_OPTOUT=1
"$DOTNET_CMD" publish "$SCRIPT_DIR/Payroll.Api" -c Release -r "$RUNTIME" --self-contained true -o "$OUTPUT_PATH" --nologo
if [ "$RUNTIME" = win-x64 ]; then python3 "$SCRIPT_DIR/scripts/check_publish.py" "$OUTPUT_PATH"; fi
