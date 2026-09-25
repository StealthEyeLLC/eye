# CUTOVER.md

**Status:** Canonical implementation and runtime cutover checklist  
**Machine:** `STEALTHEYELLC`  
**Baseline date:** 2026-08-07

This checklist implements `docs/BUILD_BLUEPRINT.md`. It intentionally avoids preserving obsolete migration procedure as the active plan.

## Phase 0 ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã¢â‚¬Â ÃƒÂ¢Ã¢â€šÂ¬Ã¢â€žÂ¢ÃƒÆ’Ã†â€™ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡Ãƒâ€šÃ‚Â¬ÃƒÆ’Ã¢â‚¬Â¦Ãƒâ€šÃ‚Â¡ÃƒÆ’Ã†â€™ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¬ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â‚¬Å¡Ã‚Â¬Ãƒâ€¦Ã‚Â¡ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¬ÃƒÆ’Ã†â€™ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â machine foundation

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

## Phase 1 ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã¢â‚¬Â ÃƒÂ¢Ã¢â€šÂ¬Ã¢â€žÂ¢ÃƒÆ’Ã†â€™ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡Ãƒâ€šÃ‚Â¬ÃƒÆ’Ã¢â‚¬Â¦Ãƒâ€šÃ‚Â¡ÃƒÆ’Ã†â€™ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¬ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â‚¬Å¡Ã‚Â¬Ãƒâ€¦Ã‚Â¡ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¬ÃƒÆ’Ã†â€™ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â contract v2 and host/engine protocol

Canonical target tools:

```text
eye_inspect
eye_run
eye_change
eye_interact
eye_external
eye_live
```

- [x] `contracts/eye-mcp-v1.json` remains immutable historical material.
- [x] `contracts/eye-mcp-v2.json` is the canonical target contract.
- [x] exact descriptors/DTOs/host validation/registration/capabilities/server instructions/docs are generated from the contract source.
- [x] exact output schemas are published.
- [x] normalized `tools/list` snapshot test exists.
- [x] ordinary implementation changes fail if they accidentally mutate the public contract.
- [x] `eye_live` is UI-only; helper tools are app-only and absent from model selection.
- [x] wait/transfer are operation families, not extra top-level tools.
- [x] host/engine protocol is versioned separately from MCP contract.
- [x] engine handshake includes protocol version, build version, public contract hash, supported operation IDs, and worker protocol version.
- [x] incompatible engine cannot become active.

Phase 1 activation gate is met. The canonical contract is generated but remains non-live until the runtime cutover gate is executed and verified.

## Phase 2 ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã¢â‚¬Â ÃƒÂ¢Ã¢â€šÂ¬Ã¢â€žÂ¢ÃƒÆ’Ã†â€™ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡Ãƒâ€šÃ‚Â¬ÃƒÆ’Ã¢â‚¬Â¦Ãƒâ€šÃ‚Â¡ÃƒÆ’Ã†â€™ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¬ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â‚¬Å¡Ã‚Â¬Ãƒâ€¦Ã‚Â¡ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¬ÃƒÆ’Ã†â€™ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â stable host core

- [x] one LocalSystem SCM service owns the stable host.
- [x] stable host serves loopback MCP.
- [x] host provides raw SYSTEM execution.
- [x] host provides active-user execution through `WTSQueryUserToken` / `CreateEnvironmentBlock` / `CreateProcessAsUser`.
- [x] host provides WSL execution through the active-user path.
- [x] CsWin32-generated bindings/SafeHandles replace suitable handwritten interop.
- [x] explicit inherited-handle lists are used.
- [x] Job Objects own launched process trees.
- [x] host owns native ConPTY handles/lifecycle.
- [x] cancellation consistently terminates owned process trees.
- [x] short operations can complete synchronously.
- [x] long operations automatically become durable jobs.
- [x] output is cursor-based and spooled rather than retained indefinitely in RAM.
- [x] tiny authoritative SQLite state is stored under SYSTEM-owned `C:\ProgramData\StealthEye` state.

