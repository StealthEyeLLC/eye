# CUTOVER.md

**Status:** v2 implementation and runtime cutover checklist  
**Machine:** `STEALTHEYELLC`  
**Baseline date:** 2026-08-07

This checklist now covers the path from the current early v2 repository to the final Eye runtime. It intentionally does not preserve obsolete platform-migration procedure as the active plan.

## Phase 0 — verify machine foundation

Before changing Eye runtime ownership:

- [ ] Windows boots and operates normally through repeated reboots.
- [ ] Current encryption/storage state is explicitly known rather than assumed.
- [ ] `C:` is the Windows/application volume.
- [ ] `X:` is provisioned as the intended physical trusted ReFS Dev Drive, approximately 300 GiB.
- [ ] `E:` bulk/archive storage is visible and excluded from destructive provisioning operations.
- [ ] `X:\Repos\eye` exists as the clean active checkout.
- [ ] required .NET/Git/build tooling is present.
- [ ] WSL2 Ubuntu 24.04 baseline is healthy with systemd.
- [ ] NVIDIA/CUDA stack is healthy where needed.
- [ ] login/account/autologon configuration is left alone unless the owner explicitly requests a change.

## Phase 1 — reconcile repository

- [ ] Fetch current `main` from `StealthEyeLLC/eye`.
- [ ] Preserve/reapply only meaningful local work not already committed.
- [ ] Do not reintroduce CRLF/EOL-only churn.
- [ ] Do not import old `se` wholesale.
- [ ] Confirm current architecture docs agree with `EYE_CANON.md`, `EYE_DECISIONS.md`, `MCP_CONTRACT.md` and `OSS_LANDSCAPE.md`.

## Phase 2 — freeze/generated public contract

Canonical public tools:

```text
eye_inspect
eye_run
eye_change
eye_interact
eye_external
```

Checklist:

- [ ] `contracts/eye-mcp-v1.json` is the canonical public-contract source.
- [ ] tool descriptors are generated from it.
- [ ] C# request/result types are generated from it.
- [ ] operation/facade registration is generated from it.
- [ ] capability metadata is generated from it.
- [ ] exact output schemas are generated/published.
- [ ] normalized `tools/list` snapshot test exists.
- [ ] ordinary implementation changes fail tests if they accidentally mutate the public contract.
- [ ] `AGENTS.md` contract-freeze rule is honored.

The five facades are effect classifications, not privilege tiers. They all route to one internal operation registry/dispatcher.

## Phase 3 — native interop foundation

- [ ] Introduce CsWin32 generation.
- [ ] Replace suitable handwritten Win32 declarations/SafeHandle patterns incrementally.
- [ ] Keep handwritten declarations only for demonstrated gaps.
- [ ] Preserve working active-session launch behavior during migration.

## Phase 4 — process and terminal substrate

- [ ] LocalSystem execution works.
- [ ] active-user execution works through `WTSQueryUserToken` / `CreateEnvironmentBlock` / `CreateProcessAsUser`.
- [ ] explicit inherited-handle lists are used.
- [ ] stdout/stderr capture is asynchronous and reliable.
- [ ] actual child processes are service-owned through Job Objects.
- [ ] descendants are cleaned up correctly.
- [ ] timeout and cancellation semantics are explicit.
- [ ] native ConPTY is wired into the dispatcher.
- [ ] terminal resize/input/output/exit lifecycle works.
- [ ] current ConPTY lifetime behavior including `ReleasePseudoConsole` is handled correctly where available.
- [ ] WSL execution uses the active-user path and does not require a permanent user helper.

## Phase 5 — service/worker IPC

- [ ] short-lived active-session worker can be launched on demand.
- [ ] StreamJsonRpc named-pipe control path works bidirectionally.
- [ ] events and cancellation work across the worker boundary.
- [ ] multiplexed bulk channels handle stdout/stderr/VT/image/audio/file data.
- [ ] worker crash/exit does not require restarting the LocalSystem service.
- [ ] no permanent desktop/session daemon is introduced without measured justification.

## Phase 6 — desktop observation/control

