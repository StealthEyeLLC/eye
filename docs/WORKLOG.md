# WORKLOG.md

**Purpose:** Durable milestone/evidence log for Eye / StealthEye.  
**Baseline date:** 2026-08-07

This is not a command transcript. Historical experiments remain evidence; only decisions promoted into `EYE_CANON.md` / `EYE_DECISIONS.md` / `BUILD_BLUEPRINT.md` are canonical.

## 2026-08-06 — direct laptop-native Eye path

- Established direct ChatGPT -> OpenAI Secure MCP Tunnel -> laptop loopback -> Eye service operation.
- Confirmed the runtime can operate as a LocalSystem Windows service.
- Began separating the target laptop-native design from earlier HEC/VPS infrastructure.
- Preserved the older `se` repository/history as prototype reference rather than treating it as the new implementation base.

## 2026-08-07 — platform architecture preparation

Major platform direction established:

- dedicated Windows machine `STEALTHEYELLC` and interactive identity `StealthEye`;
- intended repository path `X:\Repos\eye`;
- intended physical ReFS Dev Drive `X:` on the internal NVMe;
- external `E:` reserved for bulk data/models/archives/artifacts;
- Ubuntu 24.04 WSL2 with systemd for Linux-native workloads;
- Docker removed from the target Eye architecture;
- HEC/VPS removed from the target Eye request path;
- OpenAI Secure MCP Tunnel retained as external transport only.

Historical platform details are evidence, not requirements to reproduce every old package or setting.

## 2026-08-07 — LocalSystem -> active-user execution proven

Disposable tests established:

- a genuine LocalSystem SCM service can identify the active session and launch its user using `WTSQueryUserToken` / `CreateEnvironmentBlock` / `CreateProcessAsUser`;
- stdout/stderr capture can work through inherited pipes;
- launched user children can be owned by a service Job Object;
- WSL can be invoked through the same active-user path;
- a permanent user-session helper is not fundamentally required.

## 2026-08-07 — desktop-worker path proven

Disposable testing established:

- a short-lived active-session worker can be launched from SYSTEM;
- Per-Monitor V2 DPI awareness can provide physical desktop coordinates;
- secure desktop/lock state is a real boundary for ordinary desktop capture/control.

## 2026-08-07 — browser/CDP path proven

Disposable testing established:

- installed Chrome can be launched under the active interactive user with a dedicated data directory;
- a LocalSystem service can control that browser over loopback Chrome DevTools Protocol.

## 2026-08-07 — native ConPTY path proven

Disposable testing established:

- native cross-session ConPTY works from the service-owned execution path;
- terminal output can be captured;
- the tested Windows build exports `ReleasePseudoConsole`.

## 2026-08-07 — machine secret persistence proven

A throwaway value was protected by LocalSystem using DPAPI-NG descriptor `LOCAL=user`.

Testing established:

- LocalSystem could protect/unprotect;
- the encrypted blob remained usable by LocalSystem across reboot;
- the interactive `StealthEye` account could not decrypt the same test blob;
- a direct `SID=S-1-5-18` protection descriptor did not succeed in the tested environment.

No real credential was used.

## 2026-08-07 — first v2 repository implementation

The clean `StealthEyeLLC/eye` repository moved from documentation-only into an early implementation:

- .NET Windows service host;
- loopback MCP endpoint;
- LocalSystem process execution;
- active-user process execution;
- Job Object ownership;
- run request/result models;
- initial operation dispatcher;
- first MCP wrapper.

A parallel v2 development build was exercised as LocalSystem and demonstrated active-user execution under the dedicated Windows identity.

Local follow-on work also prototyped native ConPTY integration. Those source changes remain implementation material to reconcile without reintroducing incidental EOL churn.

## 2026-08-07 — open-source landscape synthesis

A broad landscape review covered MCP, Windows internals, IPC, desktop/browser automation, code intelligence, documents, data, media, local inference, transfers, updating, and evaluation.

Major outcomes included:

- CsWin32 for permanent Win32 bindings;
- StreamJsonRpc for typed IPC;
- multiplexed worker streams for bulk data;
- native Job Objects/ConPTY/explicit handle lists;
- event-driven cached UIA/Remote Operations;
- Windows.Graphics.Capture dirty regions;
- generated raw CDP plus optional Playwright .NET;
- ripgrep + Tree-sitter/ast-grep + on-demand LSPs;
- MarkItDown/Open XML/ClosedXML/PdfPig/DuckDB;
- NAudio/whisper.cpp/OCR/ONNX/OpenCV on demand;
- BITS/VSS/Restart Manager/ReFS block cloning/Process Snapshotting/Virtual Disk APIs;
- staged updating and small real-task EyeBench;
- explicit rejection of embedded autonomous-agent frameworks, generic workflow engines, Docker/Kubernetes bases, permanent Node daemons, permanent local planners, and competing MCP servers.

