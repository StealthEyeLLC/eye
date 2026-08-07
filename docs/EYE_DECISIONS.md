# EYE_DECISIONS.md

**Status:** Decision ledger and architecture freeze guardrail  
**Baseline date:** 2026-08-07

This document prevents exploratory findings from silently becoming canonical Eye design.

Use three states:

- **Canonical** — approved direction; future work should preserve it unless explicitly changed.
- **Favored / provisional** — strong current preference supported by evidence, but exact form can still change.
- **Open** — intentionally unresolved.

## 1. Canonical decisions

### Identity

- Product: **StealthEye**
- Repository: **`StealthEyeLLC/eye`**
- Local repository target: **`X:\Repos\eye`**
- Primary executable: **`eye.exe`**
- Core implementation language: **C# / .NET**

### Five-tool public MCP surface

The previous single public `eye({ op, args })` design is retired.

The canonical model-facing public surface is:

```text
eye_inspect
eye_run
eye_change
eye_interact
eye_external
```

Purpose:

- `eye_inspect` — local observation/read/query;
- `eye_run` — Windows/WSL/process/PowerShell/ConPTY execution;
- `eye_change` — precisely typed local mutations;
- `eye_interact` — desktop/application/browser interaction;
- `eye_external` — effects that intentionally leave the local machine.

This split is for accurate schemas, tool selection and effect metadata. It is not an internal privilege hierarchy.

All five facades route to one internal operation registry/dispatcher. `eye_run` remains the raw local execution escape hatch.

### Frozen generated public contract

The canonical contract source is:

```text
contracts/eye-mcp-v1.json
```

Generate from it:

- MCP tool descriptors;
- C# request/result types;
- operation/facade registration;
- capabilities metadata;
- contract documentation;
- normalized `tools/list` snapshot tests.

Public tool names, descriptions, effect classifications, input schemas and output schemas must not change during ordinary implementation work.

Breaking public changes require explicit owner authorization and a contract revision/version.

The repository guardrail is recorded in `AGENTS.md`.

### Result semantics

Use one stable structured result envelope:

```text
{ ok: true, result: ... }
```

or:

```text
{ ok: false, error: { code, message, retryable?, expected? } }
```

Routine domain failures should not escape as arbitrary MCP transport exceptions.

### Permanent runtime

One permanent Windows service:

```text
eye.exe service
```

Run it as LocalSystem.

Do not require a permanent user-session daemon, permanent Node automation service, or competing permanent MCP servers.

### Authority posture

If the owner intentionally grants authority to Eye, StealthEye should not add avoidable internal approval or privilege friction on top of that grant.

Do not deliberately downscope broad credentials merely for architectural neatness.

### Transport separation

The OpenAI Secure MCP Tunnel is transport only.

Eye serves loopback MCP and remains independently useful without owning tunnel lifecycle.

HEC/VPS infrastructure is not part of final Eye.

### Docker / orchestration

Docker and Kubernetes are not part of the target Eye runtime.

Eye is not a generic workflow engine, agent framework, plugin marketplace or generic multi-machine orchestrator.

### Windows login/account boundary

Leave Windows login/account/autologon architecture alone unless the owner explicitly requests a change. It is not a current Eye implementation target.

### Storage roles

Canonical intended roles:

```text
C: Windows / installed applications
X: physical ReFS Dev Drive / repos / build workspace
E: bulk data / models / archives / large artifacts
WSL filesystem: Linux-native permission-sensitive work
```

The intended `X:` development volume is approximately 300 GiB on the internal NVMe when provisioned.

### Native active-user execution

The LocalSystem service owns user-context execution. For commands that must run as the active interactive user:

- discover the active session;
- obtain its token with `WTSQueryUserToken`;
- build its environment with `CreateEnvironmentBlock`;
- launch with `CreateProcessAsUser`;
- use explicit intended inherited handles/pipes;
- assign the actual child to a service-owned Job Object;
- supervise descendants, cancellation, timeout and exit state directly.

A permanent user-session helper is not part of the target architecture.

### Win32 bindings

Use **CsWin32-generated bindings and SafeHandles** as the permanent preferred Win32/COM interop layer.

Handwritten interop remains acceptable temporarily while replacing the proven prototype paths.

### Terminal

Use native ConPTY for pseudoterminal execution.

Use current lifetime APIs available on the target Windows build, including `ReleasePseudoConsole` where appropriate.

Do not carry Pty.Net into v2 unless a concrete missing capability is demonstrated.

### Worker IPC

Use **StreamJsonRpc over named pipes** for service/worker control, events and cancellation.

Use **multiplexed binary streams** (favored implementation: Nerdbank.Streams) for bulk stdout/stderr/VT/image/audio/file traffic.

Do not invent a giant JSON framing protocol for all binary data.

### Browser

Use installed Chrome with a dedicated StealthEye data/profile directory and loopback Chrome DevTools Protocol.

Raw **generated typed CDP bindings** are the permanent primitive.

Playwright .NET may be used as an optional accelerator when its higher-level behavior materially helps, but it must not become a permanent Node daemon or separate browser fleet.

### Desktop worker and observation stack

Desktop-bound work uses short-lived `eye.exe worker` processes created on demand in the active session.

Canonical observation hierarchy:

1. HWND/process/window inventory;
2. event-driven UI Automation with cache requests and Remote Operations;
3. Windows.Graphics.Capture with dirty-region-aware capture;
4. OCR/visual grounding only when structural APIs are insufficient.

Workers opt into Per-Monitor V2 DPI awareness before coordinate-sensitive work.

Do not keep a permanent desktop worker unless measurements prove it necessary.

### WSL baseline

Target:

```text
Ubuntu 24.04 LTS
WSL2
systemd enabled
root default user
```

