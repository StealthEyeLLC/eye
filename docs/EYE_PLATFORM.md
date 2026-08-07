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

The old HEC/VPS reverse tunnel is stopped and disabled. Direct Eye calls continued working without it.

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

Do not record the local account password in source, chat-derived project state, or repository files.

The old `steal` account/profile still exists pending review of user-facing data. Do not delete it blindly.

The new profile's Git author identity is:

```text
StealthEye <stealtheye.eye@gmail.com>
```

The migrated GitHub SSH identity successfully cloned the new repository over SSH.

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

The old fixed VHDX:

```text
C:\Sovereign Node.vhdx
```

was deleted after archive verification and pagefile cutover. This returned roughly 400 GB of physical `C:` free space.

Post-delete `Get-PartitionSupportedSize` showed enough supported shrink capacity for a 300 GiB physical development volume.

Final internal layout now includes:

```text
C:  NTFS  Windows   ~652.7 GiB filesystem
X:  ReFS  Eye Dev    300.0 GiB
```

`X:` is a **physical partition on the Samsung NVMe**, formatted using Windows Dev Drive semantics (`Format-Volume -DevDrive`). `fsutil devdrv query X:` reports it as a trusted developer volume.

The staged fallback `C:\StealthEye-Dev.vhdx` was deleted after the physical Dev Drive was proven.

## 8. Development repository

Permanent local repository path now exists:

```text
X:\Repos\eye
```

It was cloned from:

```text
StealthEyeLLC/eye
```

using the StealthEye account's GitHub SSH key.

The local checkout was clean and synchronized with `origin/main` immediately after clone.

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

LM Studio model payload:

```text
E:\StealthEye\models\lmstudio
```

Ollama model location:

```text
E:\StealthEye\models\ollama
```

Possible future cleanup: after protected data has another safe copy, consider whether `E:` should remain exFAT or be reformatted NTFS. This remains optional.

## 10. Docker

Docker Desktop, Docker WSL state and old Docker build/container data were removed.

The target Eye runtime does not depend on Docker.

## 11. WSL — fresh StealthEye distro established

The `StealthEye` Windows account initially had no registered WSL distributions.

A fresh distribution is now installed under that account:

```text
Distribution: Ubuntu-24.04
Observed release: Ubuntu 24.04.4 LTS
WSL kernel: 6.6.87.2-microsoft-standard-WSL2
Default Linux user: root
systemd: enabled and running
```

A normal `wsl -d Ubuntu-24.04` invocation was verified to run as UID 0 without another credential prompt.

The old Ubuntu 22.04 registration under the old `steal` Windows account remains transitional and should be removed when that old profile is retired.

Do not place Linux-native permission-sensitive workloads on the ReFS Dev Drive when they require WSL Unix metadata semantics; use the WSL Linux filesystem for those workloads.

## 12. Machine-wide developer tooling

Known machine-wide tools include:

- Git
- GitHub CLI
- .NET
- Node/npm
- CUDA
- CMake
- Ninja
- FFmpeg
- VS Code
- `uv` / `uvx`
- ripgrep

Windows Developer Mode and long-path support are enabled.

User-local runtimes should be added deliberately rather than copied wholesale from the old profile.

## 13. Background-app cleanup

Removed or disabled legacy/nonessential background infrastructure includes:

- Docker Desktop;
- HEC tunnel runtime;
- Razer background services where not needed;
- HP analytics/support background components;
- assorted old-profile startup entries.

OMEN hardware support remains.

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

## 15. Lock behavior

Windows secure desktop remains a real boundary. When locked, ordinary desktop capture may be unavailable/black while service, CLI, file, WSL and non-secure-desktop operations continue where Windows permits them.

Eye should expose real lock state rather than pretending the interactive desktop is available.

## 16. Remaining platform cleanup

Core account/storage/WSL cutover is complete. Remaining platform work is narrower:

1. Review `C:\Users\steal` for Documents, Downloads, iCloudDrive, iCloudPhotos or other user-facing data worth retaining.
2. Preserve anything deliberately kept.
3. Retire the old `steal` account/profile and its old WSL registration only after that review.
4. Remove inert HEC task/files/key residue.
5. Keep the prototype `StealthEye Session - New Account` helper only while the current prototype needs it; remove it when the v2 LocalSystem service provides native active-user execution.
6. Decide whether any user-local Python/Ollama/LM Studio installation is actually required.
7. Re-test the favored DPAPI-NG local credential-store primitive across a future reboot before making it canonical.
8. Then freeze the small v2 architecture and begin clean implementation in `X:\Repos\eye`.