Detailed inventory: `OSS_LANDSCAPE.md`.

## 2026-08-07 — five-effect-class contract experiment

The previous single public `eye({ op, args })` shape was retired in favor of effect-class facades. `contracts/eye-mcp-v1.json` recorded that intermediate five-tool public design and the repository added contract-freeze discipline.

That v1 artifact is now retained as immutable historical contract material.

## 2026-08-07 — LLM-native substrate research

Follow-on research identified the implementation constraints preventing Eye from becoming a continuous, recoverable machine interface:

- synchronous timeout-bound execution;
- whole-output buffering;
- no durable jobs or terminals;
- no event-driven waits;
- no artifact plane;
- no stable identities/generations/cursors;
- no state deltas;
- no compact batching;
- no current machine/capability manifest;
- no continuation UI/operator skill;
- no self-update fault boundary.

The research reframed Eye around four primitives:

```text
Observe
Act
Wait
Transfer
```

Durable jobs/ConPTY, native waits, artifacts, stable identities/deltas, and current machine manifests became core substrate requirements rather than optional adapters.

## 2026-08-07 — six-tool final public surface adopted

Owner approved the final model-facing surface:

```text
eye_inspect
eye_run
eye_change
eye_interact
eye_external
eye_live
```

Key decisions:

- the first five retain truthful effect classes;
- `eye_live` is a tiny UI-only tool that mounts the continuation/mission component and performs no machine operation;
- wait and transfer remain typed operation families beneath the facades rather than separate top-level tools;
- ordinary ChatGPT conversations remain the primary operating host;
- core Eye operation does not require Eye Live UI.

The canonical target contract is now `contracts/eye-mcp-v2.json`.

## 2026-08-07 — stable host / versioned engine fault boundary adopted

The final reliability architecture was explicitly promoted to canon.

There is one permanent LocalSystem SCM service containing a tiny stable host. Rapidly evolving capability logic runs in a **separate supervised versioned engine child process**, never as a DLL loaded into the host.

The stable host owns:

- MCP and public-contract validation;
- raw SYSTEM/user/WSL repair execution;
- Job Objects and native ConPTY;
- durable jobs/streams;
- artifacts;
- Trigger Broker queues;
- Mission Blackboard;
- stable identities/cursors;
- minimal Eye Live;
- engine protocol, A/B activation, and rollback;
- tiny authoritative state.

The replaceable engine owns UIA/WGC, CDP, file/code/document/data/media/provider adapters, higher-level capability logic, and version-matched worker behavior.

A broken engine must leave the ChatGPT control/repair path intact.

## 2026-08-07 — identity and delta model adopted

Canonical identity model:

```text
stable object ID + incarnation generation + observation cursor
```

This distinguishes mutable state from destruction/replacement and enables cursor-based/delta observation across processes, terminals, windows/UIA, browser objects, files, artifacts, and other stateful resources.

## 2026-08-07 — final build blueprint frozen

`docs/BUILD_BLUEPRINT.md` is now the canonical implementation blueprint.

Implementation sequence:

```text
1. Contract v2 and host/engine protocol
2. Stable host: raw execution, jobs, artifacts, state, identity model
3. Versioned engine process: supervision, handshake, A/B selector, rollback
4. Workers, streams, Trigger Broker, native waits, durable continuation
5. Eye Live and Eye Operator skill
6. Desktop and browser perception/control
7. Blackboard, Relay, context capture, multi-tab continuation
8. Files/storage/code/documents/data/media/transfer/provider adapters
9. Atomic final runtime cutover after independent recovery/reboot/end-to-end proof
```

Architecture expansion should now be exceptional. New capability research should normally be placed beneath this blueprint rather than creating another architectural layer.

## Current boundary

The canonical source set is aligned around:

- one permanent LocalSystem stable host;
- a separate supervised versioned capability engine;
- six frozen model-facing tools;
- host-owned durable jobs/ConPTY/artifacts/triggers/state;
- stable identities/incarnations/cursors and delta observation;
- short-lived version-matched active-session workers;
- installed Chrome + typed raw CDP;
- optional Eye Live UI and Eye Operator skill;
- Windows-native capabilities before reinvention;
- specialized external engines on demand;
- no duplicate agent brain or generic workflow runtime.

Implementation should follow `BUILD_BLUEPRINT.md` and `CUTOVER.md` and preserve `AGENTS.md` guardrails.
