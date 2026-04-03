# LlamaCtrl

A web UI for running and managing [llama-server](https://github.com/ggerganov/llama.cpp) processes on your local machine. Ships as a single binary, no setup needed beyond having llama-server somewhere on your system.

<!-- Uncomment when CI and releases are set up:
[![Build](https://github.com/Glenn332/llamactrl/actions/workflows/build.yml/badge.svg)](https://github.com/Glenn332/llamactrl/actions)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![GitHub Release](https://img.shields.io/github/v/release/Glenn332/llamactrl)](https://github.com/Glenn332/llamactrl/releases)
-->

## What does it do?

Instead of running llama-server by hand every time, LlamaCtrl gives you a browser-based dashboard to:

- Start and stop multiple llama-server instances
- Save model configs as profiles so you don't have to retype flags
- Run benchmarks and compare results side by side
- Watch logs live while a server is running
- Preview the exact command line before you launch anything

No accounts, no cloud, no telemetry. Runs entirely on your machine.

## Features

- **Instance Manager** -- start, stop, and monitor multiple llama-server processes
- **Profile Manager** -- save and switch between configs (model path, context size, GPU layers, etc.)
- **Fine-Tune Settings** -- tweak params with a live command preview, manage LoRA adapters
- **Benchmarks** -- run, compare, and export results
- **Logs Viewer** -- real-time log streaming via SignalR with search and filter
- **Settings** -- configure paths, polling intervals, and general preferences

## Installation

### Requirements

[llama-server](https://github.com/ggerganov/llama.cpp) needs to be on your `PATH`, or you can point LlamaCtrl to the full binary path in Settings.

### Quick install

**Linux / macOS:**

```bash
curl -fsSL https://raw.githubusercontent.com/Glenn332/llamactrl/main/install.sh | bash
```

**Windows (PowerShell):**

```powershell
irm https://raw.githubusercontent.com/Glenn332/llamactrl/main/install.ps1 | iex
```

Grabs the right binary for your OS and architecture and puts it on your `PATH`.

### Build from source

Needs **.NET 10 SDK** and **Node.js 18+**.

**Linux / macOS:**

```bash
git clone https://github.com/Glenn332/llamactrl.git
cd llamactrl
chmod +x build-and-install.sh
./build-and-install.sh
```

Installs to `/usr/local/bin/llamactrl`, or `~/.local/bin/llamactrl` if you don't have sudo.

**Windows (PowerShell):**

```powershell
git clone https://github.com/Glenn332/llamactrl.git
cd llamactrl
.\build.ps1
```

Output lands in `dist\win-x64\`. Run `install.ps1` instead if you want it added to your `PATH` automatically.

**Build only (Linux / macOS):**

```bash
./build.sh osx-arm64     # macOS Apple Silicon
./build.sh osx-x64       # macOS Intel
./build.sh linux-x64     # Linux x64
./build.sh linux-arm64   # Linux ARM64
```

## Usage

```bash
# Start with defaults (opens browser automatically)
llamactrl

# Custom port
llamactrl --port 8888

# Don't open the browser on startup
llamactrl --no-browser

# Custom data directory
llamactrl --data-dir /path/to/data

# Custom llama-server path
llamactrl --binary /usr/local/bin/llama-server

# Set models directory
llamactrl --models-dir ~/my-models

# Show version
llamactrl version

# Check for updates and install the latest release
llamactrl update
```

### CLI options

| Option | Default | Description |
|--------|---------|-------------|
| `--port` | `3131` | Port to listen on |
| `--data-dir` | See [Paths](#paths) | Directory for the database and config |
| `--models-dir` | See [Paths](#paths) | Directory scanned for `.gguf` models |
| `--binary` | `llama-server` | Path to the llama-server binary |
| `--no-browser` | `false` | Skip opening the browser on startup |
| `--log-level` | `Information` | Log verbosity: `Verbose` / `Debug` / `Information` / `Warning` / `Error` |

### Subcommands

| Command | Description |
|---------|-------------|
| `version` | Print the current version |
| `update` | Check GitHub for the latest release and update in place |

## Paths

LlamaCtrl uses platform-standard directories by default:

| | Linux / macOS | Windows |
|-|---------------|---------|
| **Data directory** | `~/.config/llamactrl` | `%APPDATA%\llamactrl` |
| **Models directory** | `~/models` | `%USERPROFILE%\models` |

The data directory holds:
- `llamactrl.db` -- SQLite database (profiles, instances, benchmarks)
- `llamactrl.log` -- application logs

Both can be overridden with `--data-dir` and `--models-dir`.

## Development

The backend and frontend run as separate processes in dev. The Vite dev server (port 5173) proxies `/api` and `/hubs` to the backend (port 3131), so open your browser at `http://localhost:5173`, not 3131.

**Requirements:** .NET 10 SDK, Node.js 18+, and `npm install` run once inside `src/frontend/`.

### Terminal workflow

```bash
# Terminal 1 — backend (auto-reloads on C# changes)
make dev
# or: cd src/LlamaCtrl && dotnet watch run

# Terminal 2 — frontend (HMR)
make frontend-dev
# or: cd src/frontend && npm run dev
```

Open **http://localhost:5173**.

### VS Code

A `.vscode/launch.json` is included with four configurations:

| Configuration | What it does |
|---|---|
| **Backend (.NET)** | Launches the .NET backend on port 3131 with the C# debugger attached |
| **Frontend (Vite)** | Starts Vite on port 5173 and opens the browser |
| **Frontend (Chrome debug)** | Attaches Chrome to the running Vite server for breakpoints in `.tsx` files |
| **Full Stack** | Compound -- runs both with a single F5 |

Requires the [C# Dev Kit](https://marketplace.visualstudio.com/items?itemName=ms-dotnettools.csdevkit) extension.

### JetBrains Rider

1. Open `src/LlamaCtrl/LlamaCtrl.csproj` in Rider.
2. Select the **Development** run configuration.
3. In a terminal: `cd src/frontend && npm install && npm run dev`
4. Open **http://localhost:5173**.

## Architecture

```
Browser (React SPA)
    | REST + SignalR WS
Kestrel HTTP :3131
    |
ASP.NET Core Controllers
    |
Service Layer (InstanceService, ProfileService, ...)
    |               |
ProcessManager    SQLite (EF Core)
(llama-server)    ~/.config/llamactrl/llamactrl.db
    |
SignalR Hubs -> Browser (live logs & metrics)
```

## License

MIT
