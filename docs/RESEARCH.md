# RESEARCH.md

**Status:** Current implementation research that materially affects Eye architecture  
**Baseline date:** 2026-08-07

This file records vendor/API findings and architecture implications. The broader open-source landscape remains in `OSS_LANDSCAPE.md`.

A finding becomes canonical only when explicitly promoted into `EYE_CANON.md`, `EYE_DECISIONS.md`, or `BUILD_BLUEPRINT.md`.

## 1. Overall synthesis

Current research supports one compact conclusion:

> ChatGPT supplies intelligence and planning. Eye supplies durable privileged machine capability. External tools supply specialized engines on demand.

Eye should not embed a second autonomous-agent framework, generic workflow engine, container platform, permanent Node daemon, always-running local planner, or collection of competing MCP servers.

The implementation bottleneck is no longer missing libraries. It is durable execution, event waiting, artifact transport, state identity/deltas, continuation, and self-repair.

## 2. Final model-facing surface

The original universal `eye(op, arbitrary JSON)` shape was too weakly typed and mixed unrelated effect classes.

The intermediate v1 five-tool design improved effect classification but did not provide the continuation UI boundary.

The accepted v2 surface is:

```text
eye_inspect
eye_run
eye_change
eye_interact
eye_external
eye_live
```

The first five are effect-class capability facades. `eye_live` exists solely to mount the optional continuation/mission UI and performs no machine operation itself.

`wait` and `transfer` remain operation families beneath the facades rather than becoming seventh/eighth top-level tools.

Canonical target contract:

```text
contracts/eye-mcp-v2.json
```

The durable schema fix is one generated contract source plus exact DTOs/descriptors/output schemas/registration/server instructions and a normalized `tools/list` snapshot.

## 3. Current OpenAI MCP/plugin findings

Current official documentation supports the final UI/contract direction:

- ChatGPT Developer mode provides full MCP client support for read and write tools in conversations.
- MCP initialization `instructions` are consumed alongside tool metadata for cross-tool guidance; official guidance says to keep the first 512 characters self-contained.
- New MCP UI should associate a tool with `_meta.ui.resourceUri` and use the standard MCP Apps `ui/*` JSON-RPC bridge.
- `ui/message` is the standard bridge method for a component to send a follow-up message.
- `_meta.ui.visibility` controls whether a helper tool is available to the model, the app, or both; app-only helpers can therefore remain out of model tool selection.
- `_meta["openai/fileParams"]` identifies top-level file inputs and passes file objects containing download/file identity metadata.
- UI is explicitly optional; tools should remain useful without a component.

Primary references:

- https://developers.openai.com/api/docs/guides/developer-mode
- https://developers.openai.com/plugins/build/chatgpt-ui
- https://developers.openai.com/plugins/reference
- https://developers.openai.com/plugins/changelog

Implementation implication: Eye Live can be a small optional continuation surface without contaminating every capability tool or becoming a requirement for core Eye operation.

## 4. Stable host / separate capability-engine process

A logical module boundary is insufficient for the only remote control path. If evolving native UIA/CDP/media/GPU feature code is loaded into the LocalSystem host process, a native crash or memory-corruption bug can still destroy MCP connectivity.

Canonical implication: one SCM service contains a tiny stable host and supervises a **separate versioned capability-engine child process**.

The stable host owns repair-critical primitives; the engine owns replaceable feature logic.

Engine activation requires an independently versioned protocol and contract-hash handshake. A/B version selection preserves a previous engine and permits rollback without replacing host-owned jobs, terminals, artifacts, triggers, or mission state.

## 5. Durable jobs replace request-bound execution

The current early `ProcessRunner` style has several limitations for real agent work:

- short fixed timeout behavior;
- whole-output buffering;
- no durable attach/resume model;
- no persistent interactive terminal;
- weak behavior across conversation/tunnel disconnects;
- cancellation semantics that are not yet uniformly process-tree-owned.

Implementation implication:

- operations get a short fast-completion window;
- long work automatically returns a `job_id`;
- Job Objects remain host-owned;
- stdout/stderr/VT output is incrementally spooled;
- reads use sequence/cursor semantics;
- native ConPTY terminals remain host-owned;
- job metadata persists in a tiny host-owned SQLite database;
- engine replacement does not kill host-owned work.

A host crash cannot preserve live Win32 handles/ConPTY; recovered metadata must be marked interrupted rather than pretending otherwise.

## 6. Native event waits and Trigger Broker

Polling wastes tool calls, latency, and context when the OS/application can signal a condition directly.

