# EYE_PLATFORM.md

**Status:** Current laptop/platform source snapshot  
**Baseline date:** 2026-08-07  
**Machine:** `STEALTHEYELLC`

This document records live platform state. It is a snapshot, not a promise that every value will remain permanent.

## 1. Current Eye prototype

Installed prototype:

- product: StealthEye
- executable/tool: `eye`
- version observed: `0.5.1`
- service mode: Windows service
- service identity: LocalSystem
- loopback MCP endpoint: `http://127.0.0.1:37921/mcp`
- installed config: `C:\ProgramData\StealthEye\config.json`

The prototype is useful evidence and the current control path, but it is **not** automatically the v2 implementation design.

## 2. Direct OpenAI path

Working path:

```text
ChatGPT
  -> OpenAI Secure MCP Tunnel
  -> tunnel-client.exe on STEALTHEYELLC
  -> 127.0.0.1:37921/mcp
  -> eye.exe
```

HEC/VPS is no longer part of the laptop path. Its residual laptop task, folder, dedicated SSH key pair and handshake file have now been removed.

Tailscale was also proven unnecessary to this request path by stopping it and immediately re-verifying direct Eye access. Its service is now stopped and disabled; the package remains installed temporarily for easy reversal.

## 3. Old repository preservation

Before destructive storage work, the old `X:` payload and old `se` repository were preserved under:

```text
E:\StealthEye\archives\pre-platform-rebuild-20260807
```

Complete Git-history bundle:

```text
E:\StealthEye\archives\pre-platform-rebuild-20260807\se-full.bundle
```

The bundle was verified to contain the old repository history including local-only commits. The archive and bundle were reconfirmed readable immediately before deleting the old fixed VHDX.

## 4. Windows interactive identity — cutover complete

Primary interactive account is now:

```text
User: StealthEye
Role: local administrator
Profile: C:\Users\StealthEye
```

Live Eye session verification after reboot reported `StealthEye` as the interactive user.

Automatic console sign-in is configured with Microsoft Sysinternals Autologon. A subsequent restart was physically observed to go directly to the `StealthEye` desktop without interactive credential entry.

Do not record the local account password in source or repository files.

The old `steal` account/profile still exists pending a final iCloud retention decision.

The new profile's Git author identity is:

```text
StealthEye <stealtheye.eye@gmail.com>
```

The migrated GitHub SSH identity successfully clones/pulls the new repository. A live push was rejected by GitHub as a deploy key, so laptop-side Git write authority is not yet established.

## 5. Power and availability

Configured direction remains:

- hibernation off;
- no automatic sleep;
- no automatic display shutdown;
- no automatic disk shutdown;
- lid close = do nothing;
- screensaver disabled;
- password-on-wake disabled where applicable;
- Dynamic Lock disabled for the StealthEye profile;
- machine intended to remain continuously available.

Windows Update policy is configured to avoid automatic reboot while a user is logged on.

## 6. Pagefile — transition complete

After reboot, the old ~192 GB `X:\pagefile.sys` disappeared and only the Windows pagefile remained.

Current pagefile file:

```text
C:\pagefile.sys
Current allocation observed: 16 GiB
Configured initial: 16 GiB
Configured maximum: 32 GiB
```

## 7. Internal storage — final X established

Physical internal disk:

- `SAMSUNG MZVL81T0HFLB-00BH1`
- NVMe
- ~1 TB raw capacity
- GPT

The old fixed VHDX `C:\Sovereign Node.vhdx` was deleted after archive verification and pagefile cutover. This returned roughly 400 GB of physical `C:` free space.

Post-delete `Get-PartitionSupportedSize` showed enough supported shrink capacity for a 300 GiB physical development volume.

Final internal layout:

```text
C:  NTFS  Windows   ~652.7 GiB filesystem
X:  ReFS  Eye Dev    300.0 GiB
```

`X:` is a **physical partition on the Samsung NVMe**, formatted using Windows Dev Drive semantics. `fsutil devdrv query X:` reports it as a trusted developer volume.

The staged fallback `C:\StealthEye-Dev.vhdx` was deleted after the physical Dev Drive was proven.

## 8. Development repository

Permanent local repository:

```text
X:\Repos\eye
```

It was cloned from `StealthEyeLLC/eye` over SSH under the StealthEye account.

Clone/pull works. Current SSH authentication does not permit push; repository writes are still available through the connected GitHub control path while steady-state laptop Git authority remains an open decision.

## 9. External bulk-data drive

`E:` remains:

- label: `StealthEye`
- filesystem: exFAT
- ~2 TB raw device

