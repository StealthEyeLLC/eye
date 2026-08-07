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

HEC/VPS is no longer part of the laptop path. Its laptop task/files/dedicated key/handshake residue has been removed, and a targeted final check found no HEC-specific service, task, or standard installation path remaining.

Tailscale was also proven unnecessary to this request path by stopping it and immediately re-verifying direct Eye access. Its service is stopped and disabled; the package remains installed temporarily for easy reversal.

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

## 4. Windows interactive identity — complete

Primary interactive account:

```text
User: StealthEye
Role: local administrator
Profile: C:\Users\StealthEye
```

Automatic console sign-in is configured with Microsoft Sysinternals Autologon. Multiple subsequent restarts returned directly to the `StealthEye` desktop, including the final cleanup/DPAPI validation reboot.

Do not record the local account password in source or repository files.

The previous local account `steal` has now been removed. Its Win32 user-profile registration and residual `C:\Users\steal` directory are gone.

Before retirement, deliberately retained local material was copied to:

```text
E:\StealthEye\archives\pre-profile-retirement-20260807
```

Preserved there:

- Desktop;
- Documents;
- Downloads;
- a top-level old-profile inventory;
- seven locally resident iCloud Photos media files (~44 MiB).

Cloud-only iCloud placeholders were not deliberately hydrated merely for retirement.

The active profile's Git author identity is:

```text
StealthEye <stealtheye.eye@gmail.com>
```

The migrated GitHub SSH identity clones/pulls the new repository. A live push was rejected by GitHub as a deploy key, so laptop-side Git write authority remains a separate later choice.

## 5. Power and availability

Configured direction:

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

Current pagefile:

```text
C:\pagefile.sys
Current allocation observed: 16 GiB
Configured initial: 16 GiB
Configured maximum: 32 GiB
```

The former ~192 GiB `X:\pagefile.sys` is gone.

## 7. Internal storage — final X established

Physical internal disk:

- `SAMSUNG MZVL81T0HFLB-00BH1`
- NVMe
- ~1 TB raw capacity
- GPT

The old fixed VHDX `C:\Sovereign Node.vhdx` was deleted after archive verification and pagefile cutover.

Final internal layout:

```text
C:  NTFS  Windows   ~652.7 GiB filesystem
X:  ReFS  Eye Dev    300.0 GiB
```

`X:` is a physical partition on the Samsung NVMe, formatted using Windows Dev Drive semantics. `fsutil devdrv query X:` reports it as a trusted developer volume.

The staged fallback `C:\StealthEye-Dev.vhdx` was deleted after the physical Dev Drive was proven.

## 8. Development repository

Permanent local repository:

```text
X:\Repos\eye
```

It was cloned from `StealthEyeLLC/eye` over SSH under the StealthEye account.

Clone/pull works. Current SSH authentication does not permit push; repository writes remain available through the connected GitHub control path while steady-state laptop Git authority remains an open later decision.

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

## 11. WSL — clean StealthEye baseline

The current Windows account has one registered distro:

```text
Distribution: Ubuntu-24.04
Observed release: Ubuntu 24.04.4 LTS
WSL kernel: 6.6.87.2-microsoft-standard-WSL2
Default Linux user: root
systemd: enabled and running
```

A normal `wsl -d Ubuntu-24.04` invocation runs as UID 0 without another credential prompt.

The former Ubuntu 22.04 registration belonged to the retired `steal` profile and is no longer part of the active Windows user state.

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
- old `steal` account/profile retired;
- old `steal`-bound `StealthEye Session` task removed;
- Razer/HP analytics and assorted old-profile startup entries previously cleaned.

Still present intentionally:

- `StealthEye Session - New Account` remains because the current prototype still uses its session helper. Remove it only when clean v2 native active-user execution replaces it.
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
10. DPAPI-NG `LOCAL=user`, called as LocalSystem, can persist an encrypted blob across reboot and decrypt it afterward while the interactive StealthEye user cannot decrypt the same blob.

These experiments are the evidence behind the frozen small v2 architecture.

## 15. Machine secret persistence — reboot validation complete

A throwaway 32-byte random value was protected by a LocalSystem process with DPAPI-NG descriptor:

```text
LOCAL=user
```

Only the encrypted blob and a verification hash were persisted under `C:\ProgramData\StealthEye` for the test.

The machine was rebooted. After the Eye service and tunnel returned:

- LocalSystem successfully decrypted the persisted blob and reproduced the expected hash;
- a process running as interactive `STEALTHEYELLC\StealthEye` could not decrypt it (`NCryptUnprotectSecret` failed);
- the throwaway probe executable, source, blob and hash were then removed.

No real Eye credential was involved.

## 16. Google connector identity

The ChatGPT connector set is now pointed at the Eye Google identity:

```text
stealtheye.eye@gmail.com
```

Verified live for Gmail by reading mail addressed to that account. Drive, Calendar and Contacts were already connected under the same Eye identity.

## 17. Platform state after cleanup

The blocking laptop cutover is complete:

- StealthEye is the only intended interactive account;
- autologon survives reboot;
- Eye service/tunnel survives reboot;
- old fixed VHD storage is gone;
- final physical `X:` Dev Drive exists;
- clean Ubuntu 24.04 WSL exists under StealthEye;
- old `steal` profile is retired after local preservation;
- HEC laptop residue is gone;
- Eye Google connectors point at the Eye identity;
- DPAPI-NG machine secret persistence is reboot-validated.

Remaining platform choices are optional/non-blocking: whether to uninstall disabled Tailscale, whether `E:` is ever reformatted NTFS, and which additional user-local/Linux packages are actually needed.

The clean v2 architecture is ready for implementation in `X:\Repos\eye`.
