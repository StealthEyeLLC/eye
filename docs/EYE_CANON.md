# EYE_CANON.md

**Status:** Canonical source for the Eye / StealthEye project  
**Baseline date:** 2026-08-07  
**Product:** StealthEye  
**Project:** Eye  
**Repository:** `StealthEyeLLC/eye`  
**Local repository target:** `X:\Repos\eye`  
**Primary executable / CLI:** `eye.exe` / `eye`  
**Windows service:** `StealthEye`

## 1. Purpose

StealthEye is a laptop-native privileged capability substrate built specifically so ChatGPT can operate the dedicated StealthEye Windows machine with broad, predictable authority.

ChatGPT supplies intelligence, planning, judgment, and task orchestration. Eye supplies machine capability, observation, durable execution, waiting, transfer, state, and recovery.

Eye must not grow a second autonomous-agent runtime merely because agent frameworks exist.

## 2. Core invariant: no avoidable authority friction

When the owner intentionally grants an authority to Eye, Eye should preserve that authority rather than voluntarily downscoping it.

Eye should not add internal approval layers, artificial privilege tiers, narrow allowlists, receipt bureaucracy, or redundant confirmation mechanisms merely to constrain already-granted authority.

Real boundaries imposed by Windows secure desktop/pre-boot state, ChatGPT/OpenAI, providers, hardware, power, networking, or the execution environment remain real.

## 3. Public MCP surface

The canonical v2 model-facing surface is:

```text
eye_inspect
eye_run
eye_change
eye_interact
eye_external
eye_live
```

| Tool | Purpose |
| --- | --- |
| `eye_inspect` | Read, search, observe, query, subscribe, wait, diagnose |
| `eye_run` | SYSTEM/user/WSL processes, PowerShell, terminals, ConPTY, arbitrary local CLI fallback |
| `eye_change` | Files, patches, local configuration, services, packages, disks, snapshots, other precise local mutations |
| `eye_interact` | Windows applications, UIA, input, clipboard, Chrome/CDP interaction |
| `eye_external` | HTTP, uploads, sends, posts, provider administration, remote transfers, other open-world effects |
| `eye_live` | Opens the Eye Live mission/job/trigger/artifact/relay component; performs no machine operation itself |

The five capability facades classify effects for accurate schemas/tool selection. They are not a privilege hierarchy. `eye_run` remains the broad local execution escape hatch.

`wait` and `transfer` are first-class typed operation families beneath the facades, not additional top-level tools.

The canonical target contract is `contracts/eye-mcp-v2.json`. `contracts/eye-mcp-v1.json` is immutable historical material.

Public descriptors, request/result DTOs, host validation, operation/facade registration, capability metadata, server instructions, documentation, and normalized `tools/list` snapshots are generated from the canonical contract source.

Public-contract changes require explicit owner authorization. Routine implementation work must not silently alter the public surface.

## 4. Standalone request path

Remote ChatGPT access uses:

```text
ChatGPT
  -> OpenAI Secure MCP Tunnel
  -> tunnel-client on STEALTHEYELLC
  -> loopback MCP
  -> stable Eye host
```

The Secure MCP Tunnel is transport only.

Eye remains independently useful on the laptop when ChatGPT/tunnel connectivity is absent. Local jobs, terminals, triggers, artifacts, mission state, and engine supervision continue without a ChatGPT connection.

HEC, VPS, SEZU, Caddy, Incus, Docker, Kubernetes, Tailscale, Codex, Work, and a paid-API controller are not required parts of the final Eye runtime.

## 5. Permanent runtime topology

There is exactly one permanent Windows SCM service running as LocalSystem.

```text
Windows SCM
  -> eye.exe stable host
       -> active versioned capability-engine child process
       -> previous engine version retained for fallback
       -> on-demand active-session workers
       -> host-owned jobs / ConPTY / artifacts / triggers / state
```

The capability engine is a **separate supervised child process**, not a DLL loaded into the stable service. Risky native/COM/media/GPU/dependency failures in feature code must not be able to directly crash the MCP/repair host.

No second Windows service, permanent user-session daemon, permanent Node/browser daemon, or competing MCP server is part of the final architecture.

## 6. Stable host ownership

The stable host owns the minimal substrate required to keep Eye reachable and repairable:

- loopback MCP endpoint;
- six public descriptors, contract validation, and routing;
- server-wide MCP instructions;
- raw SYSTEM execution;
- active-user execution;
- WSL execution;
- Job Object ownership;
- native ConPTY ownership;
- durable jobs and output streams;
- artifact registry/storage;
- Trigger Broker durable queues;
- Mission Blackboard storage;
- stable object IDs, incarnation generations, observation cursors;
- minimal Eye Live control/monitor path;
- host/engine protocol;
- engine supervision and A/B selection;
- minimal repair/update capability;
- tiny authoritative persistent state.

