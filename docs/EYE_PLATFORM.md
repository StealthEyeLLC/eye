# EYE_PLATFORM.md

**Status:** Canonical target platform roles plus previously validated machine evidence  
**Baseline date:** 2026-08-07  
**Machine:** `STEALTHEYELLC`

This document separates **target platform shape** from values that must be reverified on the live machine before deployment. Historical experiments remain evidence; they are not a promise that every package/configuration value is currently present.

## 1. Hardware baseline

Dedicated HP OMEN 16-ap0xxx class laptop.

Previously observed hardware included:

- AMD Ryzen 9 8940HX, 16 cores / 32 threads;
- 32 GB RAM;
- NVIDIA RTX 5060 Laptop GPU, 8 GB nominal VRAM;
- AMD integrated graphics;
- internal Samsung ~1 TB NVMe;
- 1920x1200 internal display;
- MediaTek Wi-Fi 6E and built-in Realtek Ethernet.

See `HARDWARE.md` for the detailed observed snapshot. Re-query hardware/driver/firmware versions rather than assuming old point-in-time versions remain current.

## 2. Target request path

```text
ChatGPT
  -> OpenAI Secure MCP Tunnel
  -> tunnel-client.exe on STEALTHEYELLC
  -> loopback MCP
  -> eye.exe LocalSystem Windows service
```

HEC/VPS, Docker, Tailscale and SSH are not required parts of the steady-state Eye request path.

The tunnel remains external transport only.

## 3. Target storage roles

Canonical layout:

```text
C:  Windows / system / installed applications
X:  physical ReFS Dev Drive / repos / build workspace
E:  bulk StealthEye data / models / archives / large artifacts
WSL Linux filesystem: Linux-native permission-sensitive work
```

Target development volume:

```text
X: approximately 300 GiB, physical internal-NVMe ReFS Dev Drive, trusted
```

Do not use a VHD/VHDX as the normal development volume unless a concrete requirement changes the decision.

The external `E:` drive contains bulk data/archive roles and includes protected archive material. Treat destructive formatting/partition operations on `E:` as out of scope unless explicitly requested.

## 4. Target repository location

```text
X:\Repos\eye
```

Repository:

```text
StealthEyeLLC/eye
```

The local checkout is the normal build/workspace location once `X:` is provisioned.

Machine-side GitHub write authority is a separate decision from the ChatGPT GitHub control path. Match any future machine credential to the intended authority.

## 5. Windows interactive identity boundary

The intended dedicated interactive account name is:

```text
StealthEye
```

Do not record its password or other recovery material in source.

Login/account/autologon architecture is **not a current Eye implementation target**. Leave the active login/account arrangement alone unless the owner explicitly requests a change.

Eye should query the real active-session state and launch session-bound work through native service-owned APIs.

## 6. WSL target

Target baseline:

```text
Distribution: Ubuntu-24.04
Release family: Ubuntu 24.04 LTS
WSL version: WSL2
systemd: enabled
Default Linux user: root
```

WSL is launched through active-user execution from the Eye service rather than through a permanent user daemon.

Linux-native permission-sensitive workloads should live in the distro filesystem rather than relying on ReFS metadata semantics.

## 7. Developer/tooling baseline

Install or verify only the useful baseline, adding other tools on demand:

- Git;
- GitHub CLI;
- current .NET SDK required by Eye;
- PowerShell;
- Node/npm only for tasks that require them, not a permanent Eye service;
- CMake;
- Ninja;
- FFmpeg;
- VS Code;
- `uv` / `uvx`;
- ripgrep;
- NVIDIA/CUDA stack appropriate to the installed GPU;
- WSL.

Windows Developer Mode and long-path support are useful platform settings where still applicable.

Do not rebuild broad old-profile package inventories merely because they existed historically.

## 8. Power/availability target

The dedicated machine is intended to remain available for unattended Eye operation where practical.

Desired posture includes avoiding automatic sleep/hibernation/lid-triggered shutdown that would unnecessarily remove the machine from service.

Apply power configuration deliberately and verify it on the live installation rather than relying on old state.

## 9. Prototype/runtime boundary

The repository already contains an early v2 service/process implementation.

Do not reproduce the historical prototype topology as the desired final state.

Final runtime target:

```text
Windows SCM
  -> eye.exe LocalSystem service
       -> native SYSTEM operations
       -> active-user process launch on demand
       -> short-lived desktop workers on demand
       -> WSL execution on demand
       -> installed Chrome / loopback CDP
       -> focused external engines on demand
```

No permanent user-session helper is part of the target.

## 10. Public MCP boundary

The canonical public surface is now five effect-class facades:

```text
eye_inspect
eye_run
eye_change
eye_interact
eye_external
```

The old single model-facing `eye({ op, args })` design is no longer canonical.

All facades route to one internal operation registry/dispatcher. The split is metadata/schema/effect organization, not a privilege hierarchy.

See `MCP_CONTRACT.md`.

## 11. Previously validated architecture experiments

The following experiments were previously demonstrated on `STEALTHEYELLC` and remain useful evidence to reproduce/verify during v2 implementation:

1. `CreateProcessAsUser` from a genuine LocalSystem SCM service can launch the active interactive user.
2. stdout/stderr capture can work through inherited pipes.
3. user processes can be placed in service-owned Job Objects.
4. WSL can be invoked through the active-user execution path.
5. short-lived desktop workers can be launched into the interactive session.
6. Per-Monitor V2 workers can operate in physical display coordinates.
7. installed Chrome can be launched as the user and controlled from SYSTEM over loopback CDP with a dedicated profile.
8. native cross-session ConPTY works.
9. `ReleasePseudoConsole` was exported on the tested Windows build.
10. DPAPI-NG `LOCAL=user`, called as LocalSystem, protected a throwaway blob across reboot while the interactive user could not decrypt it.

These are implementation evidence, not reasons to preserve historical prototype code.

## 12. Machine secret target

Local credentials that `eye.exe` must retain should use the previously validated DPAPI-NG design:

```text
LocalSystem -> DPAPI-NG descriptor LOCAL=user -> encrypted blob under SYSTEM-owned machine storage
```

Persist only encrypted blobs and non-secret metadata.

Never commit secret values, passwords, private keys, BitLocker/recovery material, or plaintext provider credentials.

## 13. External identity

Operational Eye Google identity:

```text
StealthEye <stealtheye.eye@gmail.com>
```

Separate mailbox:

```text
stealtheye@stealtheye.io
```

Keep those identities distinct unless the owner explicitly changes the arrangement.

## 14. Platform verification gate before final cutover

Before declaring a new Eye runtime production-ready, verify the live platform rather than assuming this document's historical observations:

- Windows and current drivers/firmware are stable;
- storage roles exist as intended;
- `X:` is a trusted ReFS Dev Drive;
- WSL baseline is healthy;
- required developer tools are installed;
- Secure MCP Tunnel reaches loopback Eye;
- Eye service survives reboot;
- active-user execution works;
- terminal/WSL works;
- desktop worker works;
- browser/CDP works;
- secret persistence works;
- external `E:` data remains isolated from destructive provisioning steps.

Only then remove transitional/prototype runtime pieces.
