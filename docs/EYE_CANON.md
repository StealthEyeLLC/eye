# EYE_CANON.md

**Status:** Canonical source for the Eye / StealthEye ChatGPT project  
**Baseline date:** 2026-08-07  
**Product:** StealthEye  
**Repository:** `StealthEyeLLC/eye`  
**Local repository:** `X:\Repos\eye`  
**Primary executable:** `eye.exe`

## 1. Purpose

StealthEye is a laptop-native privileged capability substrate built specifically so ChatGPT can operate the dedicated StealthEye Windows machine with broad, predictable authority.

The primary optimization target is reliable machine operation by ChatGPT. Eye supplies machine capabilities; ChatGPT supplies intelligence, planning, and task orchestration.

Eye must not grow a second autonomous-agent runtime merely because agent frameworks exist.

## 2. Core invariant: no avoidable authority friction

When the owner intentionally grants an authority to Eye, StealthEye itself should preserve that authority rather than voluntarily downscoping it.

StealthEye should not add internal approval layers, privilege tiers, narrow allowlists, redundant confirmation mechanisms, or artificial capability wrappers merely to constrain already-granted authority.

The intended test is:

> If ChatGPT knows what needs to be done and the owner has already granted the underlying authority, can Eye just do it?

The intended answer is normally yes.

This does **not** mean StealthEye can bypass controls imposed by Windows secure desktop, OpenAI, providers, hardware, networking, or the execution environment. It means StealthEye should not manufacture extra friction on top of those real boundaries.

## 3. Public MCP interface: five effect-class facades

The previous one-tool `eye({ op, args })` public design is superseded.

The canonical model-facing MCP surface is:

```text
eye_inspect
eye_run
eye_change
eye_interact
eye_external
```

| Tool | Purpose | Effect class |
| --- | --- | --- |
| `eye_inspect` | Local status, files, processes, windows, UIA state, screenshots and diagnostics | Read-only/local |
| `eye_run` | Windows/WSL/process/PowerShell/ConPTY execution | Raw local execution |
| `eye_change` | Precisely typed local machine/file/service/storage/configuration changes | Local write |
| `eye_interact` | Desktop, application and browser interaction | Interactive |
| `eye_external` | Uploading, sending, posting, remote-provider administration and other effects leaving the machine | Open-world/external |

This split exists to give ChatGPT accurate names, schemas, results and effect metadata. **It is not a privilege hierarchy.** All five route to the same owner-authorized capability substrate. `eye_run` remains the raw escape hatch when a narrower typed operation is not available.

Internally, Eye keeps one operation registry/dispatcher. CLI/internal compatibility may continue to use `(op, args)` representations.

The canonical versioned contract is:

```text
contracts/eye-mcp-v1.json
```

Public descriptors, C# request/result types, operation registration, capabilities, documentation and normalized `tools/list` snapshots should be generated from that source.

Ordinary implementation work must not silently alter the public contract. Breaking public changes require an explicit owner-authorized contract revision/version.

See `docs/MCP_CONTRACT.md` and `AGENTS.md`.

## 4. Target request path

```text
ChatGPT
  -> OpenAI
  -> Secure MCP Tunnel
  -> tunnel-client on STEALTHEYELLC
  -> loopback MCP endpoint
  -> eye.exe Windows service
```

The Secure MCP Tunnel is transport only.

`eye.exe` must remain independently useful on the laptop without understanding or owning OpenAI tunnel lifecycle.

HEC, VPS, SEZU, Caddy, Incus, Docker, Tailscale and SSH are not required parts of the target Eye request path.

## 5. Permanent process topology

Target permanent runtime:

```text
Windows Service Control Manager
  -> eye.exe service (LocalSystem)
```

There should be **one permanent Eye Windows service** running as LocalSystem.

The final architecture does not require a permanent user-session daemon, tray process, logon helper or permanent Node/browser automation service.

The service owns machine execution and creates user-session workers/processes on demand.