### Phase 2 verification evidence

On 2026-09-24 the accepted host was installed as the single Auto-start StealthEye LocalSystem SCM service and independently verified to expose exactly one listener at 127.0.0.1:37931. Live MCP calls proved inline SYSTEM execution as NT AUTHORITY\SYSTEM, inline active-user execution as STEALTHEYELLC\StealthEye, automatic WSL promotion to a durable job, successful active-user WSL completion, and cursor-backed spool output (root::Linux). The accepted test suite also proves explicit inherited-handle isolation, host-owned native ConPTY lifecycle, and Job Object cancellation of both a job root process and its spawned descendant.

Phase 2 is complete. Suitable Job Object interop now uses CsWin32-generated bindings/SafeHandles while the remaining specialized process/session declarations stay narrowly handwritten where they materially simplify the implementation.
## Phase 3 ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã¢â‚¬Â ÃƒÂ¢Ã¢â€šÂ¬Ã¢â€žÂ¢ÃƒÆ’Ã†â€™ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡Ãƒâ€šÃ‚Â¬ÃƒÆ’Ã¢â‚¬Â¦Ãƒâ€šÃ‚Â¡ÃƒÆ’Ã†â€™ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¬ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â‚¬Å¡Ã‚Â¬Ãƒâ€¦Ã‚Â¡ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¬ÃƒÆ’Ã†â€™ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â artifacts and identity model

- [x] host artifact registry exists.
- [x] artifact metadata includes stable ID, kind, MIME type, size, hash/name as applicable, storage tier, and provenance.
- [x] artifact preview/range-read/export/delete/diff paths exist where applicable.
- [x] large results return artifact + useful excerpt.
- [x] ChatGPT top-level file inputs can be imported directly as artifacts where supported.
- [x] stable identity model is implemented as stable ID + incarnation + observation cursor.
- [x] PID/HWND/path reuse cannot silently alias a replaced object.
- [x] stdout/stderr/terminal/file/UI/browser readers support cursor/delta semantics.

### Phase 3 verification evidence

On 2026-09-24 the accepted artifact registry was verified to persist stable artifact IDs with incarnation, kind, MIME type, byte size, SHA-256, name, storage tier, provenance, creation time, and private backing path. Public artifact operations cover info, bounded text preview, range reads, diff, export, delete, and v2.2 artifact.import for machine-visible/materialized file inputs without exposing the private backing path. Artifact import, generated contract artifacts, and the live loopback MCP surface are covered by the accepted test suite.

The identity stores independently prove stable IDs plus incarnation and observation cursors for desktop windows, UIA elements, browser targets, jobs, and artifacts. Reused HWNDs with a different process generation, disappeared/reappeared browser targets, browser type replacement, and UIA elements under a new window incarnation all advance incarnation instead of silently aliasing the old object. Job stdout/stderr, terminal attachment, artifact range reads, and UI/browser observations expose bounded cursor/range progression.

Phase 3 is complete. Fast completed run operations whose combined stdout/stderr exceeds the inline limit now promote their existing spool streams into durable artifacts and return bounded inline excerpts, truncation flags, artifact IDs, and the original job/exit/context metadata. Small fast output remains inline and slow work remains a durable job reference.
## Phase 4 ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã¢â‚¬Â ÃƒÂ¢Ã¢â€šÂ¬Ã¢â€žÂ¢ÃƒÆ’Ã†â€™ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡Ãƒâ€šÃ‚Â¬ÃƒÆ’Ã¢â‚¬Â¦Ãƒâ€šÃ‚Â¡ÃƒÆ’Ã†â€™ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¬ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â‚¬Å¡Ã‚Â¬Ãƒâ€¦Ã‚Â¡ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¬ÃƒÆ’Ã†â€™ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â supervised versioned engine