Routine capability development should almost never modify the stable host.

Host revisions are reserved primarily for the public contract, job kernel, artifact plane, host/engine protocol, persistent state model, or repair/update path.

## 7. Versioned capability engine ownership

The replaceable engine owns rapidly evolving feature logic:

- UI Automation interpretation/actions;
- Windows.Graphics.Capture implementation;
- Chrome/CDP behavior;
- file/storage adapters beyond host repair primitives;
- code/document/data/media/GPU adapters;
- provider-specific adapters;
- higher-level capability implementations;
- version-matched active-session worker behavior.

The engine owns nothing required to repair or roll back itself.

## 8. Host/engine protocol and activation

The host/engine protocol is independently versioned and intentionally small.

The engine startup handshake includes at least:

```text
engine protocol version
engine build/version
public contract hash
supported operation IDs
worker protocol version
```

The host refuses to activate an incompatible engine.

Compatibility is intentionally asymmetric: routine new engine versions should normally run under an existing stable host. Host changes should be rare.

Engine replacement uses staged A/B directories, supervised launch, handshake/health checks, atomic activation, previous-version retention, and automatic rollback on handshake failure or crash-loop behavior.

Host-owned jobs, terminals, artifacts, triggers, mission state, and MCP connectivity remain untouched by engine replacement.

## 9. Required degraded mode

With no healthy capability engine, ChatGPT must still retain:

- `system.status` and `capabilities`;
- engine status/restart/activate/rollback;
- raw `eye_run` under SYSTEM, active user, and WSL;
- job status/output/wait/input/resize/cancel/result;
- terminal access;
- artifact reads;
- mission and trigger state;
- minimal Eye Live monitoring.

This is a hard reliability invariant.

## 10. Four machine primitives

Eye is organized around four primitives:

1. **Observe** — structured machine, UI, browser, process, file, job, artifact, and event state.
2. **Act** — raw execution or precise typed changes/interactions/external effects.
3. **Wait** — event-driven waiting for real conditions instead of repeated polling.
4. **Transfer** — large files/images/audio/output/artifacts by reference rather than enormous MCP payloads.

Higher-level capabilities should compose these primitives rather than create new architectural layers.

## 11. Durable jobs and terminals

Long-running execution is a stable-host primitive.

An operation begins immediately, gets a short fast-completion window, and automatically returns a durable `job_id` when it remains active. The caller should not need to predict whether work lasts seconds or hours.

Core job behavior includes status, incremental reads, waits, stdin, terminal resize, cancellation, attach, and final result.

Properties:

- work survives the individual MCP request;
- work survives ChatGPT/tunnel disconnects;
- work survives capability-engine replacement/crash;
- service-owned Job Objects retain descendant ownership;
- cancellation terminates the owned process tree consistently;
- output is incrementally spooled rather than retained indefinitely in RAM;
- reads use sequence/cursor semantics;
- native ConPTY terminals retain host-owned identity/state while alive;
- short commands may still complete synchronously;
- tiny durable metadata lives in a host-owned SQLite store.

A host crash cannot preserve live Win32 handles or a ConPTY. On host restart, metadata for such work is recovered and marked interrupted rather than pretending an impossible attachment survived.

## 12. Active-user and WSL execution

For interactive-user work, LocalSystem uses the supported native path:

- identify the active session;
- obtain its token with `WTSQueryUserToken`;
- create its environment with `CreateEnvironmentBlock`;
- launch with `CreateProcessAsUser`;
- use explicit inherited-handle lists;
- place the actual child in a host-owned Job Object;
- supervise descendants, cancellation, exit, and output directly.

WSL is launched through the same active-user path. No permanent user or WSL helper is required.

Target Linux baseline:

```text
Ubuntu 24.04 LTS
WSL2
systemd enabled
root default user
```

Linux-native permission/ownership-sensitive workloads belong inside the WSL Linux filesystem rather than relying on ReFS metadata behavior.

## 13. Native wait and Trigger Broker

The host Trigger Broker owns durable condition registrations and event queues. Engine-owned UIA/CDP watchers feed host-owned queues.

Supported/favored event sources include process/job exit, output/log regex, file creation/change/unlock, services, ports, session lock/unlock, Event Log/WMI, device arrival, power/network/idle state, windows, UIA, Chrome/CDP, CPU/GPU thresholds, time conditions, and any-of groups.

This is an event primitive, not a workflow engine.

## 14. Artifact plane

Large data moves through stable artifact handles.

Artifacts may represent terminal output, screenshots, recordings, audio, dumps, traces, archives, browser downloads, generated documents, query results, or files imported from ChatGPT.

Core behavior includes info, preview, range reads, export, delete, and diff.

