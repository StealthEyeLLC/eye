# HARDWARE.md

**Status:** Live-observed hardware/runtime snapshot  
**Observed:** 2026-08-07  
**Machine:** `STEALTHEYELLC`

This file records what Eye observed directly from the laptop. Hardware and volume facts may change after upgrades, repartitioning, driver changes, or device replacement.

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
- Physical DPI-aware desktop bounds: **1920 x 1200**
- Primary interactive Windows profile after cutover: `C:\Users\StealthEye`

## Internal storage

Physical disk:

- **SAMSUNG MZVL81T0HFLB-00BH1**
- NVMe SSD
- ~1 TB raw capacity
- GPT
- Health observed: Healthy / OK

Current physical volume layout after cutover:

- `C:` — NTFS, label `Windows`, filesystem size ~652.7 GiB
- `X:` — ReFS Dev Drive, label `Eye Dev`, exactly 300 GiB

`X:` is a real partition on the Samsung NVMe and reports as a trusted developer volume.

The former ~400 GB `C:\Sovereign Node.vhdx` fixed virtual disk was deleted after its important contents and Git history were independently archived.

The temporary `C:\StealthEye-Dev.vhdx` fallback was also deleted after the physical Dev Drive succeeded.

Current pagefile is on `C:`; the former ~192 GB `X:` pagefile is gone.

## External storage

Physical external disk:

- **Realtek RTL9210 NVME** bridge/device
- USB-attached SSD/NVMe
- ~2 TB raw capacity
- Health observed: Healthy / OK

Current volume:

- `E:` — exFAT, label `StealthEye`
- used for bulk data, archives, models, artifacts and related large payloads

## Network

### Wi-Fi

- **MediaTek Wi-Fi 6E MT7922 (RZ616) 160MHz PCIe Adapter**

### Ethernet

- **Realtek Gaming GbE Family Controller**

## Battery

Battery full-charged capacity telemetry observed around **63.4 Wh**.

Battery/AC firmware telemetry has shown internally inconsistent charge/discharge state, so treat it as operational telemetry rather than a precise health benchmark until independently validated.

## WSL

Fresh WSL environment under the StealthEye Windows account:

- Distribution: **Ubuntu 24.04.4 LTS**
- Kernel: **6.6.87.2-microsoft-standard-WSL2**
- Default Linux identity: **root (UID 0)**
- systemd: **running**

## Current Eye runtime

Live prototype runtime observed:

- Eye version: `0.5.1`
- Process identity: `NT AUTHORITY\SYSTEM`
- Mode: Windows service
- Framework: **.NET 10.0.5**
- Loopback MCP endpoint: `http://127.0.0.1:37921/mcp`

This runtime is the prototype used to inspect and prepare the machine. It is not the final `StealthEyeLLC/eye` implementation.

## Machine-wide development tooling

Known machine-wide tools:

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

## Current storage roles

```text
C:  Windows / system / installed applications
X:  physical trusted ReFS Dev Drive / repositories / build workspace
E:  bulk StealthEye data / models / archives / large artifacts
WSL Linux filesystem: Linux-native workloads requiring Unix permission semantics
```

Permanent Eye repository location:

```text
X:\Repos\eye
```
