#!/usr/bin/env bash
set -e

RID="${1:-osx-arm64}"
ROOT="$(cd "$(dirname "$0")" && pwd)"

echo "==> Building LlamaCtrl for $RID"
echo ""

# Step 1: Build frontend
echo "[1/2] Building React frontend..."
cd "$ROOT/src/frontend"
npm install --silent
npm run build
echo "      Frontend built -> src/LlamaCtrl/wwwroot/"
echo ""

# Step 2: Build .NET backend
echo "[2/2] Building .NET backend..."
cd "$ROOT/src/LlamaCtrl"
dotnet publish -c Release -r "$RID" --self-contained true \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -o "$ROOT/dist/$RID"

echo ""
echo "==> Build complete!"
echo "    Binary: $ROOT/dist/$RID/llamactrl"
echo ""
echo "To install globally:"
echo "    sudo cp $ROOT/dist/$RID/llamactrl /usr/local/bin/llamactrl"
echo "    chmod +x /usr/local/bin/llamactrl"
echo ""
echo "Then run: llamactrl"
