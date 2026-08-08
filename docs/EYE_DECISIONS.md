# EYE_DECISIONS.md

**Status:** Decision ledger and architecture-freeze guardrail  
**Baseline date:** 2026-08-07

This document prevents exploratory findings from silently becoming canonical Eye design.

Decision states:

- **Canonical** — approved direction; preserve unless explicitly changed by the owner.
- **Favored / provisional** — strong preference, implementation detail may still change.
- **Open** — intentionally unresolved.

## 1. Canonical decisions

### Project identity

- Product: **StealthEye**
- Project: **Eye**
- Repository: **`StealthEyeLLC/eye`**
- Local repository target: **`X:\Repos\eye`**
- Executable / CLI: **`eye.exe` / `eye`**
- Windows service: **`StealthEye`**
- Core implementation: **C# / .NET on Windows**

### Final public MCP surface

The canonical v2 model-facing surface is exactly:

```text
eye_inspect
eye_run
eye_change
eye_interact
eye_external
eye_live
```

The first five are effect-class capability facades. `eye_live` is UI-only and performs no machine operation itself.

`wait` and `transfer` are complete typed operation families beneath the existing facades, not additional top-level tools.

The five effect classes improve schema/tool/effect accuracy but are not a privilege hierarchy. `eye_run` remains the broad local execution escape hatch.

### Contract versioning

`contracts/eye-mcp-v1.json` is historical and immutable.

The canonical target contract is:

```text
contracts/eye-mcp-v2.json
```

The six v2 tool names are frozen. Public operation/schema changes require an explicit owner-authorized contract revision. Routine implementation work must not alter the public contract implicitly.

Generate descriptors, C# DTOs, host validation, operation/facade registration, capabilities, server instructions, documentation, and normalized `tools/list` snapshots from the contract source.

### Stable result and schema posture

Use shallow, exact, closed operation variants with boring JSON Schema constructs: objects, primitives, arrays, enums, bounds, required properties, and `additionalProperties: false`.

Prefer omitted optional values over `null`. Publish exact output schemas. Do not expose raw exception/stack details to the model. Large results become artifacts plus useful excerpts.

### Four machine primitives

Eye is built around:

```text
Observe
Act
Wait
Transfer
```

New capabilities should compose these primitives rather than add architectural layers.

### One permanent SCM service

There is exactly one permanent LocalSystem Windows SCM service.

It contains a tiny stable host that supervises a **separate versioned capability-engine child process**.

The engine must not be loaded as a DLL into the stable service because native/COM/media/GPU/dependency faults in feature code must not be able to directly crash the host.

No second Windows service, permanent user daemon, permanent Node automation service, or competing MCP server is part of final Eye.

### Stable host ownership

The stable host owns:

- loopback MCP endpoint and six public descriptors;
- public-contract validation/routing;
- server instructions;
- raw SYSTEM/user/WSL execution and minimal repair path;
- Job Objects and native ConPTY ownership;
- durable jobs/output streams;
- artifacts;
- Trigger Broker durable queues;
- Mission Blackboard;
- stable IDs/incarnation generations/observation cursors;
- minimal Eye Live control/monitor surface;
- host/engine protocol;
- engine supervision/A-B selection/rollback;
- tiny authoritative state.

Routine capability development should almost never modify the stable host.

### Versioned engine ownership

The replaceable engine owns evolving feature logic:

- UIA and desktop interpretation/actions;
- Windows capture implementation;
- Chrome/CDP behavior;
- file/code/document/data/media/GPU/provider adapters;
- higher-level capability implementations;
- version-matched session-worker behavior.

The engine owns nothing required to repair or roll back itself.

### Degraded-mode repair invariant

With no healthy engine, ChatGPT must still retain system/capability status, engine restart/activate/rollback, raw SYSTEM/user/WSL execution, jobs/terminals, artifact reads, mission/trigger state, and minimal Eye Live monitoring.

### Host/engine protocol

The host/engine protocol is independently versioned and small.

The engine handshake includes engine protocol version, engine build/version, public contract hash, supported operation IDs, and worker protocol version.

The host refuses incompatible engines.

Compatibility is asymmetric: new engines should normally work with the existing host; host changes are rare.

### Engine updates

Engine replacement uses staged A/B directories, supervised startup, protocol/contract handshake, health checks, atomic routing, previous-version retention, and automatic rollback on handshake failure or crash-loop behavior.

Existing host-owned jobs, terminals, artifacts, triggers, mission state, and MCP connectivity are not replaced with the engine.

### Durable jobs and ConPTY

Long operations automatically become durable host-owned jobs after a short fast-completion window.

