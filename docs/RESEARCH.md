# RESEARCH.md

**Status:** Current implementation research that materially affects Eye architecture  
**Baseline date:** 2026-08-07

This file records vendor/API findings and architecture implications. The broader open-source project landscape is maintained separately in `OSS_LANDSCAPE.md`.

A finding becomes canonical only when explicitly promoted into `EYE_DECISIONS.md` / `EYE_CANON.md`.

## 1. Overall synthesis

The current research supports a compact design:

> ChatGPT supplies intelligence and planning. Eye supplies reliable privileged machine capabilities. External tools supply specialized engines on demand.

The research does **not** support embedding another autonomous-agent framework, workflow engine, plugin marketplace, container platform, permanent Node daemon, always-running local model, or collection of competing MCP servers.

## 2. MCP contract and effect-class surface

The previous universal model-facing shape:

```text
eye(op: string, args: arbitrary JSON)
```

has three practical weaknesses:

1. operation names are unconstrained;
2. operation-specific arguments are not represented in the schema;
3. the single tool mixes read-only observation, raw execution, local mutation, interaction and external effects under one metadata classification.

The accepted architecture is now five model-facing facades:

```text
eye_inspect
eye_run
eye_change
eye_interact
eye_external
```

with one internal dispatcher/registry beneath them.

The durable fix for schema drift is a versioned canonical contract plus generated public metadata/types/registration and a normalized `tools/list` snapshot test.

Canonical source:

```text
contracts/eye-mcp-v1.json
```

See `MCP_CONTRACT.md`.

## 3. LocalSystem -> interactive-user process launch

Microsoft documents `WTSQueryUserToken` as obtaining the primary access token of a logged-on user for a specified session. The API is designed for highly trusted services running with the required LocalSystem privileges.

`CreateEnvironmentBlock` supplies the selected user's environment and `CreateProcessAsUser` supports launching into an interactive desktop.

Primary references:

- https://learn.microsoft.com/en-us/windows/win32/api/wtsapi32/nf-wtsapi32-wtsqueryusertoken
- https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-createprocessasusera
- https://learn.microsoft.com/en-us/windows/win32/api/userenv/nf-userenv-createenvironmentblock

Prior live Eye experiments demonstrated a genuine LocalSystem SCM service launching an active-user child with captured stdout/stderr.

Implementation implication: active-user execution is a native service-owned responsibility; a permanent logon/session daemon is not required merely to cross the session boundary.

## 4. Process containment, handles and terminal lifetime

The permanent process substrate should combine:

- explicit process/token/session launch semantics;
- service-owned Job Objects;
- explicit inherited-handle lists rather than accidental broad inheritance;
- asynchronous stdout/stderr/exit supervision;
- native ConPTY;
- cancellation/timeout/descendant cleanup.

Microsoft added `ReleasePseudoConsole` for current Windows versions. Previous target-machine probing confirmed the API was exported on the tested build.

Primary reference:

- https://learn.microsoft.com/en-us/windows/console/releasepseudoconsole

Implementation implication: design around the current ConPTY lifecycle rather than inheriting older prototype shutdown assumptions.

The Microsoft Terminal source is a high-value behavioral reference for ConPTY, VT and handle-lifetime details without becoming an Eye dependency.

## 5. CsWin32 as permanent interop direction

The prototype proved required Win32 paths with handwritten P/Invoke. Current research favors replacing those declarations with `microsoft/CsWin32` generated bindings/SafeHandles wherever practical.

Reasons:

- reduces handwritten struct/signature drift;
- improves SafeHandle usage;
- provides broad Windows metadata coverage without adopting a large runtime framework;
- fits the C#/.NET core.

Handwritten declarations remain acceptable for narrow gaps that generation does not handle cleanly.

## 6. Service/worker IPC

Desktop and other active-session functionality should remain short-lived/on-demand rather than becoming a permanent user daemon.

Favored split:

- **StreamJsonRpc over named pipes** for typed control calls, events and cancellation;
- **Nerdbank.Streams-style multiplexing** for bulk stdout/stderr/VT/image/audio/file streams.

Implementation implication: keep JSON as the control plane and avoid inventing a universal base64/JSON framing layer for high-volume binary data.

## 7. Desktop observation

The research strongly favors an event-driven structural-first desktop stack:

