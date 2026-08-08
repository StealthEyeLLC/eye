# CUTOVER.md

**Status:** Canonical implementation and runtime cutover checklist  
**Machine:** `STEALTHEYELLC`  
**Baseline date:** 2026-08-07

This checklist implements `docs/BUILD_BLUEPRINT.md`. It intentionally avoids preserving obsolete migration procedure as the active plan.

## Phase 0 — machine foundation

Before runtime ownership changes:

- [ ] Windows boots normally through repeated reboots.
- [ ] Device-encryption/BitLocker state is explicitly known and matches the owner's chosen posture.
- [ ] `C:` is healthy as Windows/application/system-state storage.
- [ ] `X:` is provisioned as the intended physical trusted ReFS Dev Drive, approximately 300 GiB.
- [ ] `E:` bulk/archive storage is visible and excluded from destructive provisioning work.
- [ ] `X:\Repos\eye` is the clean active checkout.
- [ ] required .NET/Git/build tooling is healthy.
- [ ] WSL2 Ubuntu 24.04 baseline is healthy with systemd.
- [ ] NVIDIA/CUDA stack is healthy where required.
- [ ] Windows login/account/autologon configuration is left alone unless explicitly changed by the owner.

## Phase 1 — contract v2 and host/engine protocol

Canonical target tools:

```text
eye_inspect
eye_run
eye_change
eye_interact
eye_external
eye_live
```

- [ ] `contracts/eye-mcp-v1.json` remains immutable historical material.
- [ ] `contracts/eye-mcp-v2.json` is the canonical target contract.
- [ ] exact descriptors/DTOs/host validation/registration/capabilities/server instructions/docs are generated from the contract source.
- [ ] exact output schemas are published.
- [ ] normalized `tools/list` snapshot test exists.
- [ ] ordinary implementation changes fail if they accidentally mutate the public contract.
- [ ] `eye_live` is UI-only; helper tools are app-only and absent from model selection.
- [ ] wait/transfer are operation families, not extra top-level tools.
- [ ] host/engine protocol is versioned separately from MCP contract.
- [ ] engine handshake includes protocol version, build version, public contract hash, supported operation IDs, and worker protocol version.
- [ ] incompatible engine cannot become active.

Do not advertise v2 as live until its activation gate is met.

## Phase 2 — stable host core

- [ ] one LocalSystem SCM service owns the stable host.
- [ ] stable host serves loopback MCP.
- [ ] host provides raw SYSTEM execution.
- [ ] host provides active-user execution through `WTSQueryUserToken` / `CreateEnvironmentBlock` / `CreateProcessAsUser`.
- [ ] host provides WSL execution through the active-user path.
- [ ] CsWin32-generated bindings/SafeHandles replace suitable handwritten interop.
- [ ] explicit inherited-handle lists are used.
- [ ] Job Objects own launched process trees.
- [ ] host owns native ConPTY handles/lifecycle.
- [ ] cancellation consistently terminates owned process trees.
- [ ] short operations can complete synchronously.
- [ ] long operations automatically become durable jobs.
- [ ] output is cursor-based and spooled rather than retained indefinitely in RAM.
- [ ] tiny authoritative SQLite state is stored under SYSTEM-owned `C:\ProgramData\StealthEye` state.

## Phase 3 — artifacts and identity model

- [ ] host artifact registry exists.
- [ ] artifact metadata includes stable ID, kind, MIME type, size, hash/name as applicable, storage tier, and provenance.
- [ ] artifact preview/range-read/export/delete/diff paths exist where applicable.
- [ ] large results return artifact + useful excerpt.
- [ ] ChatGPT top-level file inputs can be imported directly as artifacts where supported.
- [ ] stable identity model is implemented as `stable ID + incarnation + observation cursor`.
- [ ] PID/HWND/path reuse cannot silently alias a replaced object.
- [ ] stdout/stderr/terminal/file/UI/browser readers support cursor/delta semantics.

## Phase 4 — supervised versioned engine

- [ ] capability engine is a separate child process, never a DLL inside stable host.
- [ ] active and previous engine versions live side by side.
- [ ] host supervises engine health/crash behavior.
- [ ] staged engine starts and handshakes before activation.
- [ ] activation routing switches atomically.
- [ ] previous engine remains available for rollback.
- [ ] handshake failure cannot replace the working engine.
- [ ] crash-loop behavior triggers rollback.
- [ ] host-owned jobs/terminals/artifacts/triggers/mission state survive engine replacement/crash.
- [ ] degraded mode without an engine retains status, raw SYSTEM/user/WSL repair execution, jobs/terminals, artifact reads, mission/trigger state, and rollback controls.

## Phase 5 — workers, streams, Trigger Broker, waits

- [ ] StreamJsonRpc named-pipe control path works for host/engine/worker interactions.
- [ ] multiplexed binary streams handle stdout/stderr/VT/image/audio/file traffic.
- [ ] host launches short-lived active-session workers on demand.
- [ ] host owns worker lifetime, IPC, identities, and cleanup.
- [ ] worker behavior is version-matched to the active engine.
- [ ] worker crash does not require restarting the stable host.
- [ ] Trigger Broker durable queues are host-owned.
- [ ] engine UIA/CDP watchers can feed host queues.
- [ ] native waits exist for initial high-value conditions such as job/process exit, file events, service/port/session state.
- [ ] wait sources expand with desktop/browser implementation rather than through polling loops.

