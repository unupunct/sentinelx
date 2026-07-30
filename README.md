# Sentinel X

[![Release](https://img.shields.io/github/v/release/unupunct/sentinelx)](https://github.com/unupunct/sentinelx/releases/latest)
[![License: MIT](https://img.shields.io/github/license/unupunct/sentinelx)](LICENSE)

A Windows observability platform for streamers/creators — OBS Studio and SplitCam diagnostics,
live system monitoring, Windows Event Log analysis, and internet speed testing, in one portable
dark-themed WPF app. No installer, no telemetry, no cloud dependency.

## What it does

- **Dashboard** — overall stability score plus per-subsystem health cards (OBS Studio, SplitCam,
  Streaming Validation), each with plain-English findings and recommended actions. Diagnose-only:
  it never writes to OBS's config or calls anything beyond read-only status checks.
- **Live Monitoring** — real-time CPU/RAM/GPU usage, network adapter throughput, watched-process
  stats (`obs64`/`obs32`/`SplitCam`), and OBS's live stream health (skipped frames, congestion,
  bitrate) via obs-websocket.
- **Event Viewer** — browses recent Warning/Error/Critical entries from the Windows System and
  Application logs, with plain-English explanations for a curated set of common event IDs
  (Kernel-Power 41, disk errors, service failures, etc.).
- **Speed Test** — a full download/upload/bufferbloat measurement against Cloudflare's speed test
  endpoints, with a live progress sparkline and a recommended streaming bitrate for the measured
  connection.

## Building from source

```bash
dotnet build SentinelX.sln
dotnet test SentinelX.sln
```

Requires the .NET 8 SDK and Windows (WPF). Publish a self-contained single-file exe with:

```powershell
./publish.ps1
```

Headless/scriptable modes (no window shown): `Sentinel X.exe --scan-json <path>` (runs all
engines, dumps JSON), `--obs-audit-dump [path]` (OBS config/log only), `--soak [seconds] [kbps]`
(network stability soak test).

## Project status

This is phase 1 of a larger observability vision — deliberately scoped to the modules most
useful for a streaming/studio setup rather than general Windows hardware monitoring. Not yet
built: USB diagnostics, driver intelligence, ETW recording, minidump analysis, an AI root-cause
correlation engine, a settings UI (thresholds are currently hardcoded constants), and hardware
engines (CPU/GPU/storage) beyond the live tiles already on the Live Monitoring tab.

SplitCam support is intentionally limited (process/install/virtual-camera detection only) since
no documented SplitCam config file format was available to verify deeper parsing against.

## License

[MIT](LICENSE)
