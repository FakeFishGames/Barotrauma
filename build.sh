#!/bin/bash
# Build script for Barotrauma from source
# Usage: ./build.sh [clean]
#   ./build.sh        - Incremental build (fast, but may use cached artifacts)
#   ./build.sh clean  - Clean build (slower, guarantees fresh compilation)

set -e

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
cd "$SCRIPT_DIR"

CLIENT_PROJ="Barotrauma/BarotraumaClient/LinuxClient.csproj"
SERVER_PROJ="Barotrauma/BarotraumaServer/LinuxServer.csproj"
CONFIG="Release"
OUTPUT_DIR="Barotrauma/bin/ReleaseLinux/net8.0"

# Show current git info
echo "=== Git Info ==="
echo "Branch: $(git rev-parse --abbrev-ref HEAD)"
echo "Commit: $(git rev-parse --short HEAD) - $(git log -1 --format='%s')"
echo ""

# Clean if requested
if [ "$1" = "clean" ]; then
    echo "=== Cleaning build artifacts ==="
    rm -rf Barotrauma/bin/ReleaseLinux
    rm -rf Barotrauma/BarotraumaClient/obj
    rm -rf Barotrauma/BarotraumaServer/obj
    rm -rf Barotrauma/BarotraumaShared/obj
    echo "Clean complete."
    echo ""
fi

# Build server
echo "=== Building Server ==="
dotnet build "$SERVER_PROJ" /p:Configuration=$CONFIG
echo ""

# Build client
echo "=== Building Client ==="
dotnet build "$CLIENT_PROJ" /p:Configuration=$CONFIG
echo ""

# Verify output
if [ -f "$OUTPUT_DIR/Barotrauma" ]; then
    echo "=== Build Successful ==="
    echo "Client binary: $OUTPUT_DIR/Barotrauma"
    echo "Server binary: $OUTPUT_DIR/DedicatedServer"
    echo ""
    echo "To run the client:"
    echo "  cd $OUTPUT_DIR && ./Barotrauma"
else
    echo "=== Build may have failed - binary not found ==="
    echo "Expected: $OUTPUT_DIR/Barotrauma"
    exit 1
fi
