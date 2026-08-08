# HARDWARE.md

**Status:** Physical hardware baseline plus currently verified platform facts  
**Baseline date:** 2026-08-07  
**Machine:** `STEALTHEYELLC`

This file distinguishes stable physical hardware identity from dynamic software/driver/volume state. Re-query firmware, drivers, Windows build, installed tools, and provisioned volumes when exact current values matter.

## System identity

- Manufacturer: **HP**
- Model family: **OMEN Gaming Laptop 16-ap0xxx**
- Architecture: **x64**
- Windows edition: **Windows 11 Home**
- Machine name: **`STEALTHEYELLC`**
- Interactive profile target/current identity: **`C:\Users\StealthEye`**
- BIOS previously observed: AMI **F.13**; firmware version is dynamic and should be re-queried before firmware-sensitive work.

## CPU

- **AMD Ryzen 9 8940HX with Radeon Graphics**
- 16 physical cores
- 32 logical processors

## Memory

- Total installed: **32 GB**
- 2 x 16 GB Samsung modules
- Module part number previously observed: `M425R2GA3EB0-CWMOD`
- Rated memory speed previously reported: 5600 MT/s
- Configured clock previously reported: 5200 MT/s

## Graphics

### Discrete GPU

- **NVIDIA GeForce RTX 5060 Laptop GPU**
- VRAM previously reported by `nvidia-smi`: **8151 MiB** (~8 GB)

NVIDIA driver versions are dynamic and are deliberately not treated as permanent project identity.

### Integrated GPU

- **AMD Radeon(TM) 610M**

## Display

Previously observed interactive display baseline:

- one active internal display;
- physical DPI-aware desktop bounds **1920 x 1200**.

Exact monitor topology is runtime state and must be queried by Eye rather than assumed.

## Internal storage device

Physical internal SSD previously observed:

- **SAMSUNG MZVL81T0HFLB-00BH1**
- NVMe
- approximately 1 TB raw capacity
- GPT

Canonical storage role target:

```text
C: Windows / applications / stable host state / encrypted secrets / engine metadata
X: approximately 300 GiB physical trusted ReFS Dev Drive / repos / hot workspaces / job spool / temporary artifacts / ReFS clones
```

The exact live partition layout is dynamic and must be queried before any partitioning or destructive operation.

## External storage device

External device previously observed:

- **Realtek RTL9210 NVME** USB bridge/device
- approximately 2 TB raw capacity
- normal drive role: **`E:`**

Canonical role:

```text
E: models / media / archives / large downloads / cold and durable bulk artifacts
```

`E:` contains important bulk/archive data and must not be included in destructive provisioning operations unless the owner explicitly requests it.

## Network hardware

### Wi-Fi

- **MediaTek Wi-Fi 6E MT7922 (RZ616) 160MHz PCIe Adapter**

### Ethernet

- **Realtek Gaming GbE Family Controller**
- built-in RJ-45 Ethernet is available.

## Battery

Battery full-charged-capacity telemetry was previously observed around **63.4 Wh**.

Battery/AC telemetry can be dynamic and internally inconsistent; Eye should query current power state rather than treat that number as a permanent health benchmark.

## Current device-encryption posture

Current configured Windows OS-volume posture was explicitly verified after the present Windows setup:

```text
C: Fully Decrypted
BitLocker Version: None
Percentage Encrypted: 0.0%
Encryption Method: None
Protection Status: Off
Key Protectors: None
```

Automatic Windows device encryption is currently disabled through:

```text
HKLM\SYSTEM\CurrentControlSet\Control\BitLocker
PreventDeviceEncryption = 1 (REG_DWORD)
```

This is a current configured posture, not a substitute for querying `manage-bde -status` before encryption-sensitive work.

## WSL target

Canonical Linux target after provisioning:

```text
Distribution: Ubuntu-24.04
Release family: Ubuntu 24.04 LTS
WSL version: WSL2
systemd: enabled
Default Linux user: root
```

A prior installation demonstrated Ubuntu 24.04.4 LTS on WSL2 with kernel `6.6.87.2-microsoft-standard-WSL2`; exact future distro/kernel versions are dynamic.

## Developer-tool target

Useful baseline to verify/install on the current Windows installation:

- Git;
- GitHub CLI;
- .NET SDK required by Eye;
- PowerShell;
- Node/npm only where tasks require it;
- CMake;
- Ninja;
- FFmpeg;
- VS Code;
- `uv` / `uvx`;
- ripgrep;
- NVIDIA/CUDA tooling appropriate to the installed GPU;
- WSL.

Do not treat the previously installed package set as proof that these are currently present after any OS reprovisioning; Eye should expose live software manifests.

## Eye runtime target

The final Eye runtime is not identified by any historical prototype version.

Canonical runtime identity:

```text
Windows SCM
  -> one LocalSystem StealthEye service
       -> stable eye.exe host
       -> supervised versioned capability-engine child process
       -> on-demand active-session workers
```

Canonical repository:

```text
StealthEyeLLC/eye
```

Canonical local checkout target:

```text
X:\Repos\eye
```
