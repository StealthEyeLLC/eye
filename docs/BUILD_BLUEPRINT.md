# BUILD_BLUEPRINT.md

**Status:** Canonical implementation blueprint  
**Baseline date:** 2026-08-07  
**Product:** StealthEye  
**Repository:** `StealthEyeLLC/eye`

This document is the implementation blueprint for Eye. New capabilities should fit beneath this architecture rather than create new architectural layers unless the owner explicitly revises the canon.

## 1. Product boundary

Eye is a compact privileged Windows capability substrate operated by ChatGPT.

ChatGPT supplies intelligence, planning, judgment, and task orchestration. Eye supplies reliable machine capability, durable state, observation, execution, waiting, transfer, and recovery.

Eye is not an autonomous-agent framework, workflow engine, local planner, plugin marketplace, or container platform.

## 2. Final public surface

The canonical model-facing surface contains five effect-class capability tools plus one UI-only tool:

```text
eye_inspect
eye_run
eye_change
eye_interact
eye_external
eye_live
```

| Tool | Responsibility |
| --- | --- |
| `eye_inspect` | Read, search, observe, query, subscribe, wait, and diagnose |
| `eye_run` | SYSTEM/user/WSL processes, PowerShell, terminals, ConPTY, and arbitrary local CLI fallback |
| `eye_change` | Files, patches, local configuration, services, packages, disks, snapshots, and other precise local mutations |
| `eye_interact` | Windows applications, UIA, input, clipboard, and Chrome/CDP interaction |
| `eye_external` | HTTP, uploads, sends, posts, provider administration, remote transfers, and other open-world effects |
| `eye_live` | Opens the Eye Live mission/job/trigger/artifact/relay component; performs no machine operation itself |

`wait` and `transfer` are first-class typed operation families underneath these facades. They are not additional top-level tools.

The effect-class split improves model selection, schema accuracy, and host metadata. It is not an internal privilege hierarchy. `eye_run` remains the broad local escape hatch.

The canonical target contract is `contracts/eye-mcp-v2.json`. The previous `eye-mcp-v1.json` remains immutable historical contract material.

## 3. Final process topology

There is exactly one permanent Windows SCM service.

```text
Windows SCM
  -> eye.exe stable host (LocalSystem)
       -> one active versioned capability engine child process
       -> previous engine version retained for fallback
       -> on-demand active-session workers
       -> owned jobs / ConPTY / artifacts / triggers / state
```

The capability engine is a **separate supervised process**, not a DLL loaded into the service. Native faults, COM faults, media/GPU faults, dependency crashes, or bad feature builds in the engine must not be able to directly crash the stable host.

No second Windows service is introduced.

## 4. Stable host ownership

The stable host owns the parts required to keep ChatGPT connected and to recover Eye even when the capability engine is unusable:

- loopback MCP endpoint;
- six public descriptors and contract validation/routing;
- server-wide MCP instructions;
- SYSTEM, active-user, WSL, and raw repair execution;
- Job Objects and process-tree ownership;
- native ConPTY ownership;
- durable jobs and output streams;
- artifact registry/storage;
- Trigger Broker durable queues;
- Mission Blackboard storage;
- stable object identities, incarnation generations, and observation cursors;
- minimal Eye Live control/monitor surface;
- versioned engine supervision;
- A/B engine selection and rollback;
- minimal update/repair capability;
- host-owned persistent state.

Routine feature development should almost never change the stable host.

Host revisions are reserved primarily for changes to the public MCP contract, job kernel, artifact plane, host/engine protocol, persistent state model, or repair/update path.

## 5. Versioned engine ownership

The replaceable capability engine owns rapidly evolving feature logic:

- UI Automation interpretation and actions;
- Windows.Graphics.Capture implementation;
- Chrome/CDP behavior;
- file and storage adapters beyond host repair primitives;
- code intelligence adapters;
- document/data adapters;
- media/audio/GPU adapters;
- provider-specific adapters;
- higher-level capability implementations;
- version-matched session-worker behavior.

The engine owns nothing required to repair or roll back itself.

## 6. Host/engine protocol

The host/engine protocol is independently versioned and intentionally small.

Engine startup handshake includes at least:

```text
engine protocol version
engine build/version
public contract hash
supported operation IDs
worker protocol version
```

The host does not activate an incompatible engine.

