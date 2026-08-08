# EYE_PLATFORM.md

**Status:** Canonical target platform roles plus current/verifiable machine boundaries  
**Baseline date:** 2026-08-07  
**Machine:** `STEALTHEYELLC`

This document separates target platform shape from dynamic software state. Re-query Windows build, firmware, drivers, installed tools, WSL, storage, and service state whenever exact current values matter.

## 1. Machine baseline

Dedicated machine:

```text
Name: STEALTHEYELLC
Manufacturer: HP
Model family: OMEN Gaming Laptop 16-ap0xxx
Windows: Windows 11 Home x64
Interactive profile: C:\Users\StealthEye
```

Physical baseline includes AMD Ryzen 9 8940HX (16C/32T), 32 GB RAM, NVIDIA RTX 5060 Laptop GPU (~8 GB VRAM), AMD Radeon 610M, Samsung ~1 TB NVMe, 1920x1200 internal display, MediaTek Wi-Fi 6E, and built-in Realtek GbE.

See `HARDWARE.md` for the detailed physical snapshot and current device-encryption posture.

## 2. Standalone Eye path

Remote ChatGPT access:

```text
ChatGPT
  -> OpenAI Secure MCP Tunnel
  -> tunnel-client.exe on STEALTHEYELLC
  -> loopback MCP
  -> stable Eye host (LocalSystem)
```

The tunnel is transport only. Eye remains locally functional without a ChatGPT/tunnel connection.

HEC/VPS, Docker, Kubernetes, Tailscale, Codex, ChatGPT Work, and a paid API controller are not required parts of steady-state Eye.

## 3. Final runtime target

Exactly one permanent Windows SCM service:

```text
Windows SCM
  -> eye.exe stable host (LocalSystem)
       -> active supervised versioned capability-engine child process
       -> previous engine retained for rollback
       -> on-demand active-session workers
       -> host-owned jobs / ConPTY / artifacts / triggers / state
```

The replaceable engine is a separate child process, not a DLL loaded into the stable host.

No second Windows service, permanent user-session helper, permanent Node/browser daemon, or competing MCP server is part of the target.

## 4. Target storage roles

Canonical roles:

```text
C: Windows / applications / stable host state / encrypted secrets / engine metadata
X: physical ReFS Dev Drive / repos / hot workspaces / job spool / temporary artifacts / block clones
E: models / media / archives / large downloads / cold and durable bulk artifacts
WSL Linux filesystem: Linux-native permission-sensitive work
```

Target development volume:

```text
X: approximately 300 GiB, physical internal-NVMe ReFS Dev Drive, trusted
```

Tiny authoritative host state must remain under a SYSTEM-owned path on `C:` and must not depend on `X:` existing.

The external `E:` device contains important bulk/archive material. Destructive format/partition operations involving `E:` are out of scope unless explicitly requested.

## 5. Repository and executable identity

```text
Product: StealthEye
Project: Eye
Repository: StealthEyeLLC/eye
Executable: eye.exe
CLI: eye
Windows service: StealthEye
Local checkout target: X:\Repos\eye
```

The previous `se` repository remains prototype/history material and must not be ported wholesale into Eye.

## 6. Windows login/account boundary

Dedicated interactive account/profile:

```text
Account: StealthEye
Profile: C:\Users\StealthEye
```

Login/account/autologon architecture is not a current Eye implementation target. Leave the active arrangement alone unless the owner explicitly requests a change.

Do not store the account password or recovery material in repository files.

Eye queries actual active-session/lock state and uses native service-owned launch APIs when interactive-user work is required.

## 7. Current device-encryption posture

The current OS-volume configuration has been explicitly verified as fully decrypted with BitLocker protection off and no key protectors.

Automatic device encryption is currently disabled through:

```text
HKLM\SYSTEM\CurrentControlSet\Control\BitLocker
PreventDeviceEncryption = 1
```