High-value wait sources include:

- job/process exit;
- output/log/terminal patterns;
- file creation/change/unlock;
- service/port/session state;
- Event Log/WMI/device/power/network/idle events;
- window/UIA state;
- Chrome/CDP navigation/DOM/network/download events;
- CPU/GPU thresholds;
- time conditions;
- first-of-many conditions.

Canonical implication: the stable host owns durable trigger registrations/queues; engine watchers feed it. This remains an event primitive, not a workflow engine.

## 7. Artifact transport

Large output should not be serialized into giant MCP results.

Artifacts provide stable identity plus metadata such as kind, MIME type, size, hash, name, storage tier, and provenance.

Use them for terminal output, screenshots/recordings, audio, dumps/traces, archives, browser downloads, generated documents, query results, and ChatGPT-imported files.

Hot data can live on `X:` and cold/bulk data on `E:` while authoritative metadata remains under SYSTEM-owned `C:\ProgramData\StealthEye` state.

## 8. Stable identity and delta observation

Canonical identity model:

```text
stable object ID + incarnation generation + observation cursor
```

The distinction prevents PID/HWND/path reuse and object replacement from being confused with ordinary mutable state.

Use cursor/delta observation for terminal output, UIA patches, dirty screen regions, browser state, filesystem changes, and process/window additions/removals.

Implementation implication: Eye stores full working state locally and sends ChatGPT only the state required for the next decision.

## 9. LocalSystem -> interactive-user execution

Microsoft documents `WTSQueryUserToken` for obtaining a logged-on user's primary token from a trusted LocalSystem service. `CreateEnvironmentBlock` supplies the user environment and `CreateProcessAsUser` launches into the interactive context.

Primary references:

- https://learn.microsoft.com/en-us/windows/win32/api/wtsapi32/nf-wtsapi32-wtsqueryusertoken
- https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-createprocessasusera
- https://learn.microsoft.com/en-us/windows/win32/api/userenv/nf-userenv-createenvironmentblock

Prior live Eye experiments demonstrated this path with captured output.

Implementation implication: active-user execution is host-owned native service capability; a permanent logon/session daemon is not required.

## 10. Process containment, handles, and ConPTY

Permanent process substrate combines:

- service-owned Job Objects;
- explicit inherited-handle lists;
- asynchronous output/exit supervision;
- consistent cancellation/descendant cleanup;
- native ConPTY;
- current ConPTY lifetime APIs.

Microsoft `ReleasePseudoConsole` is available on current supported Windows versions and was previously observed on the target machine's tested build.

Reference:

- https://learn.microsoft.com/en-us/windows/console/releasepseudoconsole

Microsoft Terminal remains a high-value behavior reference for ConPTY/VT/handle lifetime without becoming an Eye runtime dependency.

## 11. CsWin32

The prototype proved required Win32 paths with handwritten P/Invoke. Permanent interop favors `microsoft/CsWin32` generated bindings/SafeHandles wherever practical.

This reduces handwritten signature/struct drift while preserving a native C#/.NET design.

## 12. Host/engine/worker IPC

Favored control/data split:

- StreamJsonRpc over named pipes for typed control calls, events, and cancellation;
- Nerdbank.Streams-style multiplexed binary channels for stdout/stderr/VT/images/audio/files.

JSON remains the control plane rather than a universal base64/binary framing system.

Short-lived session workers remain on demand. Host owns lifecycle/IPC; active-engine version supplies worker behavior.

## 13. Desktop observation

Preferred structural-first stack:

1. HWND/process/window inventory;
2. UI Automation cache requests;
3. UIA events;
4. UIA Remote Operations;
5. Windows.Graphics.Capture with dirty-region updates;
6. OCR/visual grounding only where structure is unavailable.

Primary references:

- https://learn.microsoft.com/en-us/uwp/api/windows.ui.uiautomation.core.coreautomationremoteoperation
- https://learn.microsoft.com/en-us/windows/win32/api/uiautomationclient/nn-uiautomationclient-iuiautomationcacherequest
- https://learn.microsoft.com/en-us/windows/win32/winauto/uiauto-howto-implement-event-handlers

Implementation implication: stable structured references and deltas should replace repeated full-tree/full-screen observation wherever possible.

## 14. Chrome/CDP

Chrome remote debugging is correctly isolated using a dedicated non-standard user-data directory.

Reference:

- https://developer.chrome.com/blog/remote-debugging-port

Canonical browser shape:

- installed Chrome;
- active-user process;
- dedicated Eye profile/data directory;
- loopback CDP;
- generated typed CDP bindings;
- optional Playwright .NET acceleration only where it materially helps.

No permanent Node daemon or separately downloaded browser fleet.

## 15. Dev Drive / WSL / storage roles

Microsoft Dev Drive guidance supports a physical development partition where practical.

Reference:

- https://learn.microsoft.com/en-us/windows/dev-drive/

Canonical storage roles:

```text
C: Windows/apps/stable host state/encrypted secrets/engine metadata
X: physical ReFS Dev Drive/repos/hot workspaces/job spool/temporary artifacts/clones
E: bulk data/models/media/archives/large or cold artifacts
WSL filesystem: Linux-native permission-sensitive workloads
```

Tiny authoritative host metadata must not depend on `X:` existing.

## 16. High-leverage built-in Windows facilities

Windows already provides several valuable lifecycle/consistency primitives:

- BITS — reboot-resilient transfers;
- VSS — consistent reads/snapshots;
- Restart Manager — file-lock/process coordination;
- Process Snapshotting — diagnostic snapshots;
- ReFS block cloning — cheap copy-on-write workspace clones;
- CopyFile2 — progress/cancellation-aware copies;
- ProjFS — lazy materialization when a real large-tree workload requires it;
- Virtual Disk API — VHD/VHDX/ISO operations.

References:

- https://learn.microsoft.com/en-us/windows/win32/bits/background-intelligent-transfer-service-portal
- https://learn.microsoft.com/en-us/windows/win32/vss/volume-shadow-copy-service-portal
- https://learn.microsoft.com/en-us/windows/win32/rstmgr/restart-manager-portal
- https://learn.microsoft.com/en-us/windows/win32/api/processsnapshot/nf-processsnapshot-psscapturesnapshot
- https://learn.microsoft.com/en-us/windows/win32/fileio/block-cloning
- https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-copyfile2
- https://learn.microsoft.com/en-us/windows/win32/projfs/projected-file-system
- https://learn.microsoft.com/en-us/windows/win32/api/virtdisk/nf-virtdisk-attachvirtualdisk

Implementation implication: expose these through the existing facades as real workloads justify them rather than building parallel subsystems.

## 17. Code/documents/data/media

Accepted layered posture:

- code: ripgrep -> Tree-sitter/ast-grep -> on-demand language servers;
- documents: MarkItDown/PdfPig/Open XML/ClosedXML -> heavier Docling only when needed;
- data: embedded/on-demand DuckDB;
- audio: NAudio + short-lived whisper.cpp;
- OCR/vision: PaddleOCR/Tesseract + ONNX/OpenCV on demand;
- local GGUF/embedding/reranking only for measured workloads.

The local GPU should accelerate focused work rather than host a permanent duplicate planner.

## 18. SYSTEM-owned secret protection

Microsoft DPAPI-NG supports protection descriptors through CNG APIs.

Previous live validation with throwaway material established that `LOCAL=user` invoked by LocalSystem could protect/unprotect across reboot while the interactive user could not decrypt the same test blob.

Primary references:

- https://learn.microsoft.com/en-us/windows/win32/api/ncryptprotect/nf-ncryptprotect-ncryptcreateprotectiondescriptor
- https://learn.microsoft.com/en-us/windows/win32/api/ncryptprotect/nf-ncryptprotect-ncryptprotectsecret
- https://learn.microsoft.com/en-us/windows/win32/api/ncryptprotect/nf-ncryptprotect-ncryptunprotectsecret

Canonical implication: use DPAPI-NG `LOCAL=user` from the LocalSystem host and store only encrypted blobs/non-secret metadata under a SYSTEM-owned path.

## 19. Transport and standalone behavior

The transport architecture remains:

```text
tunnel-client -> loopback Eye MCP endpoint
```

Eye does not own OpenAI tunnel lifecycle and does not require HEC/VPS infrastructure.

A tunnel/ChatGPT disconnect must not stop host-owned local jobs, terminals, triggers, artifacts, mission state, or engine supervision.

## 20. Research discipline

Before implementing an OS/provider-sensitive capability:

1. check current primary vendor documentation;
2. compare with a minimal live probe where practical;
3. record surprising constraints or useful APIs here;
4. use `OSS_LANDSCAPE.md` for broad dependency comparisons;
5. promote findings into canon only after explicit owner acceptance.

After the final blueprint freeze, research should normally determine **how a capability fits beneath the existing architecture**, not propose another architectural layer.
