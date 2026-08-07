# WORKLOG.md

**Purpose:** Durable record of major Eye/StealthEye preparation and evidence gathered before the clean v2 implementation.

This is a milestone log, not a transcript of every command. Prototype experiments remain evidence unless promoted into the canonical docs.

## 2026-08-06 — direct Eye path and prototype work

- Established the working direct path from ChatGPT through OpenAI Secure MCP Tunnel to the laptop-native Eye MCP service.
- Confirmed Eye runs as a LocalSystem Windows service and exposes the single `eye` MCP tool surface.
- Preserved old `se` repository state, including local-only commits.
- Began separating the direct laptop-native design from earlier HEC/VPS infrastructure.

## 2026-08-07 — repository preservation

Before destructive platform work:

- copied the real `X:` payload to `E:\StealthEye\archives\pre-platform-rebuild-20260807`;
- verified the archived old `se` repository retained its local-only commits;
- created and verified `E:\StealthEye\archives\pre-platform-rebuild-20260807\se-full.bundle` as a complete Git-history bundle.

Result: old prototype history is independently recoverable without relying on the old `X:` volume.

## 2026-08-07 — identity transition preparation

- Created the local administrator account `StealthEye` with profile `C:\Users\StealthEye`.
- Seeded Git config and the existing GitHub SSH identity into the new profile.
- Avoided copying polluted old-profile Git safe-directory state.
- Prepared transitional session startup for the prototype.
- Disabled screen locking/screensaver behavior where remotely possible.
- Confirmed Windows secure desktop remains a real boundary for synthetic credential entry.

## 2026-08-07 — power/availability preparation

Configured the dedicated laptop toward continuous availability:

- hibernation disabled;
- automatic sleep disabled;
- lid close set to do nothing;
- automatic display/disk shutdown disabled;
- screensaver/lock behavior reduced where applicable;
- automatic logged-in-user reboot behavior constrained through Windows Update policy.

## 2026-08-07 — storage cleanup and migration

Discovered that old `X:` was a fully allocated ~400 GB VHDX and contained a fixed ~192 GB pagefile despite observed peak pagefile use being only a few GB.

Preparatory actions:

- configured next-boot pagefile to `C:\pagefile.sys`, 16 GB initial / 32 GB maximum;
- copied/preserved real `X:` data before storage changes;
- staged and tested a dynamic 400 GB ReFS Dev Drive VHDX as fallback, then detached it;
- reorganized external `E:` under `E:\StealthEye\...`;
- moved/copy-verified LM Studio model payload to `E:\StealthEye\models\lmstudio`;
- configured Ollama model location as `E:\StealthEye\models\ollama`.

## 2026-08-07 — Docker removal

- pruned old Docker containers/images/build cache;
- compacted Docker storage before removal;
- uninstalled Docker Desktop;
- unregistered the Docker WSL distro;
- removed leftover Docker program/user data.

Final Eye architecture is not expected to depend on Docker.

## 2026-08-07 — old WSL cleanup

The old Ubuntu 22.04 distro under `steal` had accumulated large caches, duplicate models, temporary data and old virtual environments.

- removed disposable Linux cache/model/temp/venv baggage;
- compacted the WSL VHDX substantially;
- cleaned stale shell-profile references;
- verified basic Git/Python/Linux operation;
- shut WSL down when idle.

## 2026-08-07 — machine tooling cleanup

Cleaned machine PATH and established useful developer tools machine-wide, including Git, GitHub CLI, .NET, Node/npm, CUDA, CMake, Ninja, FFmpeg, VS Code, uv/uvx and ripgrep.

Also enabled Windows Developer Mode and long-path support.

## 2026-08-07 — background-app cleanup

Disabled/removed nonessential background infrastructure including:

- HEC laptop tunnel runtime;
- Docker Desktop;
- Razer background services where no Razer hardware was present;
- HP analytics/support background components;
- assorted old user startup entries.

OMEN hardware support remained intact.

## 2026-08-07 — architecture probes

Disposable live probes materially simplified the favored v2 design.

### LocalSystem -> active user

- Demonstrated `CreateProcessAsUser` from a real LocalSystem SCM service into the active interactive session.
- Captured stdout/stderr directly through inherited pipes.
- Demonstrated correct user profile/environment behavior.

### Job Objects

- Demonstrated cross-session assignment of the actual launched user process to a service-owned Job Object.
- Identified the old prototype's per-command launcher/job sequencing as the cause of earlier access-denied behavior.

### WSL

- Launched WSL through the active user's process/token path without a permanent user helper.

### Desktop worker

- Launched an on-demand interactive-session worker from SYSTEM.
- Verified Per-Monitor V2 DPI awareness yields physical 1920x1200 coordinates.
- Confirmed secure-desktop lock state produces unavailable/black normal desktop capture.

### Browser

- Launched installed Chrome as the active user with a dedicated temporary profile.
- Connected from SYSTEM over loopback Chrome DevTools Protocol.
- Cleaned the disposable browser/profile afterward.

### ConPTY

- Demonstrated native cross-session ConPTY from SYSTEM to an active-user shell.
- Captured terminal output successfully.
- Confirmed the current Windows build exports `ReleasePseudoConsole`.

### Real SCM validation

- Built and ran a disposable real LocalSystem Windows service to rule out Task Scheduler/probe artifacts.
- The service successfully launched an active-user child in the interactive session.
- Removed disposable service/probe artifacts afterward.

Result: favored v2 topology became one permanent LocalSystem Eye service with service-owned native execution and short-lived interactive workers rather than a permanent session daemon.

## 2026-08-07 — external identity/authority preparation