## Phase 6 — Eye Live and operator guidance

- [ ] `eye_live` returns an MCP Apps UI resource only when continuation/supervision is useful.
- [ ] core Eye operation does not depend on UI being rendered.
- [ ] Eye Live can display mission, jobs/terminals, live output, triggers, artifacts, relay state, and compact machine/context status.
- [ ] Eye Live can call app-only helpers without exposing them to model tool selection.
- [ ] UI follow-up messaging works through supported bridge behavior.
- [ ] Eye Operator skill exists and teaches modality hierarchy, jobs, waits, artifacts, handles/cursors, and contract discipline.
- [ ] MCP server initialization instructions provide compact routing rules with self-contained first 512 characters.

## Phase 7 — desktop and browser capability engine

### Desktop

- [ ] HWND/process/window inventory exists.
- [ ] UIA uses cache requests/events and Remote Operations where useful.
- [ ] stable UIA/window identities and deltas work.
- [ ] Windows.Graphics.Capture provides efficient window/screen capture.
- [ ] dirty-region observation is used where practical.
- [ ] Per-Monitor V2 DPI awareness is established before coordinate-sensitive work.
- [ ] OCR/visual grounding remains fallback rather than primary state representation.
- [ ] secure desktop/lock state is reported accurately.

### Browser

- [ ] installed Chrome launches under active user.
- [ ] dedicated Eye profile/data directory is used.
- [ ] CDP is loopback-bound where appropriate.
- [ ] typed CDP bindings are generated.
- [ ] stable target/frame/node identities and event waits work.
- [ ] downloads become artifacts.
- [ ] browser remains fully usable without Playwright installed.
- [ ] optional Playwright .NET path is available only where it materially improves behavior.
- [ ] no permanent Node daemon or bundled browser fleet exists.

## Phase 8 — Blackboard, Relay, context capture, and adapters

### Blackboard / Relay

- [ ] fixed compact Blackboard schema stores objective, facts/decisions, jobs/triggers, artifacts, unresolved questions, next action, relay messages.
- [ ] no transcript archive, task taxonomy, receipt system, generic DAG/workflow model, or scheduler language is introduced.
- [ ] Eye Live can associate available chats with missions/optional roles and relay compact messages.
- [ ] absent/closed chats preserve relay queues without claiming spontaneous MCP wakeup.

### Context capture

- [ ] one-shot context helper can capture available active-app/window, selection, clipboard, UIA, screenshot, Chrome context, and path data.
- [ ] Explorer/Chrome handoff can reuse the same context pipeline where useful.

### Capability adapters

Add based on real tasks, not completeness theater:

- [ ] machine/session/volume/software/operation manifests.
- [ ] BITS/VSS/Restart Manager/ReFS clone/CopyFile2/Process Snapshotting/Virtual Disk capabilities.
- [ ] ripgrep + Tree-sitter/ast-grep + on-demand language servers.
- [ ] MarkItDown/PdfPig/Open XML/ClosedXML; Docling only when needed.
- [ ] DuckDB.
- [ ] NAudio/whisper.cpp.
- [ ] OCR/ONNX/OpenCV on demand.
- [ ] deterministic adapters for Git/GitHub CLI, PowerShell/WSL, winget, FFmpeg, services/Task Scheduler, and other actually installed software.
- [ ] resource-aware execution considers GPU memory/thermals/power/storage tier where useful.

## Phase 9 — final runtime cutover

Only cut over when the new runtime independently operates and repairs the machine.

- [ ] stable host survives cold reboot.
- [ ] Secure MCP Tunnel reconnects to the host.
- [ ] v2 generated public contract is the served surface.
- [ ] SYSTEM/user/WSL execution works.
- [ ] durable jobs/terminal continuity across MCP/tunnel disconnects works.
- [ ] artifacts and cursor reads work.
- [ ] engine activation/rollback works.
- [ ] deliberate engine failure leaves degraded-mode repair path intact.
- [ ] desktop worker can be created/destroyed repeatedly.
- [ ] desktop observation/control works.
- [ ] browser/CDP works.
- [ ] Eye Live failure does not block ordinary MCP operation.
- [ ] structured errors/cancellation behave correctly.
- [ ] machine-secret persistence works.
- [ ] switch production tunnel target only after these checks pass.
- [ ] observe normal operation before removing compatibility mechanisms.
- [ ] remove obsolete prototype runtime/session-helper residue.
- [ ] cold reboot and prove final architecture end to end.

## Final success state

```text
ChatGPT
   |
OpenAI Secure MCP Tunnel
   |
Stable Eye Host (one LocalSystem SCM service)
   |
   +-- six generated MCP tools
   +-- raw repair execution
   +-- host-owned jobs / ConPTY / artifacts / triggers / state
   +-- supervised active versioned capability engine
   |      +-- desktop/UIA/WGC
   |      +-- Chrome/CDP
   |      +-- file/code/doc/data/media/provider adapters
   |
   +-- on-demand active-session workers
   +-- WSL
   +-- external/on-demand specialized tools
```

No required VPS/HEC dependency.  
No Docker/Kubernetes base.  
No Codex/Work/paid-API controller dependency.  
No second autonomous agent brain.  
No permanent user/session or Node automation daemon.  
No competing MCP servers.  
No generic workflow engine.
