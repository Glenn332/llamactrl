#!/usr/bin/env bash
set -e

# ── LlamaCtrl Installer ───────────────────────────────────────────────────
# Downloads the latest release binary from GitHub and installs it.
# Usage: curl -fsSL <raw-url>/install.sh | bash

# GitHub repository — update this if you fork the project
REPO="Glenn332/llamactrl"

# Try to auto-detect from git remote if running from a clone
if command -v git &>/dev/null && git rev-parse --is-inside-work-tree &>/dev/null 2>&1; then
  REMOTE_URL="$(git remote get-url origin 2>/dev/null || true)"
  if [[ -n "$REMOTE_URL" ]]; then
    # Extract owner/repo from HTTPS or SSH URL
    DETECTED="$(echo "$REMOTE_URL" | sed -E 's#.*github\.com[:/]([^/]+/[^/.]+)(\.git)?$#\1#')"
    if [[ "$DETECTED" != "$REMOTE_URL" && -n "$DETECTED" ]]; then
      REPO="$DETECTED"
    fi
  fi
fi

echo "==> LlamaCtrl Installer"
echo "    Repository: $REPO"
echo ""

# ── Detect platform ───────────────────────────────────────────────────────

OS="$(uname -s)"
ARCH="$(uname -m)"

case "$OS" in
  Darwin)
    case "$ARCH" in
      arm64) RID="osx-arm64" ;;
      *)     RID="osx-x64" ;;
    esac
    ;;
  Linux)
    case "$ARCH" in
      aarch64) RID="linux-arm64" ;;
      *)       RID="linux-x64" ;;
    esac
    ;;
  *)
    echo "Error: Unsupported operating system: $OS" >&2
    echo "       Use install.ps1 for Windows." >&2
    exit 1
    ;;
esac

ASSET="llamactrl-${RID}.tar.gz"
echo "[1/4] Detected platform: $RID"

# ── Fetch latest release URL ──────────────────────────────────────────────

API_URL="https://api.github.com/repos/${REPO}/releases/latest"

echo "[2/4] Fetching latest release info..."

if command -v curl &>/dev/null; then
  RELEASE_JSON="$(curl -fsSL "$API_URL")"
elif command -v wget &>/dev/null; then
  RELEASE_JSON="$(wget -qO- "$API_URL")"
else
  echo "Error: Neither curl nor wget found. Please install one and retry." >&2
  exit 1
fi

# Extract the browser_download_url for our asset (no jq dependency)
DOWNLOAD_URL="$(echo "$RELEASE_JSON" | grep -o "\"browser_download_url\"[[:space:]]*:[[:space:]]*\"[^\"]*${ASSET}\"" | sed 's/.*"browser_download_url"[[:space:]]*:[[:space:]]*"\([^"]*\)"/\1/')"

if [[ -z "$DOWNLOAD_URL" ]]; then
  echo "Error: Could not find asset '$ASSET' in the latest release." >&2
  echo "       Available assets:" >&2
  echo "$RELEASE_JSON" | grep -o '"browser_download_url"[[:space:]]*:[[:space:]]*"[^"]*"' | sed 's/.*"\(http[^"]*\)"/         \1/' >&2
  exit 1
fi

TAG="$(echo "$RELEASE_JSON" | grep -o '"tag_name"[[:space:]]*:[[:space:]]*"[^"]*"' | sed 's/.*"\([^"]*\)"$/\1/' | head -1)"
echo "       Latest version: ${TAG:-unknown}"
echo "       Asset: $ASSET"

# ── Download ──────────────────────────────────────────────────────────────

TMPDIR="$(mktemp -d)"
trap 'rm -rf "$TMPDIR"' EXIT

ARCHIVE="$TMPDIR/$ASSET"

echo "[3/4] Downloading $ASSET..."

if command -v curl &>/dev/null; then
  curl -fSL --progress-bar -o "$ARCHIVE" "$DOWNLOAD_URL"
elif command -v wget &>/dev/null; then
  wget -q --show-progress -O "$ARCHIVE" "$DOWNLOAD_URL"
fi

# ── Extract and install ───────────────────────────────────────────────────

echo "[4/4] Installing..."

tar -xzf "$ARCHIVE" -C "$TMPDIR"

# Determine install location
INSTALL_DIR=""
USE_SUDO=false

if [[ "$EUID" -eq 0 ]] || sudo -n true 2>/dev/null; then
  INSTALL_DIR="/usr/local/bin"
  USE_SUDO=true
  if [[ "$EUID" -eq 0 ]]; then
    USE_SUDO=false
  fi
else
  INSTALL_DIR="$HOME/.local/bin"
  mkdir -p "$INSTALL_DIR"
fi

# Find the binary in the extracted files
BINARY="$(find "$TMPDIR" -name "llamactrl" -type f ! -name "*.tar.gz" | head -1)"

if [[ -z "$BINARY" ]]; then
  echo "Error: Could not find 'llamactrl' binary in the archive." >&2
  exit 1
fi

if [[ "$USE_SUDO" == true ]]; then
  sudo install -m 755 "$BINARY" "$INSTALL_DIR/llamactrl"
else
  install -m 755 "$BINARY" "$INSTALL_DIR/llamactrl"
fi

echo ""
echo "==> llamactrl installed to $INSTALL_DIR/llamactrl"

# PATH reminder for user-local installs
if [[ "$INSTALL_DIR" == "$HOME/.local/bin" ]]; then
  if [[ ":$PATH:" != *":$HOME/.local/bin:"* ]]; then
    echo ""
    echo "NOTE: $INSTALL_DIR is not on your PATH. Add it with:"
    if [[ "$OS" == "Darwin" ]]; then
      echo "  echo 'export PATH=\"\$HOME/.local/bin:\$PATH\"' >> ~/.zshrc && source ~/.zshrc"
    else
      echo "  echo 'export PATH=\"\$HOME/.local/bin:\$PATH\"' >> ~/.bashrc && source ~/.bashrc"
    fi
  fi
fi

echo ""
echo "Done! Run: llamactrl"