Compatibility is intentionally asymmetric: routine new engine versions should continue to run under an existing stable host. Host changes should be rare.

## 7. Engine replacement and rollback

Engine updates follow an A/B pattern:

1. Stage the new engine version beside the active version.
2. Launch it as a supervised child.
3. Require protocol and public-contract-hash handshake.
4. Run activation health checks.
5. Atomically route new engine-owned operations to the new child.
6. Retain the previous engine version.
7. Revert automatically on handshake failure or crash-loop behavior.
8. Leave host-owned jobs, terminals, artifacts, triggers, mission state, and MCP connectivity untouched.

A bad engine update must not destroy the only ChatGPT control path.

## 8. Required degraded-mode capability

When the engine is completely unavailable, ChatGPT must still retain enough functionality to diagnose and repair Eye:

- `system.status` and `capabilities`;
- engine status/restart/activate/rollback;
- raw `eye_run` for SYSTEM, active user, and WSL;
- job status/output/wait/input/resize/cancel/result;
- terminal access;
- artifact reads;
- mission and trigger state;
- minimal Eye Live monitoring.

This degraded mode is a hard reliability invariant.

## 9. Durable jobs and terminals

Long-running execution is a host primitive.

An operation starts immediately, receives a short fast-completion window, and automatically returns a durable `job_id` when it remains active. The caller should not need to predict whether work will take seconds or hours.

Core job capability includes:

```text
job.start
job.status
job.read
job.wait
job.write
job.resize
job.cancel
job.attach
job.result
```

Properties:

- work survives the individual MCP request;
- work survives ChatGPT/tunnel disconnects;
- work survives capability-engine replacement or crash;
- service-owned Job Objects retain descendant ownership;
- cancellation consistently terminates the owned process tree;
- stdout/stderr/VT output is incrementally spooled rather than retained indefinitely in RAM;
- output reads use sequence/cursor semantics;
- ConPTY terminals retain terminal identity, input, dimensions, and working state while the host owns them;
- short commands can still return synchronously;
- tiny durable metadata is stored in host-owned SQLite state.

A host process crash cannot preserve live Win32 handles or a live ConPTY. After host restart, metadata for such work is recovered and marked interrupted rather than pretending an impossible attachment survived.

## 10. Native wait and Trigger Broker

Eye should not force ChatGPT to poll repeatedly for conditions Windows or applications can signal.

The host Trigger Broker owns durable condition registrations and event queues. Engine-owned UIA/CDP watchers feed events into that host-owned queue.

Wait/trigger sources can include:

- job/process exit;
- output/log regex or terminal text;
- file creation/change/unlock;
- service state;
- port availability;
- session lock/unlock;
- Windows Event Log and WMI events;
- device arrival/removal;
- power/network/idle state;
- window creation/disappearance/title/state changes;
- UIA element/property/events;
- Chrome navigation, DOM, network, download, and target events;
- CPU/GPU thresholds;
- time conditions;
- any-of sets that return the first satisfied condition.

This is an event primitive, not a workflow engine.

## 11. Artifact plane

Large data moves by reference rather than through huge MCP results.

Artifact metadata includes stable identity plus useful fields such as kind, MIME type, size, hash, name, storage tier, and provenance.

Core artifact capability includes:

```text
artifact.info
artifact.preview
artifact.read_range
artifact.export
artifact.delete
artifact.diff
```

Use artifacts for:

- large terminal output;
- screenshots and screen recordings;
- audio;
- dumps and traces;
- archives;
- browser downloads;
- generated documents;
- data-query results;
- imported ChatGPT files.

Hot/high-churn artifacts belong on `X:` when available. Large/cold durable artifacts belong on `E:`. Tiny authoritative metadata belongs under SYSTEM-owned `C:\ProgramData\StealthEye` state.

ChatGPT file inputs should use supported top-level file parameters so an attachment can become an Eye artifact without requiring the user to manually provide a laptop path. Reverse export should use supported tool/file-reference mechanisms when implemented.

## 12. Stable identity and deltas

The canonical identity model is:

```text
stable object ID + incarnation generation + observation cursor
```

Meaning:

- stable object ID identifies the logical object;
- incarnation generation detects destruction/replacement or OS identifier reuse;
- observation cursor identifies how much state the caller has already consumed.

Examples:

- a window title change advances its cursor;
- a destroyed/recreated window changes incarnation even if an HWND is reused;
- a process identity includes creation identity so PID reuse cannot confuse Eye;
- a file modification advances its cursor while replacement at the same path changes incarnation;
- browser target detach/recreation changes incarnation;
- terminal output advances its cursor without changing terminal identity.

Observation should favor deltas over repeated full state:

- UIA tree patches;
- changed screen regions;
- terminal screen/output changes;
- stdout/stderr chunks;
- browser accessibility/DOM changes;
- filesystem changes;
- process/window additions/removals.

Eye keeps full working state locally and sends ChatGPT only the state required for the next decision.

## 13. Tiny batching algebra

Eye may support bounded batching to reduce round trips without becoming a generic workflow language.

Allowed concepts:

- parallel independent reads;
- ordered finite steps;
- references to earlier results;
- `stop_on_error`;
- one overall deadline.

Do not add loops, a scheduler DSL, visual workflow builder, arbitrary DAG engine, or generic orchestration language.

## 14. Eye Live

`eye_live` is a UI-only top-level tool that opens a compact MCP Apps component for continuation and supervision.

It may provide inline and picture-in-picture views of:

- current mission state;
- running jobs and terminals;
- live output tails;
- terminal input/cancel controls;
- active triggers;
- artifacts;
- relay inbox/outbox;
- compact machine/context snapshots.

Eye Live is an accelerator, never a dependency. Every meaningful action/state it exposes must also remain reachable through ordinary MCP operations so Eye continues working in hosts that do not render the component.

New UI follows the MCP Apps bridge: tools associate UI resources through `_meta.ui.resourceUri`; the component uses the `ui/*` JSON-RPC bridge; `ui/message` can send a follow-up message. App-only helper tools use `_meta.ui.visibility` with `app` visibility so they do not pollute model tool selection.

## 15. Operator skill and server instructions

Eye ships with a compact Eye Operator skill in the plugin package.

The skill teaches ChatGPT the preferred modality hierarchy:

1. precise native typed operation;
2. CLI/API/direct file operation;
3. Chrome CDP;
4. Windows UI Automation;
5. pixel/OCR/input fallback;
6. raw unrestricted execution when no precise operation exists.

It also teaches:

- long work should become durable jobs;
- wait for events instead of manual polling;
- use artifacts instead of enormous inline output;
- reuse stable handles and cursors;
- request `operation.describe` only for unfamiliar capability detail;
- preserve the public-contract freeze.

Server initialization instructions carry the compact cross-tool rules. The first 512 characters must remain self-contained because ChatGPT documentation specifically recommends that constraint for server-wide guidance.

## 16. Mission Blackboard and Relay

The host contains one embedded SQLite-backed Mission Blackboard with a deliberately fixed compact schema:

- objective;
- current facts and decisions;
- active jobs and triggers;
- artifacts;
- unresolved questions;
- next action;
- compact relay messages.

It must not become a transcript archive, DAG/workflow database, task taxonomy, receipt system, or generic orchestration engine.

Ordinary Chat tabs can be associated with missions and optional roles such as operator, researcher, critic, or comparator. Relay queues compact messages between participating conversations through Eye Live when the UI/session is present.

If a tab is absent, Eye preserves its queue. Eye does not claim MCP can spontaneously create arbitrary ChatGPT turns in a closed conversation.

## 17. Context capture

A one-shot context helper is a first-class feature.

A global shortcut can capture a dense context packet including available pieces such as:

- active application/window;
- focused or selected text;
- clipboard;
- UIA context;
- screenshot/region;
- current Chrome target/DOM context;
- relevant filesystem path.

The same helper can support Explorer "Send to Eye" and Chrome context-menu handoff.

## 18. Desktop observation/control

Canonical desktop hierarchy:

1. HWND/process/window inventory;
2. event-driven cached UI Automation and Remote Operations;
3. Windows.Graphics.Capture with dirty-region observation;
4. OCR/visual grounding only when structured state is unavailable.

Actions target stable structural references first, selectors second, and pixels as the final fallback.

Short-lived active-session workers are launched on demand. The host owns their process lifetime, IPC, identity, and cleanup. Version-matched worker behavior comes from the active capability engine so risky desktop code remains outside the stable host.

Workers use Per-Monitor V2 DPI awareness for coordinate-sensitive work.

Secure desktop and pre-boot state remain real boundaries.

## 19. Browser

Use installed Chrome under the active user with a dedicated Eye profile/data directory and loopback CDP.