Large results return an artifact plus a useful excerpt rather than tens of thousands of inline tokens.

Use supported top-level ChatGPT file parameters where applicable so attached files can be imported directly into Eye without requiring a manual laptop path.

## 15. Stable identities, incarnations, and cursors

Canonical identity model:

```text
stable object ID + incarnation generation + observation cursor
```

The stable ID identifies the logical object. The incarnation detects replacement/reuse. The cursor identifies how much state the caller has consumed.

This applies across windows/UIA nodes, processes, terminals, browser targets/frames/nodes, files/directories, artifacts, monitor layouts, and other stateful resources.

Eye should send deltas rather than full state wherever useful: UIA patches, dirty screen regions, terminal/output changes, browser accessibility/DOM changes, filesystem changes, and process/window additions/removals.

## 16. Bounded batching

Eye may support finite batching for reduced round trips:

- parallel independent reads;
- ordered finite steps;
- references to earlier results;
- `stop_on_error`;
- one overall deadline.

Do not add loops, a generic DAG engine, workflow DSL, scheduler language, or visual workflow builder.

## 17. Eye Live

`eye_live` opens a compact optional MCP Apps component for mission/job/trigger/artifact/relay supervision.

It may show live output tails, terminal controls, active triggers, artifacts, mission/context state, and relay queues.

Eye Live is an accelerator, never a dependency. All meaningful state/action remains available through ordinary MCP operations.

New UI uses `_meta.ui.resourceUri` and the MCP Apps `ui/*` bridge. App-only helper tools use `_meta.ui.visibility` with app visibility so they do not pollute model tool selection. Follow-up messages use the standard `ui/message` bridge.

## 18. Eye Operator skill and server instructions

The plugin package contains a compact Eye Operator skill.

Preferred modality hierarchy:

1. precise native typed operation;
2. CLI/API/direct file manipulation;
3. Chrome CDP;
4. Windows UI Automation;
5. pixel/OCR/input fallback;
6. raw unrestricted execution when no precise operation exists.

The skill teaches durable jobs for long work, native waits instead of polling, artifacts instead of giant inline output, stable-handle reuse, selective `operation.describe`, and public-contract discipline.

MCP server initialization instructions carry compact cross-tool guidance. Keep the first 512 characters self-contained.

## 19. Mission Blackboard and Relay

The stable host contains one embedded SQLite-backed Mission Blackboard with a fixed compact schema:

- objective;
- current facts/decisions;
- active jobs/triggers;
- artifacts;
- unresolved questions;
- next action;
- compact relay messages.

It is not a transcript archive, workflow/DAG database, task taxonomy, receipt system, or generic orchestration engine.

Eye Live can associate ordinary Chat tabs with missions and optional roles and relay compact messages while those conversations/components are available. Closed tabs are not assumed to be spontaneously activatable through MCP.

## 20. Context capture

A one-shot global context helper is a first-class feature.

It can capture available pieces such as active app/window, selected/focused text, clipboard, UIA context, screenshot/region, current Chrome context, and relevant filesystem path, then route one dense context packet to the chosen mission.

The same helper may support Explorer "Send to Eye" and Chrome context-menu handoff.

## 21. Desktop observation and interaction

Canonical hierarchy:

1. HWND/process/window inventory;
2. event-driven cached Windows UI Automation and Remote Operations;
3. Windows.Graphics.Capture with dirty-region observation;
4. OCR/visual grounding only when structure is insufficient.

Target stable element references/selectors before pixel coordinates.

Active-session workers are short-lived and launched on demand. The stable host owns lifecycle/IPC/identity/cleanup, while version-matched worker behavior comes from the active engine so risky desktop code stays outside the host.

Workers use Per-Monitor V2 DPI awareness for coordinate-sensitive work.

Secure desktop and pre-boot state remain real boundaries.

## 22. Browser

Use installed Chrome under the active user, a dedicated Eye profile/data directory, loopback CDP, and generated typed bindings.

Raw CDP is the permanent browser primitive.

Playwright .NET is optional and used only where its locator/wait/download/trace behavior materially helps.

No permanent Node daemon or separately downloaded browser fleet.

## 23. Worker IPC and bulk streams

Use **StreamJsonRpc over named pipes** for typed host/engine/worker control, events, and cancellation.

Use **multiplexed binary channels** (favored implementation: Nerdbank.Streams) for stdout, stderr, VT data, screenshots, audio, and files.

JSON is the control plane, not the universal bulk-data format.

## 24. Native interop foundation

C# / .NET is the core implementation language.

Use **CsWin32-generated bindings and SafeHandles** as the preferred permanent Win32/COM interop layer.

Prefer Windows/.NET native facilities before adding third-party abstractions where practical.

## 25. High-value Windows facilities

Use native Windows facilities when real workloads justify them:

