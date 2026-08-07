# HARDWARE.md

**Status:** Live-observed hardware/runtime snapshot  
**Observed:** 2026-08-07  
**Machine:** `STEALTHEYELLC`

This file records what Eye observed directly from the laptop. It is intentionally separate from architecture decisions: hardware facts can change after upgrades, driver changes, repartitioning, or replacement devices.

## System

- Manufacturer: **HP**
- Model: **OMEN Gaming Laptop 16-ap0xxx**
- Architecture: x64
- Windows: **Windows 11 Home**
- Windows version/build observed: **10.0.26200 / build 26200**
- BIOS: AMI **F.13**

## CPU

- **AMD Ryzen 9 8940HX with Radeon Graphics**
- 16 physical cores
- 32 logical processors

## Memory

- Total installed: **32 GB**
- 2 x 16 GB Samsung modules
- Module part number observed: `M425R2GA3EB0-CWMOD`
- Rated speed reported: 5600 MT/s
- Configured clock reported: 5200 MT/s

## Graphics

### Discrete GPU

- **NVIDIA GeForce RTX 5060 Laptop GPU**
- NVIDIA memory reported by `nvidia-smi`: **8151 MiB**
- NVIDIA driver observed: **592.82**

### Integrated GPU

- **AMD Radeon(TM) 610M**

## Display / interactive session

- One active monitor observed
- Physical desktop bounds observed with DPI-aware session tooling: **1920 x 1200**

The interactive session may be locked while machine/service operations remain available.

## Internal storage

Physical internal disk:

- **SAMSUNG MZVL81T0HFLB-00BH1**
- NVMe SSD
- ~1 TB raw capacity
- Health observed: Healthy / OK

Current Windows volume snapshot at observation time:

- `C:` — NTFS, label `Windows`, ~1.02 TB volume size, ~303 GB free
- `X:` — ReFS, label `Sovereign Node`, ~400 GB virtual disk, transitional

The current `X:` is backed by the old fixed VHDX and is not the intended final storage topology.

## External storage

Physical external disk:

- **Realtek RTL9210 NVME** bridge/device
- USB-attached SSD/NVMe
- ~2 TB raw capacity
- Health observed: Healthy / OK

Current volume:

- `E:` — exFAT, label `StealthEye`, ~2.05 TB volume size, ~1.81 TB free at observation time

## Network

### Wi-Fi

- **MediaTek Wi-Fi 6E MT7922 (RZ616) 160MHz PCIe Adapter**
- Status at observation: Up

### Ethernet

- **Realtek Gaming GbE Family Controller**
- Status at observation: Disconnected

## Battery

Battery full-charged capacity telemetry observed around **63.4 Wh**.

Battery/AC firmware telemetry has previously shown internally inconsistent charge/discharge state, so battery status should be treated as operational telemetry rather than a precise hardware-health benchmark until independently validated.

## Current Eye runtime

Live prototype runtime observed:

- Eye version: `0.5.1`
- Process identity: `NT AUTHORITY\SYSTEM`
- Mode: Windows service
- Framework: **.NET 10.0.5**
- Loopback MCP endpoint: `http://127.0.0.1:37921/mcp`

This runtime is the prototype used to inspect and prepare the machine. It is not the final `StealthEyeLLC/eye` implementation.

## Machine-wide development tooling already established

The platform preparation has made these tools available machine-wide for the future `StealthEye` account:

- Git
- GitHub CLI
- .NET
- Node / npm
- CUDA tooling
- CMake
- Ninja
- FFmpeg
- VS Code
- `uv` / `uvx`
- ripgrep

Windows Developer Mode and long-path support are enabled.

## Storage target

Favored final layout after the pending local cutover:

- `C:` — Windows/system/apps
- `X:` — physical ReFS Dev Drive partition on the internal Samsung NVMe, size chosen after final shrink measurement
- `E:` — bulk StealthEye data/models/archives

A tested dynamic ReFS Dev Drive VHDX exists only as a fallback if a physical `X:` partition is not desirable.