Treat this as current configured state and re-query `manage-bde -status` before encryption-sensitive operations.

## 8. WSL target

```text
Distribution: Ubuntu-24.04
Release family: Ubuntu 24.04 LTS
WSL version: WSL2
systemd: enabled
Default Linux user: root
```

WSL runs through active-user execution from the stable host. A permanent WSL/user helper is not required.

Linux-native permission-sensitive workloads belong in the distro filesystem rather than depending on ReFS metadata semantics.

## 9. Developer/tooling baseline

Verify/install only the useful baseline and add specialized tools on demand:

- Git;
- GitHub CLI;
- .NET SDK required by Eye;
- PowerShell;
- Node/npm only for tasks requiring it;
- CMake;
- Ninja;
- FFmpeg;
- VS Code;
- `uv` / `uvx`;
- ripgrep;
- NVIDIA/CUDA stack appropriate to the GPU;
- WSL.

Developer Mode and long-path support are useful Windows settings where applicable.

Do not rebuild historical package inventories merely because they once existed.

## 10. Public MCP boundary

Canonical target v2 surface:

```text
eye_inspect
eye_run
eye_change
eye_interact
eye_external
eye_live
```

The first five are effect-class capability facades. `eye_live` is UI-only and performs no machine operation.

`wait` and `transfer` remain operation families beneath those facades rather than extra top-level tools.

See `MCP_CONTRACT.md` and `contracts/eye-mcp-v2.json`.

## 11. Host-owned repair boundary

Even when the versioned feature engine is unavailable, the stable host must retain enough capability to diagnose and repair Eye:

- system/capability status;
- engine status/restart/activate/rollback;
- raw SYSTEM/user/WSL execution;
- durable jobs/terminals;
- artifact reads;
- mission/trigger state;
- minimal Eye Live monitoring.

This boundary is the reason rapid feature code stays in a separate engine process.

## 12. Previously validated architecture evidence

Prior disposable experiments on this machine established that:

1. `CreateProcessAsUser` from a genuine LocalSystem SCM service can launch the active interactive user.
2. stdout/stderr capture can work through inherited pipes.
3. user children can be placed in service-owned Job Objects.
4. WSL can be invoked through the active-user execution path.
5. short-lived desktop workers can be launched into the interactive session.
6. Per-Monitor V2 workers can operate in physical display coordinates.
7. installed Chrome can run under the user and be controlled from SYSTEM over loopback CDP using a dedicated profile.
8. native cross-session ConPTY works.
9. `ReleasePseudoConsole` was exported on the tested Windows build.
10. DPAPI-NG `LOCAL=user`, called as LocalSystem, protected throwaway material across reboot while the interactive user could not decrypt it.

These are implementation evidence, not reasons to preserve historical prototype topology.

## 13. Machine secret target

Locally retained Eye credentials use the validated pattern:

```text
LocalSystem
  -> DPAPI-NG descriptor LOCAL=user
  -> encrypted blob + non-secret metadata
  -> SYSTEM-owned machine storage
```

Never commit secret values, passwords, private keys, recovery material, or plaintext provider credentials.

## 14. External identity

Operational Google identity:

```text
StealthEye <stealtheye.eye@gmail.com>
```

Separate mailbox:

```text
stealtheye@stealtheye.io
```

Keep those identities distinct unless explicitly changed.

## 15. Power/availability target

The dedicated machine is intended to remain available for unattended Eye operation where practical.

Avoid unnecessary sleep/hibernation/lid-triggered shutdown, and use Windows power requests for long host-owned work where appropriate.

Power configuration is dynamic machine state and should be explicitly verified rather than assumed.

## 16. Platform verification principle

Eye should eventually expose live machine manifests so ChatGPT does not depend on stale documentation for dynamic state.

Before destructive, firmware-sensitive, resource-sensitive, or final-cutover operations, query current truth for the relevant Windows build, storage, encryption, WSL, driver, service, tunnel, session, GPU, and tool state.
