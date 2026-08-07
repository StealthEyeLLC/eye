# EYE_PLATFORM.md

**Status:** Current laptop/platform source snapshot  
**Baseline date:** 2026-08-07  
**Machine:** `STEALTHEYELLC`

This document records the current machine state and the remaining physical/manual transition boundary. It is a snapshot, not a promise that every value will remain permanent.

## 1. Current Eye prototype

Installed prototype:

- product: StealthEye
- executable/tool: `eye`
- version observed: `0.5.1`
- service mode: Windows service
- service identity: LocalSystem
- loopback MCP endpoint observed: `http://127.0.0.1:37921/mcp`
- installed config: `C:\ProgramData\StealthEye\config.json`

The prototype is valuable evidence but is **not** automatically the v2 design.

## 2. Direct OpenAI path

The working direct path is:

```text
ChatGPT
  -> OpenAI Secure MCP Tunnel
  -> tunnel-client.exe on STEALTHEYELLC
  -> 127.0.0.1:37921/mcp
  -> eye.exe
```

The old HEC/VPS reverse tunnel has been stopped and disabled.

Direct Eye calls continued working after HEC was disabled, proving HEC is not in the active request path.

Old HEC files/keys may remain inert until final cleanup, but they are not target architecture.

## 3. Old repository preservation

The old `se` repository and its local-only history were preserved before destructive storage work.

Archive root:

```text
E:\StealthEye\archives\pre-platform-rebuild-20260807
```

A full Git bundle was created:

```text
E:\StealthEye\archives\pre-platform-rebuild-20260807\se-full.bundle
```

The bundle was verified to contain the complete history, including the local commits that had not been pushed.

The old repository is historical prototype material.

The new v2 repository is `StealthEyeLLC/eye`.

## 4. Windows identity transition

A new local Windows account exists:

```text
User: StealthEye
Full name: StealthEye
Role: local administrator
Profile: C:\Users\StealthEye
```

It is intended to become the primary interactive account.

The profile has been seeded with a clean Git identity/config and the existing GitHub SSH identity.

The old interactive account `steal` has not yet been retired.

Important: do not delete `C:\Users\steal` until user-facing data is reviewed.

A final scan showed that the old profile still contains items such as:

- Documents
- Downloads
- iCloudDrive
- iCloudPhotos
- old agent/tool state
- old per-user applications

StealthEye-specific repository history and important machine assets have already been preserved separately, but personal/iCloud material should not be blindly erased.

## 5. Current credential / login boundary

Remote platform controls allowed some account and lock-policy changes but did not allow clearing the stored Windows password or injecting credentials into Windows secure desktop.

The remaining human boundary is a one-time local unlock / account cutover.

Do not store or document the owner's unlock code in project source.

Target behavior after cutover is a dedicated always-available StealthEye machine with no avoidable interactive sign-in friction.

## 6. Power and availability

Configured direction:

- hibernation off,
- no automatic sleep,
- no automatic display shutdown,
- no automatic disk shutdown,
- lid close = do nothing,
- screensaver disabled,
- password-on-wake disabled where applicable,
- Dynamic Lock disabled for the new profile,
- machine intended to remain available continuously.

Windows Update policy was configured to avoid automatic reboot while a user is logged on.

## 7. Pagefile transition

The old `X:` currently came from a fixed VHDX and had an extremely large fixed pagefile.

Next-boot pagefile configuration was changed to:

```text
C:\pagefile.sys
Initial: 16 GB
Maximum: 32 GB
```

The old in-use `X:\pagefile.sys` remains allocated until reboot.

A controlled reboot is therefore part of the final storage cutover.

## 8. Storage

### Internal disk

Primary internal disk:

- Samsung NVMe
- ~1 TB raw capacity
- GPT
- Windows `C:` occupies most of the disk today

The old `X:` is a fixed VHDX:

```text
C:\Sovereign Node.vhdx
```

It is ~400 GB fully allocated and should be removed only after the controlled reboot and confirmation that the preservation archive is safe.

### Favored final X

Favored target:

- carve a real physical partition from the internal NVMe,
- format it as a ReFS Dev Drive,
- mount it as `X:`,
- use it for development/build/repository work.

Approximate favored size is ~300 GB, but the exact size is **not canonical yet** and should be selected after the reboot and a fresh supported-shrink measurement.

### Fallback Dev Drive

A dynamic fallback VHDX was staged:

```text
C:\StealthEye-Dev.vhdx
```

It is a 400 GB logical ReFS Dev Drive but physically tiny while empty.

It was tested successfully and then detached.

Use it only if the physical ReFS partition proves undesirable or awkward.

## 9. External bulk-data drive

`E:` is labeled:

```text
StealthEye
```

Current filesystem: exFAT.

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

Important protected archive:

```text
E:\ARCHIVE - do not touch
```

LM Studio model data was copied to:

```text
E:\StealthEye\models\lmstudio
```

Ollama machine model location is configured as:

```text
E:\StealthEye\models\ollama
```

Possible future cleanup: once another spare drive is connected and protected data is copied elsewhere, consider reformatting `E:` from exFAT to NTFS for stronger Windows semantics. This is optional and not yet approved as canonical.

## 10. Docker

Docker Desktop and its WSL distro were removed.

Old containers/images/build cache were pruned.

The target Eye architecture should not depend on Docker.

## 11. WSL

The old Ubuntu 22.04 distribution remains associated with the old `steal` user and is transitional.

It was heavily cleaned and its VHDX compacted.

The preferred direction is a fresh WSL distribution under the new `StealthEye` account after first login.

Ubuntu 24.04 LTS is favored, not yet final.

## 12. Machine-wide developer tooling

Machine-level tooling has been cleaned and made available for the future `StealthEye` account.

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

Windows long paths are enabled.

Git system config enables long paths.

Developer Mode is enabled.

Old-profile-only Python/Ollama/VS Code installations should not be copied wholesale into the new account.

## 13. Background-app cleanup

Removed or disabled legacy/nonessential background infrastructure included:

- Docker Desktop
- HEC tunnel
- HP analytics/support background tasks
- Razer background services
- various old per-user auto-start entries

OMEN hardware support was retained.

## 14. Live architecture experiments that passed

Disposable experiments proved the following from a LocalSystem service-style context:

1. `CreateProcessAsUser` can launch the active user into the real interactive session.
2. stdout/stderr can be captured directly with inherited pipes.
3. the created user process can be assigned to a service-owned Job Object.
4. WSL can be invoked through that user process.
5. a short-lived desktop worker can be launched into the interactive session.
6. DPI-aware workers see the physical monitor dimensions.
7. installed Chrome can be launched as the user with loopback CDP.
8. native ConPTY works cross-session.
9. a real SCM LocalSystem service can perform this launch even while itself living inside the normal service job.

All disposable probe services/tasks/files were cleaned after testing.

These experiments are the evidence behind the v2 no-permanent-session-helper architecture.

## 15. Lock behavior

The current Windows session was verified as locked during testing.

When locked:

- normal desktop capture is unavailable/black due to secure desktop,
- machine/service/CLI operations remain available,
- WSL and non-desktop process work can continue,
- browser/CDP can work where it does not require secure-desktop interaction.

Eye should surface this state rather than attempting to hide it.

## 16. Remaining cutover sequence

When the owner is physically back at the laptop:

1. Unlock Windows once.
2. Complete the `StealthEye` local-account credential/sign-in transition.
3. Log into `StealthEye` once.
4. Verify the interactive user context.
5. Perform one controlled reboot.
6. Confirm the new `C:` pagefile is active and the old `X:` pagefile is gone.
7. Remove the old fixed `C:\Sovereign Node.vhdx`.
8. Re-measure supported `C:` shrink.
9. Prefer creating the physical ReFS Dev Drive `X:`.
10. Create a fresh WSL distribution under `StealthEye`.
11. Establish only the user-local runtimes actually needed.
12. Review old `steal` user-facing/iCloud data.
13. Retire the old `steal` account/profile only after that review.
14. Remove inert HEC residue.
15. Then move into the clean Eye implementation phase.
