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

Eye should expose one stable MCP tool:

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

The final architecture should not require a permanent `eye session` user helper, logon task, tray process, or user-session daemon.

The service owns machine execution and creates user-session processes on demand.

The current prototype still uses a transitional session helper for some user-context operations. Do not confuse that transition mechanism with the final topology.

## 6. Active-user execution

For operations that need the logged-in interactive user, the service should use the native Windows path:

- identify the active session;
- obtain its user token;
- construct the appropriate environment block;
- call `CreateProcessAsUser`;
- capture standard input/output/error with inherited handles;
- place the actual child in a service-owned Job Object;
- resume and supervise the process.

Live experiments proved that a genuine LocalSystem SCM service can launch an active-session process owned by the interactive user.

The earlier prototype failure was caused by per-command Job Object / launcher sequencing, not by a fundamental SCM or Windows-session barrier.

## 7. Process and terminal execution

Preferred native building blocks:

- `CreateProcessAsUser`
- Windows Job Objects
- anonymous/inheritable pipes
- native ConPTY (`CreatePseudoConsole`)
- process handles and exit codes

Live experiments proved:

- cross-session user execution;
- direct stdout/stderr capture;
- Job Object assignment after user-process creation;
- native ConPTY in the active user session;
- WSL invocation from that user process.

The current Windows build also exports `ReleasePseudoConsole`.

The final implementation should not require Pty.Net if native ConPTY covers the needed terminal behavior.

## 8. WSL

WSL should be launched through the active user's token from the LocalSystem service.

A permanent user helper is not required for WSL.

Current clean StealthEye-account baseline:

```text
Ubuntu 24.04.4 LTS
WSL2
systemd enabled
root default user
```

Linux-native workloads that require Unix permission/ownership semantics should live inside the WSL Linux filesystem rather than depending on ReFS-hosted metadata behavior.

## 9. Desktop-bound operations

Operations that genuinely need the interactive desktop may use a short-lived on-demand worker created by the LocalSystem service in the active user session.

Examples:

- screen capture;
- UI Automation;
- clipboard;
- keyboard/mouse input;
- window APIs.

Preferred first design:

```text
eye.exe service
  -> short-lived eye.exe worker in session N
  -> inherited pipe(s)
  -> result
  -> exit
```

Do not add a permanent user worker unless measured performance later justifies it.

The worker should opt into Per-Monitor V2 DPI awareness before screen-coordinate work.

## 10. Lock state

Eye must query and report the real Windows session lock state.

When the session is locked, ordinary desktop capture can be unavailable/black because the secure desktop is active.

Eye should report/fail desktop operations naturally when secure desktop prevents them. It should not pretend the desktop is accessible.

Machine, CLI, WSL, browser/CDP, file, and service operations can continue when the interactive desktop is locked where Windows permits them.

The dedicated `StealthEye` Windows account is configured for automatic console sign-in so normal reboot should return directly to the interactive desktop.

## 11. Browser

Favored browser architecture:

- use the installed system Chrome;
- launch it as the active user;
- use a dedicated StealthEye browser profile;
- bind Chrome DevTools Protocol to loopback;
- control it from the LocalSystem service through CDP.

Live experiments proved this works across the SYSTEM -> active-user boundary.

The user's ordinary browser/profile for personal use should remain separate.

Prefer direct CDP over shipping a bundled browser or Playwright/Node runtime if direct CDP provides the required functionality.

## 12. UI Automation

Favor native Windows UI Automation / COM from the session worker.

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

For v2, favor ordinary Windows startup/supervision for `tunnel-client` rather than custom tunnel-supervisor code inside Eye.

The exact final tunnel-client startup mechanism is not yet canonical.

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

The `StealthEye` Windows account successfully clones it over SSH into `X:\Repos\eye`.

Repository source must remain free of plaintext credentials.

If broader GitHub authority is later intentionally granted to Eye, do not select a repo-scoped deploy key merely to reduce that authority. Choose an authentication primitive matching the authority the owner intends to grant.

### Google identity

Operational Eye Google identity:

```text
StealthEye <stealtheye.eye@gmail.com>
```

Current verified ChatGPT connector state on 2026-08-07:

- Google Drive: **Eye identity connected**
- Google Calendar: **Eye identity connected**
- Google Contacts: **Eye identity connected**
- Gmail: **not currently connected to the Eye identity; connector is pointed at the owner's personal Gmail account and must be switched before Eye mail authority is considered live**

Do not read, organize, send from, or otherwise operate the personal Gmail mailbox as though it were Eye's mailbox.

The intended durable Eye identity remains `stealtheye.eye@gmail.com` for communication, files, scheduling, and contacts.

The account is operationally Eye's identity while remaining legally/administratively owned and recoverable by the owner.

The separate `stealtheye@stealtheye.io` mailbox currently remains hosted through Titan and has not been migrated to Google Workspace.

## 15. Storage roles

Current canonical storage roles:

```text
C:  Windows / system / installed applications
X:  300 GiB physical trusted ReFS Dev Drive / repos / build workspace
E:  bulk StealthEye data / models / archives / large artifacts
WSL Linux filesystem: Linux-native permission-sensitive work
```

The old fixed `C:\Sovereign Node.vhdx` and temporary `C:\StealthEye-Dev.vhdx` fallback are gone.

## 16. Dependency posture

The target core remains C# / .NET.

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

## 17. Things Eye is deliberately not

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

## 18. Design style

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