Jobs survive the MCP request, ChatGPT/tunnel disconnects, and engine replacement/crash. Output is incrementally spooled and read through cursors. Job Objects own descendants. Cancellation terminates the owned tree consistently.

Native ConPTY is the canonical pseudoterminal primitive. The host owns terminal handles/lifetime. Use current lifetime APIs including `ReleasePseudoConsole` where available.

A host crash cannot preserve live Win32 handles/ConPTY; recovered metadata is marked interrupted rather than pretending impossible continuity.

### Active-user execution

LocalSystem owns user-context execution using `WTSQueryUserToken`, `CreateEnvironmentBlock`, `CreateProcessAsUser`, explicit inherited-handle lists, and host-owned Job Objects.

A permanent user helper is not required.

### WSL

WSL runs through active-user execution.

Target baseline:

```text
Ubuntu 24.04 LTS
WSL2
systemd enabled
root default user
```

Linux-native permission-sensitive workloads belong in the WSL Linux filesystem rather than ReFS.

### Trigger Broker / native waits

Waiting is event-driven wherever practical. The host owns durable trigger registrations/queues; UIA/CDP watchers in the engine feed the host queue.

Do not turn waits/triggers into a workflow engine.

### Artifact plane

Large files, output, images, recordings, audio, dumps, downloads, generated documents, and query results use stable artifact handles rather than giant inline MCP payloads.

ChatGPT file inputs should use supported top-level file parameters where applicable.

### Stable identity model

Canonical identity:

```text
stable object ID + incarnation generation + observation cursor
```

Use it for processes, terminals, windows/UIA nodes, browser targets/frames/nodes, files/directories, artifacts, and other stateful resources.

Prefer state deltas over repeated complete snapshots.

### Bounded batching only

Allow parallel reads, finite ordered steps, result references, `stop_on_error`, and an overall deadline.

No loops, generic DAG engine, scheduler DSL, workflow language, or visual workflow builder.

### Eye Live

`eye_live` is an optional MCP Apps UI accelerator for mission/job/trigger/artifact/relay supervision.

Everything meaningful it displays or controls must remain available through ordinary MCP operations.

Use `_meta.ui.resourceUri` and the MCP Apps `ui/*` bridge for new UI. App-only helper tools use `_meta.ui.visibility` with app-only visibility. Follow-up messages use `ui/message`.

### Eye Operator skill

The plugin package includes a compact Eye Operator skill teaching this modality order:

```text
native typed operation
-> CLI/API/direct file
-> CDP
-> UI Automation
-> pixels/OCR/input
-> raw execution fallback
```

It also teaches durable jobs, native waits, artifacts, stable-handle reuse, and contract discipline.

Server initialization instructions carry compact cross-tool guidance; keep the first 512 characters self-contained.

### Mission Blackboard / Relay

The host uses one small embedded SQLite-backed Blackboard containing objective, current facts/decisions, active jobs/triggers, artifacts, unresolved questions, next action, and compact relay messages.

It must not become a transcript archive, task taxonomy, receipt store, generic workflow database, or DAG engine.

Multi-tab relay is optional continuation machinery through Eye Live; Eye does not claim MCP can awaken arbitrary closed chats.

### Context helper

A one-shot global context capture/handoff helper is a first-class feature. It may include active window/app, selection, clipboard, UIA, screenshot/region, current Chrome context, and relevant path.

### Desktop stack

Canonical observation/control hierarchy:

1. HWND/process/window inventory;
2. event-driven cached UIA + Remote Operations;
3. Windows.Graphics.Capture dirty-region observation;
4. OCR/visual grounding only when structural APIs are insufficient.

Workers are short-lived. Host owns worker process/IPC/lifecycle; version-matched worker behavior comes from the active engine.

### Browser stack

Use installed Chrome, dedicated Eye profile/data directory, loopback CDP, and generated typed CDP bindings.

Raw CDP is permanent. Playwright .NET is optional acceleration only. No permanent Node daemon or separate browser fleet.

### Worker IPC / streams

Use StreamJsonRpc over named pipes for typed control/events/cancellation.

Use multiplexed binary streams, favored via Nerdbank.Streams, for stdout/stderr/VT/images/audio/files.

### Win32 bindings

Use CsWin32-generated bindings/SafeHandles as the preferred permanent Win32/COM layer.

### Windows-native capability posture

Prefer Windows facilities such as BITS, VSS, Restart Manager, Process Snapshotting, ReFS block cloning, CopyFile2, ProjFS where needed, Virtual Disk API, and UIA Remote Operations/cache/events over reinvention.

### Code/document/data/media posture

Layer by measured need:

- ripgrep -> Tree-sitter/ast-grep -> on-demand language servers;
- MarkItDown/PdfPig/Open XML/ClosedXML -> heavier Docling only when needed;
- embedded/on-demand DuckDB;
- NAudio + short-lived whisper.cpp;
- PaddleOCR/Tesseract + ONNX/OpenCV on demand.

