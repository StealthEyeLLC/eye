# WORKLOG.md

**Purpose:** Durable milestone/evidence log for Eye / StealthEye.  
**Baseline date:** 2026-08-07

This is not a command transcript. Historical experiments remain evidence; only decisions promoted into `EYE_CANON.md` / `EYE_DECISIONS.md` are canonical.

## 2026-08-06 — direct laptop-native Eye path

- Established direct ChatGPT -> OpenAI Secure MCP Tunnel -> laptop loopback -> Eye service operation.
- Confirmed the runtime can operate as a LocalSystem Windows service.
- Began separating the target laptop-native design from earlier HEC/VPS infrastructure.
- Preserved the older `se` repository/history as prototype reference rather than treating it as the new implementation base.

## 2026-08-07 — platform architecture preparation

Major platform direction established:

- dedicated Windows interactive identity `StealthEye`;
- intended repository path `X:\Repos\eye`;
- intended physical ReFS Dev Drive `X:` on the internal NVMe;
- external `E:` reserved for bulk data/models/archives/artifacts;
- Ubuntu 24.04 WSL2 with systemd for Linux-native workloads;
- Docker removed from the target Eye architecture;
- HEC/VPS removed from the target Eye request path;
- OpenAI Secure MCP Tunnel retained as external transport only.

Historical platform details are evidence, not requirements to reproduce every old package or setting. Current target roles live in `EYE_PLATFORM.md`.

## 2026-08-07 — LocalSystem -> active-user execution proven

Disposable tests established:

- a genuine LocalSystem SCM service can identify the active session and launch its user using `WTSQueryUserToken` / `CreateEnvironmentBlock` / `CreateProcessAsUser`;
- stdout/stderr capture can work through inherited pipes;
- launched user children can be owned by a service Job Object;
- WSL can be invoked through the same active-user path;
- a permanent user-session helper is not fundamentally required.

This evidence is the basis for the service-owned active-user execution architecture.

## 2026-08-07 — desktop-worker path proven

Disposable testing established:

- a short-lived active-session worker can be launched from SYSTEM;
- Per-Monitor V2 DPI awareness can provide physical desktop coordinates;
- secure desktop/lock state is a real boundary for ordinary desktop capture/control.

The target remains short-lived on-demand workers rather than a permanent session daemon.

## 2026-08-07 — browser/CDP path proven

Disposable testing established:

- installed Chrome can be launched under the active interactive user with a dedicated data directory;
- a LocalSystem service can control that browser over loopback Chrome DevTools Protocol.

The canonical browser direction is now raw generated typed CDP bindings with Playwright .NET only as an optional accelerator.

## 2026-08-07 — native ConPTY path proven

Disposable testing established:

- native cross-session ConPTY works from the service-owned execution path;
- terminal output can be captured;
- the tested Windows build exports `ReleasePseudoConsole`.

The target no longer requires Pty.Net unless a concrete native gap is demonstrated.

## 2026-08-07 — machine secret persistence proven

A throwaway value was protected by LocalSystem using DPAPI-NG descriptor `LOCAL=user`.

Testing established:

- LocalSystem could protect/unprotect;
- the encrypted blob remained usable by LocalSystem across reboot;
- the interactive `StealthEye` account could not decrypt the same test blob;
- a direct `SID=S-1-5-18` protection descriptor did not succeed in the tested environment.

No real credential was used.

This mechanism was promoted into the canonical local secret-persistence design.

## 2026-08-07 — first v2 repository implementation

The clean `StealthEyeLLC/eye` repository moved from documentation-only into an early implementation:

- .NET Windows service host;
- loopback MCP endpoint;
- LocalSystem process execution;
- active-user process execution;
- Job Object ownership;
- run request/result models;
- initial operation dispatcher;
- first public MCP wrapper.

A parallel v2 development build was successfully exercised as a LocalSystem service and demonstrated an active-user `whoami` command returning the dedicated Windows account identity.

Local follow-on work also prototyped native ConPTY integration, including current lifetime APIs and reusable process-runner helpers. Those changes must be reconciled carefully against the clean repository rather than recreating incidental EOL churn.

## 2026-08-07 — open-source landscape synthesis

A broad current landscape review covered MCP, Windows internals, IPC, desktop/browser automation, code intelligence, documents, data, media, local inference, transfers, updating and evaluation.

Major outcomes:

- CsWin32 favored for permanent Win32 bindings;
- StreamJsonRpc favored for service/worker control;
- Nerdbank.Streams-style multiplexing favored for bulk worker streams;
- native Job Objects/ConPTY/explicit handle lists retained;
- event-driven UIA cache/events/Remote Operations favored over repeated whole-tree scraping;
- Windows.Graphics.Capture dirty-region observation favored over repeated full screenshots;
- raw generated CDP retained as the permanent browser primitive;
- Playwright .NET made optional rather than foundational;
- ripgrep + Tree-sitter/ast-grep + on-demand LSPs favored for code;
- MarkItDown/Open XML/ClosedXML/PdfPig/DuckDB favored for structured document/data capability;
- NAudio/whisper.cpp/OCR/ONNX/OpenCV favored as on-demand media/local-inference tools;
- BITS/VSS/Restart Manager/ReFS block cloning/Process Snapshotting/Virtual Disk APIs identified as high-value native Windows capability sources;
- VeloPack favored for staged atomic updating;
- a small real-task EyeBench favored for measurement;
- autonomous-agent frameworks, workflow engines, Docker/Kubernetes bases, permanent Node daemons, permanent local planners and competing MCP servers explicitly rejected as default architecture.

Detailed inventory: `OSS_LANDSCAPE.md`.

## 2026-08-07 — five-tool public MCP architecture adopted

Owner approved replacing the previous single model-facing `eye({ op, args })` design.

Canonical public facades are now:

```text
eye_inspect
eye_run
eye_change
eye_interact
eye_external
```

The five tools classify effects for model-facing schemas/metadata but share one internal operation registry/dispatcher and owner-authorized capability substrate.

`eye_run` remains the broad execution escape hatch.

The public contract is now versioned/frozen at:

```text
contracts/eye-mcp-v1.json
```

with the intended generation chain:

```text
contract
  -> MCP descriptors
  -> C# request/result types
  -> dispatcher registration
  -> capabilities
  -> documentation
  -> normalized tools/list snapshot test
```

`AGENTS.md` now explicitly forbids accidental public-contract edits without an owner-authorized contract revision.

## 2026-08-07 — canonical build order refreshed

Current implementation order:

```text
1. generated/frozen five-facade contract
2. CsWin32 interop foundation
3. StreamJsonRpc + multiplexed worker streams
4. finish active-user / Job Object / ConPTY execution
5. event-driven UIA + dirty-region capture
6. typed CDP + optional Playwright .NET
7. high-value Windows-native facilities as concrete needs appear
8. code/document/data/audio adapters from actual workloads
9. whisper/OCR/semantic retrieval only on demand
10. staged atomic updates + EyeBench
11. final prototype-to-v2 runtime cutover after independent end-to-end proof
```

## Current boundary

The architectural source of truth is now aligned around:

- one permanent LocalSystem service;
- five generated model-facing MCP facades;
- one internal dispatcher/operation registry;
- service-owned native SYSTEM/active-user/WSL execution;
- short-lived desktop workers;
- installed Chrome + raw typed CDP;
- Windows-native capabilities before reinvention;
- specialized external engines on demand;
- no duplicate agent runtime.

Next implementation work should follow `CUTOVER.md` and must preserve the contract-freeze rules in `AGENTS.md`.