## 6. Native interop foundation

C# / .NET is the core implementation language.

Use **CsWin32-generated bindings and SafeHandles** as the preferred permanent Win32/COM interop layer. The existing handwritten `NativeMethods.cs` proved the architecture and can be replaced incrementally.

Prefer Windows/.NET native facilities where they are sufficient before adding a third-party abstraction.

## 7. Active-user execution

For operations that need the logged-in interactive user, the LocalSystem service uses the supported native Windows path:

- identify the active session;
- obtain its user token with `WTSQueryUserToken`;
- construct its environment with `CreateEnvironmentBlock`;
- launch with `CreateProcessAsUser`;
- use explicit inherited-handle lists for intended stdio/IPC handles;
- place the actual child in a service-owned Job Object;
- supervise process lifetime, descendants, cancellation, timeouts and exit status directly.

Prior live experiments proved that a genuine LocalSystem SCM service can launch an active-session process owned by the interactive user and capture its output.

A permanent user helper is not required merely to cross from LocalSystem into the active session.

## 8. Process and terminal execution

Native building blocks include:

- `CreateProcessAsUser`;
- Windows Job Objects;
- asynchronous pipe/process supervision;
- explicit inherited-handle lists;
- native ConPTY (`CreatePseudoConsole`);
- current ConPTY lifetime APIs including `ReleasePseudoConsole` where appropriate;
- process handles and exit codes.

ConPTY is the canonical pseudoterminal primitive. Do not carry Pty.Net into v2 unless a concrete missing capability proves native ConPTY insufficient.

`eye_run` remains the broad execution escape hatch for Windows SYSTEM/user and WSL contexts.

## 9. WSL

WSL is launched through the active user's token from the LocalSystem service.

A permanent WSL or user helper is not required.

Target distro baseline:

```text
Ubuntu 24.04 LTS
WSL2
systemd enabled
root default user
```

Linux-native workloads that depend on Unix permission/ownership semantics belong inside the WSL Linux filesystem rather than relying on ReFS-hosted metadata behavior.

## 10. Service/worker IPC

Desktop-bound and other active-session worker functionality uses short-lived `eye.exe worker` processes created on demand by the LocalSystem service.

Preferred control path:

- **StreamJsonRpc over named pipes** for typed calls, events and cancellation;
- **Nerdbank.Streams-style multiplexed binary channels** for bulk streams such as stdout, stderr, VT terminal data, screenshots, audio and files.

JSON is the control plane, not the universal bulk-data framing format.

Do not keep a permanent interactive worker alive unless measurements later prove a real performance requirement.

## 11. Desktop observation and interaction

The canonical observation hierarchy is:

1. native HWND/process/window inventory;
2. event-driven Windows UI Automation using cache requests and Remote Operations;
3. Windows.Graphics.Capture with efficient per-window/screen snapshots and dirty-region updates;
4. OCR or heavier visual grounding only when structural accessibility information is unavailable.

This hierarchy is intended to avoid repeatedly shipping full screenshots or scraping complete UI trees when a smaller structured state change is available.

Workers opt into Per-Monitor V2 DPI awareness before coordinate-sensitive work.

Secure desktop/lock state remains a real Windows boundary. Eye should report that state accurately rather than pretending inaccessible desktop content is available.

## 12. Browser

Browser architecture:

- use installed system Chrome;
- launch it as the active user;
- use a dedicated StealthEye browser profile/data directory;
- bind Chrome DevTools Protocol to loopback;
- control it from the LocalSystem service through **generated typed CDP bindings**.

Raw CDP is the permanent primitive.

Playwright .NET is an optional accelerator for sites/tasks where its locator, wait, download or trace behavior materially helps. It must not become a required permanent Node daemon or browser fleet.

The ordinary human browser/profile remains separate from Eye's dedicated automation profile.

## 13. High-value Windows-native capability families

Eye should expose Windows facilities when real workloads justify them rather than reimplementing their lifecycle behavior.