- BITS;
- VSS;
- Restart Manager;
- Process Snapshotting;
- ReFS block cloning;
- CopyFile2;
- ProjFS when useful;
- Virtual Disk API;
- UIA Remote Operations/cache/events.

Expose them through precise typed operations beneath the existing six-tool public surface.

## 26. Code, documents, data, audio, and vision

Layer capabilities by measured need:

- code: ripgrep -> Tree-sitter/ast-grep -> on-demand language servers;
- documents: MarkItDown/PdfPig/Open XML/ClosedXML, heavier Docling when needed;
- data: embedded/on-demand DuckDB;
- audio: NAudio + short-lived whisper.cpp;
- local vision: PaddleOCR/Tesseract + ONNX/OpenCV on demand;
- occasional local inference only when measured useful.

Do not keep an always-running local planner/model merely because the GPU can run one.

## 27. Machine manifests and resource awareness

Eye should provide current machine truth through concise typed operations such as `machine.describe`, `session.describe`, `volume.describe`, `software.find`, `software.version`, `operation.list`, and `operation.describe`.

Execution can account for GPU free memory, thermals/throttling where available, AC/battery state, storage tier, process priority/affinity, Job Object resource controls, and Windows power requests.

## 28. Storage roles

Canonical roles:

```text
C: Windows / applications / stable host state / encrypted secrets / engine metadata
X: physical trusted ReFS Dev Drive / repos / hot workspaces / job spool / temporary artifacts / block clones
E: models / media / archives / large downloads / cold and durable bulk artifacts
WSL filesystem: Linux-native permission-sensitive work
```

The intended `X:` size is approximately 300 GiB on the internal NVMe when provisioned.

Tiny authoritative host metadata must not depend on `X:` existing. Keep it under a SYSTEM-owned `C:\ProgramData\StealthEye` path.

## 29. Machine secret persistence

For credentials retained by the LocalSystem host, use the previously validated **DPAPI-NG `LOCAL=user` invoked by LocalSystem** design.

Persist only encrypted blobs and non-secret metadata under a SYSTEM-owned path.

No plaintext credential, private key, password, or recovery material belongs in source.

## 30. External authority and identities

Canonical repository:

```text
StealthEyeLLC/eye
```

Operational Eye Google identity:

```text
StealthEye <stealtheye.eye@gmail.com>
```

Separate mailbox:

```text
stealtheye@stealtheye.io
```

Current secret names supplied by the owner:

```text
EyeRuntime
OpenAIAdmin
```

Secret values must not be committed.

Machine-side GitHub/provider authority should match the authority actually intended when such direct adapters are implemented.

## 31. Windows login/account boundary

Windows login/account/autologon architecture is not a current Eye implementation target. Leave it alone unless the owner explicitly requests a change.

Eye should depend on the existence of an active interactive session when desktop work requires one, not on speculative account-management machinery.

## 32. Dependency and licensing posture

Prefer permissive dependencies and verify the exact pinned revision/license before importing code.

General posture:

- MIT/Apache/BSD/MS-PL: normally suitable with notices;
- MPL: preserve file-level obligations;
- LGPL: prefer external/dynamic use where appropriate;
- GPL/AGPL: isolate externally unless obligations are deliberately accepted;
- no usable license: do not copy implementation code.

Model weights, datasets, and subdirectories can carry different licenses.

Detailed dependency research lives in `docs/OSS_LANDSCAPE.md`.

## 33. Explicit non-goals

Do not turn Eye into:

- a second autonomous-agent brain;
- a generic workflow/DAG engine;
- a plugin marketplace;
- a policy/approval framework;
- a receipt/evidence bureaucracy;
- a generic multi-machine orchestrator;
- a VPS-dependent runtime;
- a Docker/Kubernetes base;
- a permanent local planner/model;
- a permanent Node/browser automation daemon;
- a collection of competing MCP servers;
- a kernel-driver project without a proven hardware need;
- a large custom ChatGPT UI or dashboard duplicating ordinary tool output.

## 34. Canonical build blueprint

The implementation sequence and ownership detail live in `docs/BUILD_BLUEPRINT.md` and `docs/CUTOVER.md`.

The architecture is considered mature enough to build. Future research should normally answer **where a capability fits under the existing blueprint**, not invent a new architectural layer.

Architectural expansion requires explicit owner authorization.

## 35. Design style

Favor:

- directness;
- raw native authority;
- predictable machine structure;
- exact generated contracts;
- durable jobs and event waits;
- stable identities and deltas;
- artifact references instead of giant payloads;
- one permanent service;
- replaceable/fault-contained feature logic;
- minimal permanent processes;
- minimal third-party runtime weight;
- clear transport/local-capability separation;
- measured growth based on real tasks.

Avoid architecture ceremony, speculative layers, framework gravity, and duplicate agency.
