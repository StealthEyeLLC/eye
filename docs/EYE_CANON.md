# EYE_CANON.md

**Status:** Canonical source for the Eye / StealthEye ChatGPT project  
**Baseline date:** 2026-08-07  
**Product:** StealthEye  
**Repository:** `StealthEyeLLC/eye`  
**Local repository:** `X:\Repos\eye`  
**Primary executable:** `eye.exe`  
**Public MCP tool name:** `eye`

## 1. Purpose

StealthEye is a laptop-native execution substrate built specifically so ChatGPT can operate the dedicated StealthEye Windows machine with broad, predictable authority through one stable tool surface.

The laptop is not being designed as a conventional human-first developer workstation. Human usability matters where needed, but the primary optimization target is reliable machine operation by ChatGPT.

## 2. Core invariant: no avoidable authority friction

When the owner intentionally grants an authority to Eye, StealthEye itself should preserve that authority rather than voluntarily downscoping it.

StealthEye should not add internal approval layers, privilege tiers, narrow allowlists, redundant confirmation mechanisms, or artificial capability wrappers merely to constrain already-granted authority.

The intended test is:

> If ChatGPT knows what needs to be done and the owner has already granted the underlying authority, can Eye just do it?

The intended answer is normally yes.

This does **not** mean StealthEye can bypass controls imposed by Windows secure desktop, OpenAI, GitHub, Google, other providers, hardware, network availability, or the execution environment itself. It means StealthEye should not manufacture extra friction on top of those real boundaries.

## 3. Stable public interface

Eye exposes one stable MCP tool shape:

```text
eye({ op, args })
```

The public tool shape should remain small and stable while the operation namespace can grow.

Avoid proliferating many public MCP tools unless a concrete reason appears.

## 4. Target request path

```text
ChatGPT
  -> OpenAI
  -> Secure MCP Tunnel
  -> tunnel-client on STEALTHEYELLC
  -> loopback MCP endpoint
  -> eye.exe Windows service
```

The Secure MCP Tunnel is transport only.

`eye.exe` must remain independently useful on the laptop without understanding or owning OpenAI tunnel lifecycle.

HEC, VPS, SEZU, Caddy, Incus, Docker, Tailscale, and SSH are not part of the target Eye request path.

## 5. Permanent process topology

Target permanent runtime:

```text
Windows Service Control Manager
  -> eye.exe service (LocalSystem)
```

There should be **one permanent Eye Windows service** running as LocalSystem.

The final architecture does not require a permanent `eye session` user helper, logon task, tray process, or user-session daemon.

The service owns machine execution and creates user-session processes on demand.

The current prototype still uses a transitional session helper for some user-context operations. Do not confuse that transition mechanism with the final topology.

## 6. Active-user execution

For operations that need the logged-in interactive user, the service uses the native Windows path:

- identify the active session;
- obtain its user token with `WTSQueryUserToken`;
- construct its environment with `CreateEnvironmentBlock`;
- call `CreateProcessAsUser`;
- capture standard input/output/error with inherited handles;
- place the actual child in a service-owned Job Object;
- resume and supervise the process.

Live experiments proved that a genuine LocalSystem SCM service can launch an active-session process owned by the interactive user.

The earlier prototype failure was caused by per-command Job Object / launcher sequencing, not by a fundamental SCM or Windows-session barrier.

## 7. Process and terminal execution

Native building blocks:

- `CreateProcessAsUser`
- Windows Job Objects
- anonymous/inheritable pipes
- native ConPTY (`CreatePseudoConsole`)
- current ConPTY lifetime APIs including `ReleasePseudoConsole` where appropriate
- process handles and exit codes

Live experiments proved cross-session user execution, direct stdout/stderr capture, Job Object assignment, native ConPTY in the active session, and WSL invocation through that user process.

The final implementation should not require Pty.Net unless native ConPTY later proves insufficient for a concrete requirement.

## 8. WSL

WSL is launched through the active user's token from the LocalSystem service.

A permanent user helper is not required for WSL.

Current clean StealthEye-account baseline:

```text
Ubuntu 24.04.4 LTS
WSL2
systemd enabled
root default user
```

Linux-native workloads that require Unix permission/ownership semantics live inside the WSL Linux filesystem rather than depending on ReFS-hosted metadata behavior.

## 9. Desktop-bound operations

Operations that genuinely need the interactive desktop use a short-lived on-demand worker created by the LocalSystem service in the active user session.

Examples:

- screen capture;
- UI Automation;
- clipboard;
- keyboard/mouse input;
- window APIs.

Target shape:

```text
eye.exe service
  -> short-lived eye.exe worker in session N
  -> inherited pipe(s)
  -> result
  -> exit
```

Do not add a permanent user worker unless measured performance later justifies it.

The worker opts into Per-Monitor V2 DPI awareness before screen-coordinate work and favors native Windows UI Automation / COM.

## 10. Lock and sign-in state

Eye must query and report the real Windows session lock state.

When the session is locked, ordinary desktop capture can be unavailable/black because the secure desktop is active.

Eye should report/fail desktop operations naturally when secure desktop prevents them. It should not pretend the desktop is accessible.