Favored facilities include:

- BITS for reboot-resilient transfers;
- VSS for consistent reads/snapshots of changing data;
- Restart Manager for identifying/coordinating file lockers;
- Process Snapshotting for process/thread/handle/memory diagnostics;
- ReFS block cloning for cheap workspace snapshots/clones on `X:`;
- CopyFile2 for cancellable/progress-aware native copies;
- ProjFS for lazy materialization if a large-tree use case appears;
- Virtual Disk API for VHD/VHDX/ISO operations;
- UIA Remote Operations/cache requests/events for efficient desktop state.

Candidate operation families include:

```text
transfer.start/status/wait/cancel
fs.snapshot/clone/copy/lockers
disk.attach/detach/inspect
process.snapshot
ui.observe/subscribe/query/act
```

Candidate names are not automatically published contract entries. Publication requires a contract revision.

## 14. Code intelligence

Keep code capability layered and on demand:

1. `rg` for default raw search;
2. Tree-sitter/ast-grep for syntax-aware structure and mechanical rewrites;
3. language servers only for relevant workspaces.

Generic capability-shaped operations may include:

```text
code.symbols
code.references
code.rename
code.diagnostics
```

Do not introduce a permanent heavyweight indexing service until actual repository scale makes it necessary.

## 15. Documents and structured data

Favored adapters as workloads appear:

- MarkItDown for common document extraction;
- PdfPig for native C# PDF text/geometry;
- Open XML SDK for Office packages;
- ClosedXML for high-level spreadsheet work;
- Docling only for heavier layout/table/OCR extraction;
- DuckDB for embedded/on-demand querying of CSV, JSON, Parquet, logs and artifacts.

LibreOffice/Pandoc and similar broad converters should remain external processes where useful rather than linked into Eye.

## 16. Audio, vision and local GPU work

Favored on-demand building blocks:

- NAudio for microphone/system/process-loopback capture;
- whisper.cpp for short-lived transcription;
- PaddleOCR with Tesseract fallback;
- ONNX Runtime for local inference across suitable backends;
- OpenCV for ordinary preprocessing/matching/CV work;
- llama.cpp only for occasional measured local inference/embedding/reranking needs.

Do not reserve GPU memory permanently for an always-running local planner. The local GPU should accelerate focused workloads, not duplicate ChatGPT.

## 17. External engines

Eye gains breadth by launching focused tools on demand rather than absorbing their runtimes.

Examples include:

- PowerShell;
- winget;
- git/GitHub CLI;
- ripgrep;
- ast-grep;
- language servers;
- FFmpeg;
- MarkItDown/Docling;
- whisper.cpp;
- OCR engines;
- rclone/curl/OpenSSH;
- LibreOffice where broad conversion is needed.

External tools remain subordinate capability engines behind the Eye contract, not independent permanent MCP servers.

## 18. Updating and measurement

Use a staged/atomic update design, with VeloPack as the favored implementation direction unless later evidence changes that decision.

Build a small laptop-native **EyeBench** from real Eye tasks. Measure only practical outcomes:

- task success;
- elapsed time;
- Eye tool calls;
- retries/restarts;
- bytes transferred;
- required user intervention.

Benchmarking exists to guide performance engineering, not to create user-facing verification ceremony.

## 19. Transport

The OpenAI Secure MCP Tunnel remains external transport:

```text
tunnel-client
  -> 127.0.0.1:<Eye MCP port>

eye.exe
  -> loopback MCP
```

Do not build a custom tunnel subsystem into Eye unless a concrete missing requirement appears.

## 20. External authority

### OpenAI

Current GitHub Actions secret names supplied by the owner:

```text
EyeRuntime
OpenAIAdmin
```

`EyeRuntime` is the runtime/tunnel-side OpenAI credential. `OpenAIAdmin` is deliberately broad organization-admin authority.

No secret values belong in repository source or project documentation.