Primary layout:

```text
E:\StealthEye\
  archives\
  artifacts\
  cache\
  checkpoints\
  datasets\
  media\
  models\
```

Protected archive area:

```text
E:\ARCHIVE - do not touch
```

LM Studio model payload: `E:\StealthEye\models\lmstudio`.

Ollama model location: `E:\StealthEye\models\ollama`.

Possible future cleanup: after protected data has another safe copy, consider whether `E:` should remain exFAT or be reformatted NTFS. This remains optional.

## 10. Docker

Docker Desktop, Docker WSL state and old Docker build/container data were removed.

The target Eye runtime does not depend on Docker.

## 11. WSL — fresh StealthEye distro established

A fresh distribution is installed under the StealthEye Windows account:

```text
Distribution: Ubuntu-24.04
Observed release: Ubuntu 24.04.4 LTS
WSL kernel: 6.6.87.2-microsoft-standard-WSL2
Default Linux user: root
systemd: enabled and running
```

A normal `wsl -d Ubuntu-24.04` invocation was verified to run as UID 0 without another credential prompt.

The old Ubuntu 22.04 registration under the old `steal` Windows account remains transitional and should be removed when that old profile is retired.

Linux-native permission-sensitive workloads should use the WSL Linux filesystem rather than relying on ReFS-hosted Unix metadata behavior.

## 12. Machine-wide developer tooling

Known machine-wide tools include Git, GitHub CLI, .NET, Node/npm, CUDA, CMake, Ninja, FFmpeg, VS Code, `uv`/`uvx`, and ripgrep.

Windows Developer Mode and long-path support are enabled.

User-local runtimes should be added deliberately rather than copied wholesale from the old profile.

## 13. Background / transitional cleanup

Completed:

- Docker Desktop removed;
- HEC laptop task/files/dedicated key removed;
- Tailscale stopped and disabled after direct-path independence test;
- old `steal`-bound `StealthEye Session` task removed;
- Razer/HP analytics and assorted old-profile startup entries previously cleaned.

Still present intentionally:

- `StealthEye Session - New Account` is running under `StealthEye` because the current prototype still uses its session helper. Remove it only when clean v2 native active-user execution replaces it.
- Tailscale package remains installed but disabled for easy reversal.
- OMEN hardware support remains.

## 14. Live architecture experiments that passed

Disposable experiments established:

1. `CreateProcessAsUser` can launch the active user from a real LocalSystem SCM service.
2. stdout/stderr capture works through inherited pipes.
3. user processes can be placed in service-owned Job Objects.
4. WSL can be invoked through active-user execution.
5. short-lived desktop workers can be launched into the interactive session.
6. Per-Monitor V2 workers see the physical 1920x1200 display coordinates.
7. installed Chrome can be launched as the user and controlled from SYSTEM over loopback CDP.
8. native ConPTY works cross-session.
9. `ReleasePseudoConsole` is exported on this laptop's current Windows build.

These are evidence behind the favored v2 no-permanent-session-helper architecture.

## 15. Old-profile inventory

`C:\Users\steal` was inventoried read-only.

Small user-facing areas are essentially old development/prototype material:

- Desktop: 2 files, ~4.5 KB;
- Documents: 24 files, ~1.9 MB;
- Downloads: 9 files, ~28 KB;
- OneDrive/Videos/Music: effectively empty.

The only material requiring a deliberate retirement choice is iCloud:

- `iCloudDrive`: 15,766 files, ~2.78 GB logical, all observed as reparse-backed;
- `iCloudPhotos`: 472 files, ~3.02 GB logical, 463 marked offline and 470 reparse-backed.

No copy/delete/hydration was performed. Blindly copying cloud-backed placeholders is intentionally avoided.

## 16. Remaining platform cleanup

Core account/storage/WSL/repository/HEC cutover is complete. Remaining work is narrow:

1. Decide whether the old iCloud material needs a local hydrated archive or whether the cloud copy is sufficient.
2. Retire the old `steal` account/profile and its old WSL registration after that choice.
3. Keep the prototype `StealthEye Session - New Account` helper only until v2 native active-user execution replaces it.
4. Optionally uninstall disabled Tailscale if no unrelated use appears.
5. Decide whether any user-local Python/Ollama/LM Studio installation is actually required.
6. Re-test the favored DPAPI-NG local credential-store primitive across a future reboot before making it canonical.
7. Switch the ChatGPT Gmail connector to `stealtheye.eye@gmail.com` before treating Eye Gmail authority as live.
8. Freeze the small v2 architecture and begin clean implementation in `X:\Repos\eye`.
