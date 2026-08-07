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
- Seeded a clean Git config and the existing GitHub SSH identity into the new profile.
- Avoided copying polluted old-profile Git safe-directory state.
- Prepared transitional session startup for the new account while architecture experiments were still in progress.
- Disabled screen locking/screensaver behavior where remotely possible.
- Confirmed Windows secure desktop remains a real boundary for synthetic credential entry.

The old `steal` account/profile remains until user-facing data is reviewed.

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

Actions:

- configured next-boot pagefile to `C:\pagefile.sys`, 16 GB initial / 32 GB maximum;
- copied/preserved real `X:` data before storage changes;
- staged and tested a dynamic 400 GB ReFS Dev Drive VHDX as fallback, then detached it;
- selected a physical internal-NVMe ReFS Dev Drive as the favored final `X:` direction, pending post-reboot shrink measurement;
- reorganized external `E:` under `E:\StealthEye\...`;
- moved/copy-verified LM Studio model payload to `E:\StealthEye\models\lmstudio`;
- configured Ollama model location as `E:\StealthEye\models\ollama`.

## 2026-08-07 — Docker removal

The old machine contained substantial stopped/created Docker infrastructure and build cache unrelated to the final Eye design.

Actions:

- pruned containers/images/build cache;
- compacted Docker storage before removal;
- uninstalled Docker Desktop;
- unregistered the Docker WSL distro;
- removed leftover Docker program/user data.

Final Eye architecture is not expected to depend on Docker.

## 2026-08-07 — WSL cleanup

The old Ubuntu 22.04 distro under `steal` had accumulated large caches, duplicate models, temporary data and old virtual environments.

Actions:

- removed disposable Linux cache/model/temp/venv baggage;
- compacted the WSL VHDX substantially;
- cleaned stale shell-profile references;
- verified basic Git/Python/Linux operation;
- shut WSL down when idle.

A fresh distro under `StealthEye` is favored after first login.

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

- Launched WSL successfully through the active user's process/token path without a permanent user helper.

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
- Identified lifetime/EOF handling details to address with current Windows APIs.

### Real SCM validation

- Built and ran a disposable real LocalSystem Windows service to rule out Task Scheduler/probe artifacts.
- The service successfully launched an active-user child in the interactive session.
- Removed all disposable service/probe artifacts afterward.

Result: favored v2 topology became one permanent LocalSystem Eye service with service-owned native execution and short-lived interactive workers rather than a permanent session daemon.

## 2026-08-07 — external identity/authority preparation

- New public v2 repository established as `StealthEyeLLC/eye`.
- GitHub Actions secret names supplied by the owner: `EyeRuntime` and `OpenAIAdmin`.
- Eye Google identity created as `stealtheye.eye@gmail.com`.
- Gmail, Google Drive, Google Calendar and Google Contacts connected to ChatGPT for that identity.
- `stealtheye@stealtheye.io` remains on Titan mail for now; no mail migration is required for initial Eye work.

## 2026-08-07 — current vendor research

Current primary documentation was checked for the design-sensitive areas:

- LocalSystem / `WTSQueryUserToken` / `CreateProcessAsUser`;
- current ConPTY lifetime APIs;
- Chrome remote-debugging profile requirements;
- physical vs VHD Dev Drive behavior;
- DPAPI-NG principal/SID secret protection;
- OpenAI Secure MCP Tunnel.

See `RESEARCH.md` for the durable reference list.

## 2026-08-07 — clean Eye repository initialization

With owner approval, initialized `StealthEyeLLC/eye` as a documentation-first public repository.

No old `se` source was imported.

Initial repository material documents:

- project identity and architectural intent;
- canonical design;
- current platform state;
- decision/open-item ledger;
- live laptop hardware/runtime facts;
- external research findings;
- this worklog;
- the local cutover checklist.

Implementation remains intentionally deferred until the pending physical laptop cutover and final small architecture pass are complete.