### GitHub

Canonical repository:

```text
StealthEyeLLC/eye
```

Machine-side GitHub authority may be selected later according to the authority actually needed. Do not choose a narrower repo credential merely for architectural neatness if broader authority is explicitly intended.

### Google identity

Operational Eye Google identity:

```text
StealthEye <stealtheye.eye@gmail.com>
```

The separate `stealtheye@stealtheye.io` mailbox remains distinct unless explicitly migrated later.

## 21. Windows login/account boundary

Windows login/account/autologon architecture is **not a current Eye implementation target**. Leave the existing login/account arrangement alone unless the owner explicitly requests a change.

Eye should depend on the existence of an active interactive session when a desktop operation requires one, not on speculative account-management machinery.

## 22. Storage roles

Canonical intended storage roles:

```text
C:  Windows / system / installed applications
X:  physical trusted ReFS Dev Drive / repos / build workspace
E:  bulk StealthEye data / models / archives / large artifacts
WSL Linux filesystem: Linux-native permission-sensitive work
```

The intended `X:` size is approximately 300 GiB on the internal NVMe when the platform is provisioned accordingly.

## 23. Machine secret persistence

For credentials that the LocalSystem Eye service must retain on the laptop, the validated preferred design is **DPAPI-NG `LOCAL=user` invoked by LocalSystem**.

Persist only encrypted blobs and non-secret metadata under a SYSTEM-owned machine path.

Previous live validation established that LocalSystem could protect/unprotect across reboot while the interactive user could not decrypt the same test blob. A direct `SID=S-1-5-18` descriptor failed in the tested environment and is not the chosen path.

No plaintext credential or recovery material belongs in source.

## 24. Dependency and licensing posture

Prefer permissive dependencies and verify exact pinned licenses before importing code.

General posture:

- MIT/Apache/BSD/MS-PL: normally suitable with notices;
- MPL: keep file-level obligations clear;
- LGPL: prefer external/dynamic use where appropriate;
- GPL/AGPL: isolate externally unless obligations are deliberately accepted;
- no usable license: do not copy implementation code.

Model weights, datasets and individual directories can have separate licensing.

The detailed dependency landscape is in `docs/OSS_LANDSCAPE.md`.

## 25. Things Eye is deliberately not

Do not turn Eye into:

- a generic workflow engine;
- an autonomous-agent framework;
- a plugin marketplace;
- a policy framework;
- an approval engine;
- a receipt/evidence bureaucracy;
- a generic multi-machine orchestration framework;
- a VPS-dependent system;
- a Docker/Kubernetes runtime;
- an always-running local-model system;
- a permanent Node daemon;
- a collection of competing MCP servers;
- a kernel-driver project without a proven hardware requirement.

## 26. Canonical build order

Current order:

```text
1. Freeze/generate the five-facade MCP contract and tools/list snapshot
2. Replace handwritten Win32 declarations with CsWin32
3. Establish StreamJsonRpc control IPC and multiplexed worker streams
4. Finish Job Object / active-user / ConPTY execution semantics
5. Build event-driven cached UIA plus dirty-region capture
6. Generate typed CDP bindings; add optional Playwright .NET
7. Add high-value Windows-native facilities as concrete needs appear
8. Add code/document/data/audio adapters based on actual workloads
9. Add whisper/OCR/semantic retrieval only on demand
10. Add staged atomic updating and EyeBench
11. Cut over from prototype/transitional runtime only after v2 independently proves reboot, desktop, browser, terminal and recovery behavior
```

Do not begin by porting the old `se` implementation wholesale.

## 27. Design style

Favor:

- directness;
- raw native authority;
- predictable machine structure;
- precise generated contracts;
- one internal dispatcher;
- durable operation;
- minimal permanent processes;
- minimal third-party runtime weight;
- clear transport/local-capability separation;
- measured growth based on real tasks.

Avoid architecture ceremony, speculative layers and framework gravity.