No permanently loaded local planner/model by default.

### Resource-aware execution

Eye may account for GPU free memory, CPU/GPU thermals/throttling where available, AC/battery state, storage tier, process priority/affinity, Job Object resource controls, and Windows power requests.

### Standalone behavior / transport separation

Eye remains laptop-native and useful without ChatGPT connected.

Remote access uses the official OpenAI Secure MCP Tunnel as external transport. A tunnel outage must not stop host-owned local work.

HEC/VPS/Docker/Kubernetes/Tailscale/Codex/Work/paid-API controller are not required dependencies.

### Storage roles

Canonical roles:

```text
C: Windows / applications / stable host state / encrypted secrets / engine metadata
X: physical ReFS Dev Drive / repos / hot workspaces / job spool / temporary artifacts / ReFS clones
E: models / media / archives / large downloads / cold bulk artifacts
WSL filesystem: Linux-native permission-sensitive work
```

Intended `X:` size is approximately 300 GiB when provisioned.

Tiny authoritative host state must live under a SYSTEM-owned `C:\ProgramData\StealthEye` path and not depend on `X:` existing.

### Machine secret persistence

Use DPAPI-NG `LOCAL=user` invoked by LocalSystem for locally retained service secrets, storing only encrypted blobs and non-secret metadata.

### External identities

Operational Google identity:

```text
stealtheye.eye@gmail.com
```

Separate mailbox:

```text
stealtheye@stealtheye.io
```

Current secret names:

```text
EyeRuntime
OpenAIAdmin
```

Never commit secret values.

### Login/account boundary

Leave Windows login/account/autologon architecture alone unless explicitly requested by the owner.

### Old repository

The old `se` repository is prototype/history material. Do not port it wholesale into Eye.

### Architecture stop rule

The architecture is mature enough to build. New research should normally fit capabilities beneath `docs/BUILD_BLUEPRINT.md` rather than create new layers.

Architectural expansion requires explicit owner authorization.

## 2. Favored / provisional decisions

### State store

Favor embedded SQLite for tiny host-owned durable metadata, Blackboard, trigger metadata, job metadata, artifact metadata, and version state.

### Engine/update implementation

Favor simple versioned directories and atomic active-version selection. VeloPack remains useful reference/possible machinery where it fits without obscuring the host-controlled A/B model.

### EyeBench

Use a small real-task EyeBench during engineering for task success, elapsed time, Eye calls, retries/restarts, bytes transferred, and user intervention.

### External dependency use

Dependencies cataloged in `docs/OSS_LANDSCAPE.md` are candidates, not automatic imports. Add only when implementation or measured workload justifies them.

### Tunnel supervision

Favor the official `tunnel-client` external to Eye under ordinary Windows startup/supervision.

## 3. Open decisions

### Exact engine IPC transport details

StreamJsonRpc/named pipes are favored, but exact host/engine method partition, message framing, and bulk-channel binding should be finalized during implementation without weakening the fault boundary.

### Exact Eye Live presentation

Inline and picture-in-picture behavior are desired where supported; exact component UX remains implementation-level as long as ordinary MCP parity is preserved.

### E: filesystem

The role is stable; exact filesystem remains a platform choice.

### Direct Google/OpenAI/GitHub provider adapters

Add direct provider credentials/operations only when they provide concrete value. Match credentials to the authority the owner actually intends.

### Semantic/vector retrieval

Do not add vector infrastructure until measured retrieval workloads justify it.

### Heavy local models / GUI grounding

Remain on-demand/experimental rather than permanent runtime components.

## 4. Canonical implementation sequence

The detailed blueprint is `docs/BUILD_BLUEPRINT.md`.

Current sequence:

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

## 5. Architecture filter

Before adding a component, ask:

1. Does it fit under the existing blueprint?
2. Does Windows/.NET already provide the primitive?
3. Should it be a host primitive, replaceable engine capability, or on-demand external tool?
4. Does it reduce a measured failure mode or implementation burden?
5. Is it required for a current/near-term capability?
6. Does it preserve the one-service fault boundary and six-tool public surface?
7. Does it avoid manufacturing authority friction?
8. Does its exact license fit the intended use/distribution model?

If it mainly adds ceremony, duplicate agency, speculative future-proofing, framework gravity, or a new architectural layer without demonstrated need, do not add it.

## 6. Source-of-truth rule

A successful experiment or research finding is evidence, not automatically a canonical design change.

Promote it here only when explicitly accepted by the owner.

When canon changes, update the source documents rather than leaving contradictory instructions scattered through old notes.