1. native HWND/process/window inventory;
2. UI Automation cache requests;
3. UIA events;
4. UIA Remote Operations to reduce cross-process round trips;
5. Windows.Graphics.Capture with dirty-region-aware updates;
6. OCR/visual grounding only for content not represented structurally.

Primary references:

- https://learn.microsoft.com/en-us/uwp/api/windows.ui.uiautomation.core.coreautomationremoteoperation
- https://learn.microsoft.com/en-us/windows/win32/api/uiautomationclient/nn-uiautomationclient-iuiautomationcacherequest
- https://learn.microsoft.com/en-us/windows/win32/winauto/uiauto-howto-implement-event-handlers

Implementation implication: Eye should increasingly transmit compact changed state rather than repeatedly scraping full accessibility trees or shipping entire screenshots.

## 8. Chrome remote debugging and CDP

Chrome changed remote-debugging behavior for the normal user data directory; a dedicated non-standard `--user-data-dir` is the correct automation isolation model.

Primary reference:

- https://developer.chrome.com/blog/remote-debugging-port

The canonical browser design remains:

- installed system Chrome;
- active-user process;
- dedicated Eye profile/data directory;
- loopback CDP;
- control from the LocalSystem service.

Permanent primitive: generated typed bindings from `ChromeDevTools/devtools-protocol`.

Playwright .NET is optional higher-level acceleration rather than the base runtime.

## 9. Dev Drive / ReFS / WSL roles

Microsoft Dev Drive guidance supports a physical partition as the preferred low-overhead development volume when practical.

Primary reference:

- https://learn.microsoft.com/en-us/windows/dev-drive/

The same ecosystem has filesystem-semantic differences relevant to WSL; Linux-native permission/ownership-sensitive workloads should live in the WSL Linux filesystem rather than assuming ReFS behaves like ext4.

Canonical storage roles remain:

```text
C: Windows/apps
X: physical ReFS Dev Drive / repos/build workspace
E: bulk data/models/archives/artifacts
WSL filesystem: Linux-native workloads
```

## 10. Built-in Windows capabilities with high leverage

Several of the most useful future Eye capabilities already exist as Windows facilities.

### BITS

Reboot-resilient background transfers with progress and cancellation.

Reference:

- https://learn.microsoft.com/en-us/windows/win32/bits/background-intelligent-transfer-service-portal

Candidate operations:

```text
transfer.start
transfer.status
transfer.wait
transfer.cancel
```

### VSS

Consistent reads/snapshots of locked or actively changing data.

Reference:

- https://learn.microsoft.com/en-us/windows/win32/vss/volume-shadow-copy-service-portal

### Restart Manager

Identify and coordinate applications holding resources/files.

Reference:

- https://learn.microsoft.com/en-us/windows/win32/rstmgr/restart-manager-portal

### Process Snapshotting

Consistent process/thread/handle/virtual-memory diagnostic snapshots.

Reference:

- https://learn.microsoft.com/en-us/windows/win32/api/processsnapshot/nf-processsnapshot-psscapturesnapshot

### ReFS block cloning

Cheap copy-on-write clone/snapshot behavior for supported ReFS files/workspaces.

Reference:

- https://learn.microsoft.com/en-us/windows/win32/fileio/block-cloning

### CopyFile2

Native cancellable/progress-reporting file-copy primitive.

Reference:

- https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-copyfile2

### ProjFS

Lazy materialization for very large trees when a real model/dataset/artifact workload justifies it.

Reference:

- https://learn.microsoft.com/en-us/windows/win32/projfs/projected-file-system

### Virtual Disk API

Native VHD/VHDX/ISO attach/inspect operations.

Reference:

- https://learn.microsoft.com/en-us/windows/win32/api/virtdisk/nf-virtdisk-attachvirtualdisk

Implementation implication: add typed Eye operations over these facilities as concrete workloads appear rather than building parallel subsystems.

## 11. Code intelligence

The research supports a layered approach:

- ripgrep as the default raw search;
- Tree-sitter for incremental syntax structure;
- ast-grep for structural search/rewrites;
- language servers spawned only for relevant workspaces.

The Language Server Protocol can support stable capability-shaped operations such as symbols, references, rename and diagnostics without embedding IDEs or a permanent indexing service.