Machine, CLI, WSL, browser/CDP, file, and service operations can continue when the interactive desktop is locked where Windows permits them.

The dedicated `StealthEye` Windows account is configured for automatic console sign-in. Reboot validation has repeatedly returned the machine directly to the interactive `StealthEye` desktop. The previous `steal` account/profile has been retired.

## 11. Browser

Browser architecture:

- use installed system Chrome;
- launch it as the active user;
- use a dedicated StealthEye browser profile/data directory;
- bind Chrome DevTools Protocol to loopback;
- control it from the LocalSystem service through CDP.

Live experiments proved this works across the SYSTEM -> active-user boundary.

The user's ordinary browser/profile for personal use remains separate.

Prefer direct CDP over shipping a bundled browser or Playwright/Node runtime when direct CDP provides the required capability.

## 12. UI Automation

Use native Windows UI Automation / COM from the short-lived session worker.

Avoid introducing a large third-party desktop automation framework unless a concrete missing capability justifies it.

## 13. Transport

The OpenAI Secure MCP Tunnel is external transport.

Target separation:

```text
tunnel-client
  -> forwards to 127.0.0.1:<Eye MCP port>

eye.exe
  -> serves MCP on loopback
```

The current prototype contains custom tunnel supervision, but that is not the target architecture.

For v2, keep `tunnel-client` external to Eye under ordinary Windows startup/supervision. The exact replacement startup mechanism remains late-bound until it can be tested without risking the working control path.

## 14. External authority

### OpenAI

GitHub Actions secret names supplied by the owner:

- `EyeRuntime`
- `OpenAIAdmin`

`EyeRuntime` is the runtime/tunnel-side OpenAI credential.

`OpenAIAdmin` is the deliberately broad OpenAI organization-admin credential.

Neither is a GitHub deploy key.

StealthEye should not intentionally replace `OpenAIAdmin` with a narrower credential if the owner has chosen to grant full OpenAI administrative authority.

No secret values belong in repository source or project documentation.

### GitHub

Repository:

```text
StealthEyeLLC/eye
```

It is public.

The `StealthEye` Windows account clones/pulls it over SSH into `X:\Repos\eye`. The current SSH credential does not provide push authority; broader machine-side GitHub authority can be added later using an authentication primitive that matches the authority the owner actually intends to grant.

Repository source must remain free of plaintext credentials.

Do not choose a repo-scoped deploy key merely to reduce intentionally broader authority.

### Google identity

Operational Eye Google identity:

```text
StealthEye <stealtheye.eye@gmail.com>
```

Current ChatGPT connector state:

- Gmail: **Eye identity connected**
- Google Drive: **Eye identity connected**
- Google Calendar: **Eye identity connected**
- Google Contacts: **Eye identity connected**

This is Eye's durable service identity for communication, files, scheduling, and contacts.

The account is operationally Eye's identity while remaining legally/administratively owned and recoverable by the owner.

The separate `stealtheye@stealtheye.io` mailbox remains hosted through Titan and has not been migrated to Google Workspace.

## 15. Storage roles

Current canonical storage roles:

```text
C:  Windows / system / installed applications
X:  300 GiB physical trusted ReFS Dev Drive / repos / build workspace
E:  bulk StealthEye data / models / archives / large artifacts
WSL Linux filesystem: Linux-native permission-sensitive work
```

The old fixed `C:\Sovereign Node.vhdx` and temporary `C:\StealthEye-Dev.vhdx` fallback are gone.

## 16. Machine secret persistence

Credentials that the LocalSystem Eye service must retain on the laptop are protected with **DPAPI-NG `LOCAL=user` invoked by LocalSystem**.

Persist only encrypted blobs and non-secret metadata under a SYSTEM-owned machine path.

This exact behavior was live-validated on `STEALTHEYELLC` with throwaway random plaintext:

- protect/unprotect succeeded as SYSTEM;
- the persisted encrypted blob survived reboot and still decrypted as SYSTEM;
- the interactive `StealthEye` account could not decrypt the same blob;
- a direct `SID=S-1-5-18` protection descriptor failed during encryption and is not the chosen path.

The validation artifacts were deleted after the test.

## 17. Dependency posture

The target core is C# / .NET.

Prefer Windows-native capabilities wherever they are sufficient.

Target direction:

- one .NET service;
- native Win32 process/session APIs;
- native ConPTY;
- native Job Objects;
- native/COM UI Automation;
- direct Chrome CDP;
- WSL through native process launch;
- ASP.NET/MCP pieces needed for the loopback server.

Avoid Docker and large runtime stacks unless a concrete requirement appears.

## 18. Things Eye is deliberately not

Do not turn Eye into:

- a generic workflow engine;
- a plugin marketplace;
- a policy framework;
- a receipt/evidence bureaucracy;
- an approval engine;
- a generic agent platform;
- a multi-machine orchestration framework;
- a VPS-dependent system.

Add abstractions only after a real requirement proves they are needed.

## 19. Design style

Favor:

- directness;
- raw native authority;
- predictable machine structure;
- small stable interfaces;
- durable operation;
- minimal permanent processes;
- minimal third-party dependencies;
- clear separation between transport and local capability.

Avoid architecture ceremony and speculative layers.