Launch WSL through active-user execution from the service. Linux-native permission/ownership-sensitive workloads live in the WSL Linux filesystem rather than on ReFS.

### Windows-native capability posture

Prefer exposing built-in Windows facilities over reimplementing their lifecycle behavior when a real workload exists.

High-value facilities include:

- BITS;
- VSS;
- Restart Manager;
- Process Snapshotting;
- ReFS block cloning;
- CopyFile2;
- ProjFS when needed;
- Virtual Disk API;
- UIA Remote Operations/cache/events.

Candidate operation names are not automatically public contract entries.

### Code/document/data/media posture

Layer capabilities by measured need:

- code: ripgrep -> Tree-sitter/ast-grep -> on-demand language servers;
- documents: MarkItDown/PdfPig/Open XML/ClosedXML, heavier Docling only as needed;
- data: embedded/on-demand DuckDB;
- audio: NAudio + short-lived whisper.cpp;
- local vision: PaddleOCR/Tesseract + ONNX/OpenCV on demand.

Do not keep an always-running local model merely because the GPU can run one.

### Machine credential persistence

For secrets that the LocalSystem Eye service must retain locally, use **DPAPI-NG with protection descriptor `LOCAL=user`, invoked by LocalSystem**.

Persist only encrypted blobs and non-secret metadata under a SYSTEM-owned machine path.

This mechanism was previously validated across reboot with throwaway material; the interactive user could not decrypt the same blob.

### External Eye identity

Use:

```text
stealtheye.eye@gmail.com
```

as Eye's operational Google identity.

The separate `stealtheye@stealtheye.io` mailbox remains distinct unless deliberately migrated later.

### OpenAI secret names

Current secret names supplied by the owner:

```text
EyeRuntime
OpenAIAdmin
```

Do not place their values in source.

### Old repository

The old `se` repository is prototype/history material.

Do not copy the old codebase wholesale into `eye`.

## 2. Favored / provisional decisions

### Atomic updates

Favor VeloPack-style staged/atomic Windows updates and rollback.

### EyeBench

Favor a small laptop-native benchmark suite drawn from real desktop/browser/code/terminal/file tasks.

Measure task success, elapsed time, tool calls, retries/restarts, bytes transferred and required user intervention.

### Dependency use

Favored components and external engines are cataloged in `docs/OSS_LANDSCAPE.md`.

A listed project is not automatically a dependency. Add it only when the implementation or a measured workload justifies it.

### Tunnel supervision

Favor running official `tunnel-client` externally under ordinary Windows startup/supervision rather than custom tunnel code inside Eye.

The exact startup mechanism remains an implementation detail until tested.

### Legal/source hygiene

Favor one `THIRD-PARTY-NOTICES` file plus pinned dependency versions when third-party runtime/deployment dependencies become substantial enough to require it.

Use build-time SBOM/license tooling later if dependency/import volume justifies the machinery.

## 3. Open decisions

### E: filesystem

The target role of `E:` is stable; exact filesystem choice can remain a platform decision.

### WSL package set

Do not preinstall a large Linux toolchain merely because it is available. Add packages as concrete work requires them.

### Google direct API access from eye.exe

Standalone direct Google API credentials remain optional. Add them only if laptop-side provider operations provide a concrete benefit beyond ChatGPT connectors.

### OpenAI admin operation surface

The owner intends broad OpenAI admin authority.

Decide later whether `eye_external` publishes a general raw OpenAI request operation, specific broad organization-management operations, or both.

Do not intentionally narrow the underlying credential's granted authority.

### GitHub machine authority

Choose steady-state machine-side GitHub authority only when `eye.exe` itself needs to push/administer GitHub. Match the credential to the intended authority rather than automatically using a repo-scoped deploy key.

### Semantic/vector retrieval

Do not add vector infrastructure until a measured code/document/data retrieval workload demonstrates benefit over current structured/search primitives.

### Heavy local models / GUI grounding

Remain on-demand/experimental. Do not promote them into a permanent runtime without a measured reason.

## 4. Canonical implementation order

Current build order:

```text
1. Generate/freeze the five-facade public MCP contract and tools/list snapshot
2. Replace handwritten Win32 declarations with CsWin32
3. Establish StreamJsonRpc control IPC and multiplexed worker streams
4. Finish Job Object / active-user / ConPTY execution semantics
5. Build event-driven cached UIA plus dirty-region capture
6. Generate typed CDP bindings; add optional Playwright .NET
7. Add BITS/VSS/Restart Manager/ReFS clone/process snapshots as real workloads require them
8. Add code/document/data/audio adapters based on actual use
9. Add whisper/OCR/semantic retrieval only on demand
10. Add staged atomic updating and EyeBench
11. Cut over from prototype/transitional runtime only after v2 independently proves reboot/terminal/desktop/browser/failure recovery
```

Do not begin by porting old `se` implementation wholesale.

## 5. Architecture filter

Before adding a component, ask:

1. Does Windows/.NET already provide the primitive natively?
2. Can the capability be external/on-demand instead of permanent?
3. Does the dependency reduce a measured failure mode or implementation burden?
4. Is it required for a current or near-term capability?
5. Does it preserve one service, one internal dispatcher and the five stable model-facing effect classes?
6. Does it avoid manufacturing authority friction?
7. Does its exact license fit the intended use/distribution model?

If the component is mainly ceremony, speculative future-proofing, policy layering, framework gravity or duplicate agency, do not add it yet.

## 6. Source-of-truth rule

A successful experiment or research finding is evidence, not automatically a canonical design change.

Promote it here only when the owner explicitly accepts the direction or the conversation clearly establishes it as the intended target.

When a canonical decision changes, update the source documents instead of leaving contradictory instructions scattered through older notes.
