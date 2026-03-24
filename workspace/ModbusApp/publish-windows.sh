#!/usr/bin/env bash
# Publishes Modbus.Desktop as a self-contained Windows x64 executable.
# Run this from WSL2 or a dev container that has access to /mnt/c.
# The output is written to the Windows user's Desktop by default.

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT="$SCRIPT_DIR/Modbus.Desktop/Modbus.Desktop.csproj"

# ── Resolve output path ───────────────────────────────────────────────────────

if [[ -n "${PUBLISH_OUT:-}" ]]; then
    OUT="$PUBLISH_OUT"
elif [[ -d "/mnt/c" ]]; then
    # Running inside WSL2 or a container with the Windows filesystem mounted.
    WIN_USER="$(cmd.exe /c "echo %USERNAME%" 2>/dev/null | tr -d '\r\n')"
    if [[ -z "$WIN_USER" || "$WIN_USER" == "%USERNAME%" ]]; then
        # cmd.exe not available (pure Linux). Fall back to a local output folder.
        OUT="$SCRIPT_DIR/publish/win-x64"
    else
        OUT="/mnt/c/Users/$WIN_USER/Desktop/ModbusApp"
    fi
else
    OUT="$SCRIPT_DIR/publish/win-x64"
fi

# ── Publish ───────────────────────────────────────────────────────────────────

echo "Publishing Modbus.Desktop → $OUT"
echo ""

dotnet publish "$PROJECT" \
    --runtime win-x64 \
    --self-contained true \
    --configuration Release \
    --output "$OUT" \
    -p:PublishSingleFile=true \
    -p:IncludeNativeLibrariesForSelfExtract=true

echo ""
echo "Done. Run Modbus.Desktop.exe from:"
echo "  $OUT"