Large-corpus engines such as Zoekt/SCIP remain later decisions after ordinary search becomes measurably insufficient.

## 12. Documents and data

Favored small/on-demand stack:

- MarkItDown for common extraction;
- PdfPig for native C# PDF text/geometry;
- Open XML SDK for Office packages;
- ClosedXML for higher-level Excel work;
- Docling only when heavier layout/table/OCR extraction is actually needed;
- DuckDB for embedded SQL over CSV/JSON/Parquet/logs/artifacts.

Vector databases/indexes remain unnecessary until semantic retrieval proves a real workload advantage.

## 13. Audio, OCR and local inference

Favored on-demand stack:

- NAudio for microphone/system/process-loopback audio;
- whisper.cpp for local transcription;
- PaddleOCR with Tesseract fallback;
- ONNX Runtime for focused model inference;
- OpenCV for ordinary CV/preprocessing;
- llama.cpp only for occasional local embedding/reranking/small inference needs.

Implementation implication: exploit the GPU for short-lived focused workloads, not as an excuse to run a second permanent planner.

## 14. SYSTEM-owned secret protection

Microsoft CNG DPAPI/DPAPI-NG supports protection descriptors and `NCryptProtectSecret` / `NCryptUnprotectSecret`.

Primary references:

- https://learn.microsoft.com/en-us/windows/win32/api/ncryptprotect/nf-ncryptprotect-ncryptcreateprotectiondescriptor
- https://learn.microsoft.com/en-us/windows/win32/api/ncryptprotect/nf-ncryptprotect-ncryptprotectsecret
- https://learn.microsoft.com/en-us/windows/win32/api/ncryptprotect/nf-ncryptprotect-ncryptunprotectsecret
- https://learn.microsoft.com/en-us/windows/win32/seccng/cng-dpapi-constants

Previous live validation with throwaway material established:

- `LOCAL=user`, invoked by LocalSystem, could protect/unprotect;
- the encrypted blob remained decryptable by LocalSystem after reboot;
- the interactive user could read the blob but could not decrypt it;
- a direct `SID=S-1-5-18` descriptor did not successfully protect in the tested environment.

Canonical implication: use DPAPI-NG `LOCAL=user` from the LocalSystem Eye service and persist only encrypted blobs/non-secret metadata in a SYSTEM-owned location.

## 15. OpenAI Secure MCP Tunnel

The transport architecture remains:

```text
tunnel-client -> loopback Eye MCP endpoint
```

Eye should not own the tunnel lifecycle or grow a custom remote-access subsystem when the official transport is sufficient.

The public MCP schema, effect metadata and tool descriptions should be treated as a first-class model-facing API rather than documentation-only hints. This is the reason the five-tool generated contract was promoted to canonical architecture.

## 16. Open-source landscape

The detailed adopt/external/reference/later/isolate inventory is in:

```text
docs/OSS_LANDSCAPE.md
```

Key accepted outcomes:

- CsWin32 is the preferred binding layer.
- StreamJsonRpc + multiplexed streams are the preferred worker IPC split.
- raw generated CDP is the permanent browser primitive.
- UIA events/cache/Remote Operations plus dirty capture are the preferred desktop observation model.
- external/on-demand tools are preferred to embedding heavyweight runtimes.
- agent frameworks are sources of ideas, not Eye runtimes.

## 17. Licensing research discipline

Before copying third-party source or distributing a linked dependency:

1. pin the exact revision/version;
2. inspect the actual license for that revision and relevant subdirectories/assets;
3. preserve required notices;
4. keep GPL/AGPL or unclear-license implementations isolated unless obligations are deliberately accepted;
5. remember that model/data licenses can differ from code licenses.

A future `THIRD-PARTY-NOTICES` file plus pinned dependencies is the favored minimal legal mechanism.

## 18. Research discipline

Before implementing an OS/provider-specific capability:

1. check current primary vendor documentation;
2. compare it with a minimal live probe where practical;
3. record surprising constraints or useful APIs here;
4. use `OSS_LANDSCAPE.md` for broad project/dependency comparisons;
5. promote a result to `EYE_DECISIONS.md` / `EYE_CANON.md` only after explicit acceptance.

This keeps research useful without allowing browsing or attractive repositories to turn into speculative architecture.