- [x] capability engine is a separate child process, never a DLL inside stable host.
- [x] active and previous engine versions live side by side.
- [x] host supervises engine health/crash behavior.
- [x] staged engine starts and handshakes before activation.
- [x] activation routing switches atomically.
- [x] previous engine remains available for rollback.
- [x] handshake failure cannot replace the working engine.
- [x] crash-loop behavior triggers rollback.
- [x] host-owned jobs/terminals/artifacts/triggers/mission state survive engine replacement/crash.
- [x] degraded mode without an engine retains status, raw SYSTEM/user/WSL repair execution, jobs/terminals, artifact reads, mission/trigger state, and rollback controls.

### Phase 4 verification evidence

On 2026-09-24 the supervised engine layer was exercised with real child processes and side-by-side staged versions. A and B handshake and ping before activation; selector state persists through an atomic temporary-file replacement; restart and rollback preserve the previous staged version; two rapid B crashes restart once and then roll back to A.

An incompatible v2.3 contract hash was injected only through the internal engine-start test seam while using the real engine executable. Version B was rejected by the real handshake validator before installation, while the healthy A process, active selector, and PID remained unchanged.

Host-ownership integration coverage creates a completed job, a live ConPTY terminal, an artifact, mission blackboard, and pending trigger before A-to-B replacement. After B restart and crash-loop rollback, the live terminal still accepts input, and all durable host-owned records remain readable with the same stable identities. Degraded-mode coverage starts with a missing active engine and proves engine status, raw SYSTEM/user/WSL repair execution, artifact reads, mission/trigger state, and rollback to the staged previous engine remain functional.

The active-user scheduled fallback was also hardened during this phase: the scheduled wrapper now joins the host Job Object before a gate allows it to spawn the requested command. This removes the fast-process PID race where a short user command could exit before ownership was established.

Phase 4 is complete.
## Phase 5 ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã¢â‚¬Â ÃƒÂ¢Ã¢â€šÂ¬Ã¢â€žÂ¢ÃƒÆ’Ã†â€™ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡Ãƒâ€šÃ‚Â¬ÃƒÆ’Ã¢â‚¬Â¦Ãƒâ€šÃ‚Â¡ÃƒÆ’Ã†â€™ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¬ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â‚¬Å¡Ã‚Â¬Ãƒâ€¦Ã‚Â¡ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¬ÃƒÆ’Ã†â€™ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â workers, streams, Trigger Broker, waits

- [x] StreamJsonRpc named-pipe control path works for host/engine/worker interactions.
- [x] multiplexed binary plane exposes and verifies stdout/stderr/VT/image/audio/file channels.
- [x] host launches short-lived active-session workers on demand.
- [x] host owns worker lifetime, IPC, identities, and cleanup.
- [x] worker behavior is version-matched to the active engine.
- [x] worker crash does not require restarting the stable host.
- [x] Trigger Broker durable queues are host-owned.
- [x] UIA/CDP worker watchers can feed host-owned trigger queues.
- [x] initial event-driven waits cover process exit, file events, time, UIA changes, and browser navigation; service/port/session sources remain future expansion.
- [x] wait sources expand with desktop/browser implementation rather than through polling loops.

### Phase 5 verification evidence

On 2026-09-24 the host/engine/worker control plane was verified over StreamJsonRpc named pipes. The worker bulk pipe uses Nerdbank.Streams and worker protocol 1.1 exposes six canonical binary channels: stdout, stderr, terminal.vt, image, audio, and file. A real active-session worker round-tripped a 16 KiB non-text payload byte-for-byte over every channel while terminal VT continues to use the terminal.vt channel in production.

Worker ownership is host-controlled. Active-session workers are launched on demand, version-resolved from the selected engine, placed under host-owned process control, replaced automatically after a crash, and destroyed after the idle lease window. Tests prove a killed cached worker is replaced without changing the stable host PID and that an idle worker does not remain resident.