- New public v2 repository established as `StealthEyeLLC/eye`.
- GitHub Actions secret names supplied by the owner: `EyeRuntime` and `OpenAIAdmin`.
- Eye Google identity created as `stealtheye.eye@gmail.com`.
- Google Drive, Calendar and Contacts connections verified under the Eye identity.
- `stealtheye@stealtheye.io` remains on Titan mail for now.

## 2026-08-07 — vendor research

Checked current primary documentation for design-sensitive areas:

- LocalSystem / `WTSQueryUserToken` / `CreateProcessAsUser`;
- current ConPTY lifetime APIs;
- Chrome remote-debugging profile requirements;
- physical vs VHD Dev Drive behavior;
- DPAPI-NG protection;
- OpenAI Secure MCP Tunnel.

See `RESEARCH.md`.

## 2026-08-07 — clean Eye repository initialization

With owner approval, initialized `StealthEyeLLC/eye` as a documentation-first public repository.

No old `se` source was imported.

Initial material documents project identity, canonical architecture, current platform state, decision ledger, hardware, research, worklog and cutover plan.

## 2026-08-07 — physical cutover completed

### Windows account / autologon

- Confirmed the new `StealthEye` local administrator account and profile.
- Configured Microsoft Sysinternals Autologon for the dedicated `StealthEye` console account.
- Rebooted/restarted and physically confirmed Windows goes directly to the `StealthEye` desktop without routine credential entry.
- Live Eye `session.info` confirmed `StealthEye` as the interactive user.
- Kept the local password out of repository/project documentation.

### Pagefile

After reboot:

- confirmed only `C:\pagefile.sys` remained;
- observed a 16 GiB current pagefile allocation;
- confirmed the old ~192 GiB `X:\pagefile.sys` was gone.

### Old fixed VHD removal

Immediately before deletion, reconfirmed the preservation archive and complete `se-full.bundle`, then removed:

```text
C:\Sovereign Node.vhdx
```

This returned roughly 400 GB of physical `C:` space.

### Physical Dev Drive

Post-delete shrink measurement showed ~304.9 GiB of supported shrink capacity on `C:`.

Actions:

- shrank `C:` by exactly 300 GiB;
- created a new 300 GiB partition on internal Samsung NVMe disk 0;
- assigned `X:`;
- formatted it using `Format-Volume -DevDrive`;
- labeled it `Eye Dev`;
- verified ReFS and trusted Dev Drive status;
- deleted the staged fallback `C:\StealthEye-Dev.vhdx`.

### Permanent repo clone

Created `X:\Repos\eye` and cloned `StealthEyeLLC/eye` over SSH from the new Windows account.

The migrated GitHub SSH key works for clone/pull. Git author identity was corrected to:

```text
StealthEye <stealtheye.eye@gmail.com>
```

A later live push established that the current laptop SSH identity is read-only/deploy-key authority rather than repository write authority.

### Fresh WSL

Installed `Ubuntu-24.04` under the StealthEye Windows account.

Observed runtime:

```text
Ubuntu 24.04.4 LTS
6.6.87.2-microsoft-standard-WSL2
```

Configured and verified root as default user and systemd running.

## 2026-08-07 — HEC/Tailscale cleanup

- Removed the HEC laptop task/files/dedicated SSH key/handshake residue.
- Performed a final targeted check for HEC-specific services, scheduled tasks and standard install paths; none remained.
- Stopped Tailscale and immediately confirmed direct ChatGPT -> Eye access continued working.
- Disabled Tailscale service; package retained temporarily for easy reversal.

## 2026-08-07 — old profile retirement completed

The old `C:\Users\steal` profile was inspected before deletion.

To avoid needless cloud hydration, cloud-only iCloud placeholders were not forced local. Locally meaningful material was preserved first under:

```text
E:\StealthEye\archives\pre-profile-retirement-20260807
```

Preserved:

- Desktop: 2 files;
- Documents: local files/directories;
- Downloads: 9 files;
- top-level profile inventory JSON;
- seven locally resident iCloud Photos media files totaling ~44 MiB.

Then:

- removed local Windows user `steal`;
- removed its Win32 user-profile registration;
- removed the residual `C:\Users\steal` directory;
- rebooted and confirmed the directory remains gone;
- verified the active `StealthEye` user has only the clean `Ubuntu-24.04` WSL registration.

## 2026-08-07 — DPAPI-NG reboot persistence validated

A fresh throwaway 32-byte random value was protected by LocalSystem using DPAPI-NG descriptor `LOCAL=user`.

Only the encrypted blob and a SHA-256 verification value were persisted under `C:\ProgramData\StealthEye`.

The laptop was rebooted. After automatic sign-in and the Eye/tunnel control path returned:

- LocalSystem successfully decrypted the persisted blob and reproduced the expected hash;
- interactive `STEALTHEYELLC\StealthEye` could read the test material but `NCryptUnprotectSecret` failed with `0x8009002C` and did not recover plaintext;
- all temporary probe source, binary, blob and hash files were removed.

No real credential was involved.

This completed the missing reboot-persistence validation and the mechanism was promoted into the v2 canonical design.

## 2026-08-07 — Eye Gmail connector corrected

The ChatGPT Gmail connector now points to `stealtheye.eye@gmail.com`. Live mail addressed to the Eye identity was read successfully through the connector.

Drive, Calendar and Contacts were already on the Eye identity.

## Current boundary

The blocking laptop/platform cutover is complete.

The small v2 architecture has been frozen for initial implementation. Remaining choices such as exact replacement tunnel startup, broader machine-side GitHub authority, optional external API surfaces, Tailscale package removal and future `E:` filesystem changes are deliberately late/non-blocking decisions.

Clean implementation can now proceed in `X:\Repos\eye` without importing old `se` wholesale.
