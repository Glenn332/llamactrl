#!/usr/bin/env bash
set -e

OS="$(uname -s)"
ARCH="$(uname -m)"

# Detect RID
if [[ "$OS" == "Darwin" && "$ARCH" == "arm64" ]]; then
  RID="osx-arm64"
elif [[ "$OS" == "Darwin" ]]; then
  RID="osx-x64"
elif [[ "$ARCH" == "aarch64" ]]; then
  RID="linux-arm64"
else
  RID="linux-x64"
fi

ROOT="$(cd "$(dirname "$0")" && pwd)"
DIST="$ROOT/dist/$RID"

# ── Build ──────────────────────────────────────────────────────────────────

echo "==> Building LlamaCtrl for $RID"
echo ""

echo "[1/2] Building React frontend..."
cd "$ROOT/frontend"
npm install --silent
npm run build
echo "      Done."
echo ""

echo "[2/2] Publishing .NET backend..."
cd "$ROOT/src/LlamaCtrl"
dotnet publish -c Release -r "$RID" --self-contained true \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -o "$DIST"
echo "      Done."
echo ""

# ── Install ────────────────────────────────────────────────────────────────

# Platform-specific install paths
INSTALL_DIR="/usr/local/bin"
USE_SUDO=true
if [[ "$EUID" -ne 0 ]] && ! sudo -n true 2>/dev/null; then
  INSTALL_DIR="$HOME/.local/bin"
  USE_SUDO=false
  mkdir -p "$INSTALL_DIR"
  echo "No sudo available — installing to user directory."
fi

echo "==> Installing llamactrl"
echo "    Command -> $INSTALL_DIR/llamactrl"
echo ""

if [[ "$USE_SUDO" == true ]]; then
  sudo install -m 755 "$DIST/llamactrl" "$INSTALL_DIR/llamactrl"
else
  install -m 755 "$DIST/llamactrl" "$INSTALL_DIR/llamactrl"
fi

# Remind users if ~/.local/bin is not on PATH
if [[ "$INSTALL_DIR" == "$HOME/.local/bin" ]]; then
  if [[ ":$PATH:" != *":$HOME/.local/bin:"* ]]; then
    echo "NOTE: Add ~/.local/bin to your PATH:"
    if [[ "$OS" == "Darwin" ]]; then
      echo "  echo 'export PATH=\"\$HOME/.local/bin:\$PATH\"' >> ~/.zshrc && source ~/.zshrc"
    else
      echo "  echo 'export PATH=\"\$HOME/.local/bin:\$PATH\"' >> ~/.bashrc && source ~/.bashrc"
    fi
    echo ""
  fi
fi

echo "Done! Run: llamactrl"