The Trigger Broker persists registrations and ordered event queues in host-owned SQLite state. Pending time/file triggers reattach after broker restart, process exit uses process-incarnation-aware native exit waiting, file creation uses FileSystemWatcher, UIA changes are fed from the active-session UIA watcher, and browser navigation is now fed from a real CDP Page.frameNavigated event into the same durable queue/cursor model. These sources are event-driven; broader service, port, session, Event Log, device, power/network, and performance sources are future extensions and must not be approximated with polling loops.

Phase 5 is complete.
## Phase 6 ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã¢â‚¬Â ÃƒÂ¢Ã¢â€šÂ¬Ã¢â€žÂ¢ÃƒÆ’Ã†â€™ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡Ãƒâ€šÃ‚Â¬ÃƒÆ’Ã¢â‚¬Â¦Ãƒâ€šÃ‚Â¡ÃƒÆ’Ã†â€™ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¬ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â‚¬Å¡Ã‚Â¬Ãƒâ€¦Ã‚Â¡ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¬ÃƒÆ’Ã†â€™ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â Eye Live and operator guidance

- [x] eye_live returns an MCP Apps UI resource only when continuation/supervision is useful.
- [x] core Eye operation does not depend on UI being rendered.
- [x] Eye Live can display mission, jobs/terminals, live output, triggers, artifacts, relay state, and compact machine/context status.
- [x] Eye Live can call app-only helpers without exposing them to model tool selection.
- [x] UI follow-up messaging works through supported bridge behavior.
- [x] Eye Operator skill exists and teaches modality hierarchy, jobs, waits, artifacts, handles/cursors, and contract discipline.
- [x] MCP server initialization instructions provide compact routing rules with self-contained first 512 characters.

### Phase 6 verification evidence

On 2026-09-24 Eye Live was verified as an optional MCP Apps component mounted only through the dedicated eye_live UI tool. The ordinary five capability facades plus eye_live remain the six model-visible tools; eye_live_refresh and eye_live_action are served with app-only UI visibility and are reserved for the mounted app. The live loopback MCP surface test verifies both the eight-tool served surface and the six-tool model-visible boundary.

Eye Live now renders compact machine/context status, engine recovery state, recent missions, relay messages, jobs/terminals with bounded stdout/stderr tails, triggers, and artifacts. Its bounded app-only action helper permits engine restart/rollback and job cancellation without exposing those helpers to model selection. Its follow-up composer uses the MCP Apps ui/message bridge so the user can hand the displayed state back to ChatGPT without turning the component into an autonomous agent.

Core Eye operation remains independent of UI rendering. The Eye Live snapshot is host-owned and remains useful when the capability engine is unavailable; ordinary MCP tools and the full test suite do not require the component to be mounted. The canonical Eye Operator source in docs/EYE_OPERATOR_SKILL.md is tested for modality hierarchy, durable jobs, native waits, artifacts, stable IDs/incarnations/cursors, UIA/CDP guidance, optional Eye Live doctrine, and contract discipline. Server initialization instructions remain contract-owned and the first 512 characters are tested to carry the essential ChatGPT/typed-operation/job/artifact routing rules.

Phase 6 is complete.
## Phase 7 ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã¢â‚¬Â ÃƒÂ¢Ã¢â€šÂ¬Ã¢â€žÂ¢ÃƒÆ’Ã†â€™ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡Ãƒâ€šÃ‚Â¬ÃƒÆ’Ã¢â‚¬Â¦Ãƒâ€šÃ‚Â¡ÃƒÆ’Ã†â€™ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¬ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â‚¬Å¡Ã‚Â¬Ãƒâ€¦Ã‚Â¡ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¬ÃƒÆ’Ã†â€™ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â desktop and browser capability engine

### Desktop

- [x] HWND/process/window inventory exists.
- [x] UIA uses cache requests/events and Remote Operations where useful.
- [x] stable UIA/window identities and deltas work.
- [x] Windows.Graphics.Capture provides efficient window/screen capture.
- [x] dirty-region observation is used where practical.
- [x] Per-Monitor V2 DPI awareness is established before coordinate-sensitive work.
- [x] OCR/visual grounding remains fallback rather than primary state representation.
- [x] secure desktop/lock state is reported accurately.