Generate typed bindings from the Chrome DevTools Protocol. Raw CDP is the permanent primitive.

Playwright .NET is optional and loaded only where its locator/wait/download/trace behavior materially helps.

Browser state uses stable target/frame/node identity, generations, cursors, event waits, and artifact-backed downloads.

No permanent Node daemon and no separate downloaded browser fleet.

## 20. Machine manifests and resource awareness

Eye exposes concise live truth rather than forcing ChatGPT to trust stale documentation.

Useful operations include:

```text
machine.describe
session.describe
volume.describe
software.find
software.version
operation.list
operation.describe
```

Machine descriptions should include the current Windows/session state, CPU/RAM/GPU/VRAM, storage capacity, WSL state, active jobs/terminals, installed adapters, Chrome/CDP state, service state, engine version, and tunnel health where available.

Execution should be resource-aware:

- GPU free memory before local inference;
- CPU/GPU temperature and throttling when available;
- AC/battery state;
- storage tier selection;
- Job Object CPU/memory controls where useful;
- process priority/affinity where useful;
- Windows power requests for long jobs.

## 21. Capability adapters

After the substrate is stable, add thin deterministic adapters for real installed software and Windows facilities rather than mini-agents.

Favored capability areas include:

- Git/GitHub CLI;
- VS Code/language servers;
- Chrome/CDP;
- PowerShell/WSL;
- winget;
- Open XML/Office COM;
- DuckDB;
- FFmpeg;
- Windows services/Task Scheduler;
- BITS;
- VSS;
- Restart Manager;
- ReFS block cloning;
- Process Snapshotting;
- Virtual Disk APIs;
- documents/data/audio/OCR/local GPU workloads on demand.

## 22. Storage ownership

Canonical roles:

```text
C: Windows, installed applications, stable host state, encrypted secrets, engine version metadata
X: active repositories, hot workspaces, job/output spool, temporary/hot artifacts, cheap ReFS clones
E: models, media, archives, large downloads, cold/durable bulk artifacts
WSL filesystem: Linux-native permission/ownership-sensitive work
```

Authoritative host metadata should not depend on `X:` existing. Keep tiny critical state under a SYSTEM-owned `C:\ProgramData\StealthEye` path.

## 23. Transport and standalone behavior

Eye is laptop-native and independently useful without ChatGPT being connected.

Remote ChatGPT access uses the official Secure MCP Tunnel as external transport:

```text
ChatGPT -> OpenAI Secure MCP Tunnel -> laptop loopback MCP -> stable Eye host
```

A tunnel outage must not stop local jobs, terminals, triggers, artifacts, mission state, or engine supervision.

Eye has no required HEC/VPS/Docker/Kubernetes/Tailscale/Codex/Work/paid-API controller dependency.

## 24. Explicit non-goals

Do not add by default:

- a second autonomous agent brain;
- LangGraph/AutoGen-style orchestration runtime;
- a generic workflow engine;
- a vector database without measured need;
- Docker or Kubernetes as the base;
- a permanent Node/browser automation daemon;
- a large custom ChatGPT UI;
- a dashboard duplicating data ordinary tools can return;
- multiple competing execution backends for the same job;
- a permanent local planner/model;
- a kernel driver without a proven hardware requirement;
- multiple competing MCP servers;
- an extra approval/receipt bureaucracy inside Eye.

## 25. Build sequence

Canonical implementation sequence:

```text
1. Contract v2 and host/engine protocol
2. Stable host: raw execution, jobs, artifacts, state, identity model
3. Versioned engine process: supervision, handshake, A/B selector, rollback
4. Workers, streams, Trigger Broker, native waits, durable continuation
5. Eye Live and Eye Operator skill
6. Desktop and browser perception/control
7. Blackboard, Relay, context capture, multi-tab continuation
8. Files, storage, code, documents, data, media, transfer and provider adapters
9. Atomic final runtime cutover after independent recovery/reboot/end-to-end proof
```

Engineering evaluation may use EyeBench to compare success, latency, calls, retries, bytes moved, and user intervention. It is not per-task ceremony.

## 26. Architecture stop rule

The architecture is considered complete enough to build.

Future research should normally answer:

> Where does this capability fit underneath the existing blueprint?

rather than:

> What new architectural layer should Eye gain?

Architectural expansion requires an explicit owner-authorized canonical revision.
