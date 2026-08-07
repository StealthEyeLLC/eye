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

Cleaned machine PATH and established useful developer tools machine-wide, including:

- Git
- GitHub CLI
- .NET
- Node/npm
- CUDA
- CMake
- Ninja
- FFmpeg
- VS Code
- uv/uvx
- ripgrep

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

The owner returned to the laptop and crossed the secure-desktop boundary manually.

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

Immediately before deletion:

- reconfirmed `E:\StealthEye\archives\pre-platform-rebuild-20260807` exists;
- reconfirmed the complete `se-full.bundle` exists.

Then removed:

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
- verified it is ReFS;
- verified `fsutil devdrv query X:` reports a trusted developer volume.

The staged fallback `C:\StealthEye-Dev.vhdx` was then deleted.

### Permanent repo clone

Created:

```text
X:\Repos\eye
```

and cloned `StealthEyeLLC/eye` over SSH from the new Windows account.

The migrated GitHub SSH key worked successfully.

Corrected the new account's Git author identity from the old personal value to:

```text
StealthEye <stealtheye.eye@gmail.com>
```

### Fresh WSL

The new Windows account initially had no WSL distributions.

Installed:

```text
Ubuntu-24.04
```

Observed runtime:

```text
Ubuntu 24.04.4 LTS
6.6.87.2-microsoft-standard-WSL2
```

Configured and verified:

- default Linux user: root;
- systemd enabled;
- `systemctl is-system-running` => `running`;
- normal launch requires no Linux username/password prompt.

A first attempt to write `/etc/wsl.conf` produced literal escape characters; it was immediately overwritten with a correct multiline file, the distro was terminated/relaunched, and root-default + systemd were reverified.

## Current boundary after physical cutover

Core account/storage/WSL/repository cutover is now complete.

Remaining platform cleanup:

- inspect old `C:\Users\steal` user-facing/iCloud material;
- preserve anything deliberately wanted;
- retire old `steal` account/profile and old WSL registration;
- remove inert HEC residue;
- retain the prototype session helper only until v2 native active-user execution replaces it;
- perform final small architecture freeze before clean implementation begins.