### Browser

- [x] installed Chrome launches under active user.
- [x] dedicated Eye profile/data directory is used.
- [x] CDP is loopback-bound where appropriate.
- [x] typed CDP bindings are generated.
- [x] stable target/frame/node identities and event waits work.
- [x] downloads become artifacts.
- [x] browser remains fully usable without Playwright installed.
- [x] optional Playwright .NET path is available only where it materially improves behavior.
- [x] no permanent Node daemon or bundled browser fleet exists.

### Phase 7 verification evidence

On 2026-09-24 the desktop/browser capability layer was verified through the version-matched worker protocol 1.2 and MCP contract v2.6. The desktop worker inventories HWND/process/window state, establishes Per-Monitor V2 awareness before observation, uses UI Automation cache requests plus event watchers, persists stable window/UIA identities with cursors, captures through Windows.Graphics.Capture, requests dirty regions, and invokes Windows OCR only when text recognition is explicitly requested. Secure/locked-session reporting now combines WTS session state with the actual input-desktop name/accessibility, so Default versus secure desktop state is surfaced instead of inferred from missing windows.

UIA Remote Operations remain an optional optimization point rather than a forced dependency; the accepted current operations are already covered by cached reads/actions/events, so no extra Remote Operations layer was added without a measured case. The Phase 7 invariant test locks this policy together with the existing modality hierarchy.

The browser worker launches the installed Google Chrome under the active user with a dedicated Eye profile and an ephemeral CDP port explicitly bound to 127.0.0.1. Raw CDP remains the permanent primitive. The bounded typed CDP subset is now generated deterministically from contracts/cdp-bindings-subset.json by tools/generate-cdp-bindings.ps1, and a reproducibility test regenerates the file and compares it byte-for-byte.

Host-owned browser state now persists stable public target/frame/node identities and observation cursors while keeping raw CDP target/frame/backend-node identities private. Browser navigation event waits continue through the host Trigger Broker. A real loopback HTTP attachment was downloaded by Chrome and promoted into the canonical ArtifactStore; the public result exposes artifact ID/incarnation/name/size/SHA-256/MIME/storage tier and no browser filesystem path or raw CDP identity.

Playwright is not installed or required in v1 because the accepted raw-CDP path already covers the current browser behaviors. The optional Playwright .NET accelerator remains permitted only for a measured locator/wait/download/trace advantage. No Eye project references Playwright, Puppeteer, Selenium, Node automation, a bundled Chromium fleet, or a permanent browser daemon.

The Phase 7 focused acceptance passed 22/22 tests, including real active-session browser/UIA/WGC paths, generated-binding reproducibility, stable DOM handles, secure desktop/session state, and artifact-backed downloads.

The authoritative repository acceptance then passed 128/128 tests with zero failed or skipped cases after clean host, worker, engine, and test builds.

Phase 7 is complete.
## Phase 8 ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã¢â‚¬Â ÃƒÂ¢Ã¢â€šÂ¬Ã¢â€žÂ¢ÃƒÆ’Ã†â€™ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡Ãƒâ€šÃ‚Â¬ÃƒÆ’Ã¢â‚¬Â¦Ãƒâ€šÃ‚Â¡ÃƒÆ’Ã†â€™ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¬ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â‚¬Å¡Ã‚Â¬Ãƒâ€¦Ã‚Â¡ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¬ÃƒÆ’Ã†â€™ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â Blackboard, Relay, context capture, and adapters

### Blackboard / Relay

- [x] fixed compact Blackboard schema stores objective, facts/decisions, jobs/triggers, artifacts, unresolved questions, next action, relay messages.
- [x] no transcript archive, task taxonomy, receipt system, generic DAG/workflow model, or scheduler language is introduced.
- [x] Eye Live can associate available chats with missions/optional roles and relay compact messages.
- [x] absent/closed chats preserve relay queues without claiming spontaneous MCP wakeup.