- [ ] HWND/process/window inventory is available.
- [ ] UIA uses cache requests for only needed properties/patterns.
- [ ] UIA event subscriptions provide changed-state observation.
- [ ] UIA Remote Operations are used where they reduce cross-process round trips.
- [ ] Windows.Graphics.Capture supports efficient window/screen capture.
- [ ] dirty-region updates are used where practical.
- [ ] Per-Monitor V2 DPI awareness is established before coordinate-sensitive work.
- [ ] OCR/visual grounding remains a fallback rather than the primary UI representation.
- [ ] secure desktop/lock state is reported accurately.

## Phase 7 — browser

- [ ] installed Chrome launches as active user.
- [ ] dedicated Eye profile/data directory is used.
- [ ] CDP binds only where intended, normally loopback.
- [ ] typed CDP bindings are generated from the protocol schema.
- [ ] target/tab/navigation/evaluation/input/network/download/upload/screenshot primitives work.
- [ ] browser remains usable without Playwright installed.
- [ ] optional Playwright .NET path can be used for tasks where it materially improves reliability.
- [ ] no permanent Node daemon or bundled browser fleet is required.

## Phase 8 — high-value Windows-native capabilities

Add each only with a concrete workload and an explicit contract revision where publication is required.

Potential capability families:

- [ ] BITS transfers: `transfer.start/status/wait/cancel`.
- [ ] VSS consistent snapshots/reads.
- [ ] Restart Manager locker inspection/coordination.
- [ ] ReFS block-clone workspace snapshots/clones.
- [ ] CopyFile2 progress/cancel-aware copies.
- [ ] Process Snapshotting diagnostics.
- [ ] Virtual Disk attach/detach/inspect.
- [ ] ProjFS only if a large lazy-materialization workload appears.

## Phase 9 — code/document/data/media adapters

Add based on actual tasks, not completeness theater.

- [ ] ripgrep baseline.
- [ ] Tree-sitter and/or ast-grep when syntax-aware operations are needed.
- [ ] on-demand LSP adapters for symbols/references/rename/diagnostics.
- [ ] MarkItDown/PdfPig/Open XML/ClosedXML for document work.
- [ ] Docling only for heavier extraction needs.
- [ ] DuckDB for structured local data queries.
- [ ] NAudio for native audio capture.
- [ ] whisper.cpp for on-demand transcription.
- [ ] PaddleOCR/Tesseract and ONNX/OpenCV for on-demand vision/OCR.
- [ ] no always-running local model unless a measured workload later proves the need.

## Phase 10 — updates and measurement

- [ ] staged/atomic update path exists.
- [ ] failed update can roll back cleanly.
- [ ] service-aware restart/cutover is reliable.
- [ ] EyeBench contains a small representative set of real tasks.
- [ ] measure task success, elapsed time, Eye calls, retries/restarts, bytes transferred and required user intervention.

## Phase 11 — final runtime cutover

Only perform after v2 independently operates the machine.

- [ ] v2 service survives cold reboot.
- [ ] Secure MCP Tunnel reconnects to v2 loopback endpoint.
- [ ] `eye_inspect` works.
- [ ] `eye_run` works in SYSTEM/user/WSL contexts.
- [ ] active-session worker can be created/destroyed repeatedly.
- [ ] desktop observation/control works.
- [ ] browser/CDP works.
- [ ] file/code operations work.
- [ ] structured errors and cancellation behave correctly.
- [ ] machine-secret persistence works.
- [ ] worker/service/tunnel failure recovery is understood.
- [ ] switch the production tunnel target to v2.
- [ ] observe normal operation before removing compatibility mechanisms.
- [ ] remove obsolete prototype service/runtime pieces.
- [ ] remove transitional session helper/task if still present.
- [ ] remove temporary compatibility paths.
- [ ] reboot from cold state and prove the final architecture end to end.

## Final success state

```text
ChatGPT
   |
OpenAI Secure MCP Tunnel
   |
Eye LocalSystem Service
   |
   +-- five generated MCP facades
   +-- one internal operation registry/dispatcher
   +-- native SYSTEM capabilities
   +-- active-user process execution
   +-- on-demand desktop worker
   +-- installed Chrome / typed CDP
   +-- WSL
   +-- Windows native facilities
   +-- on-demand specialized engines
```

No VPS dependency.  
No HEC dependency.  
No permanent session daemon.  
No generic agent runtime.  
No Docker/Kubernetes base.  
No permanent Node automation service.  
No competing MCP servers.