### Context capture

- [x] one-shot context helper can capture available active-app/window, selection, clipboard, UIA, screenshot, Chrome context, and path data.
- [x] Explorer/Chrome handoff can reuse the same context pipeline where useful.

### Capability adapters

Add based on real tasks, not completeness theater:

- [x] machine/session/volume/software/operation manifests.
- [x] BITS/VSS/Restart Manager/ReFS clone/CopyFile2/Process Snapshotting/Virtual Disk capabilities are represented as measured native capabilities and remain callable through deterministic host execution.
- [x] ripgrep + Tree-sitter/ast-grep + on-demand language servers are capability-manifested; absent optional tools stay non-resident until a real task needs them.
- [x] MarkItDown/PdfPig/Open XML/ClosedXML and Docling are registered as optional on-demand document adapters; no permanent install is required without a workload.
- [x] DuckDB is represented as an optional analytical adapter and reports actual installed availability.
- [x] NAudio/whisper.cpp are represented as optional audio adapters and remain non-resident unless a workload requires them.
- [x] OCR/ONNX/OpenCV are on-demand vision adapters; Windows OCR remains the built-in fallback.
- [x] deterministic adapter truth exists for Git/GitHub CLI, PowerShell/WSL, winget, FFmpeg, services/Task Scheduler, and other actually installed software.
- [x] resource-aware routing can inspect memory, AC/battery state, volumes/storage format/free space, installed software, and capability availability; no autonomous scheduler is introduced.

### Phase 8 verification evidence

The Blackboard remains a fixed compact mission record: objective, facts/decisions, jobs/triggers, artifacts, unresolved questions, next action, and bounded relay messages. Chat/role association is intentionally a separate tiny durable SQLite index so the Blackboard does not become a transcript/task/workflow database. Relay messages persist independently of chat availability; marking or removing a chat association does not delete queued relay state and does not create a spontaneous wakeup mechanism.

One-shot context capture now combines the existing desktop/UIA/screenshot/browser packet with bounded active-session context from the existing short-lived worker: clipboard text, focused UIA selection/value, foreground executable path, Explorer folder, and selected filesystem paths. The packet is still imported into the artifact plane and temporary plaintext context files are removed.

The six-tool MCP surface now exposes typed machine/session/volume/software/operation manifests plus mission/relay/context operations without adding top-level tools. Native Windows facilities and installed CLIs are measured rather than assumed. Optional heavy document/data/audio/vision adapters are explicitly represented as optional/on-demand and are not installed merely for checklist completeness. eye_run remains the deterministic universal escape hatch for legitimate local capability that has not yet earned a permanent typed adapter.

Acceptance evidence on this tree:
- worker, engine, host, and test builds: 0 warnings / 0 errors;
- v2.7 contract and served-surface gate: green;
- Phase 8 behavioral/integration gates: green, including active-session context and six-tool dispatcher routing;
- authoritative permanent suite: 132/132 passed, 0 failed, 0 skipped;
- residue audit: 0 session tasks, 0 owned temporary session/output files, 0 worker processes, 0 Chrome test profiles;
- frozen v2.7 contract SHA-256: 607e811d7273bd91f382f6270969157c152d46d2abc1a01817f3d757ca48b82d.

Phase 8 is complete.
## Phase 9 ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã¢â‚¬Â ÃƒÂ¢Ã¢â€šÂ¬Ã¢â€žÂ¢ÃƒÆ’Ã†â€™ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡Ãƒâ€šÃ‚Â¬ÃƒÆ’Ã¢â‚¬Â¦Ãƒâ€šÃ‚Â¡ÃƒÆ’Ã†â€™ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¬ÃƒÆ’Ã†â€™Ãƒâ€ Ã¢â‚¬â„¢ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â‚¬Å¡Ã‚Â¬Ãƒâ€¦Ã‚Â¡ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â¬ÃƒÆ’Ã†â€™ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â final runtime cutover

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
