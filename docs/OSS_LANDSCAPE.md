# OSS_LANDSCAPE.md

**Status:** High-value implementation landscape and dependency guidance  
**Baseline date:** 2026-08-07

This document records the current broad open-source landscape considered materially relevant to Eye. It is architecture input, not an instruction to import every project listed here.

The central conclusion is simple:

> Eye is strongest as a compact privileged capability layer operated by ChatGPT, not as another autonomous-agent framework.

## Legal reuse boundary

Eye currently has no repository `LICENSE` file. Before copying third-party implementation code or distributing Eye, verify the exact pinned revision and license.

General posture:

- MIT, Apache-2.0, BSD, and MS-PL: generally suitable with required notices.
- MPL-2.0: usable, but modified MPL-covered files remain under MPL terms.
- LGPL: prefer external invocation or dynamic linking where appropriate and comply with its terms.
- GPL/AGPL: ideas may be studied, but importing/linking code can introduce copyleft obligations; isolate externally unless deliberately accepted.
- CC-BY: useful for research material/models with attribution, but awkward as a software-code license.
- No license or inconsistent licensing: do not copy implementation code; clean-room the concept.
- Model weights, datasets, subdirectories, examples, and bundled assets can have licenses different from the repository root.

Minimal project legal machinery should eventually include a `THIRD-PARTY-NOTICES` file plus pinned dependency versions. Add heavier license-scanning machinery only when dependency volume justifies it.

References:

- https://www.copyright.gov/circs/circ33.pdf
- https://opensource.org/licenses
- https://docs.github.com/en/repositories/managing-your-repositorys-settings-and-features/customizing-your-repository/licensing-a-repository

## Recommended core stack

| Subsystem | Direction |
| --- | --- |
| ChatGPT interface | Official MCP C# SDK; five public effect-class facades over one internal dispatcher |
| Remote transport | Official OpenAI tunnel client, external to Eye |
| Win32 bindings | CsWin32-generated P/Invokes and SafeHandles |
| Service/worker control | StreamJsonRpc over named pipes |
| Bulk worker streams | Nerdbank.Streams multiplexed binary channels |
| Processes | Native Job Objects, async supervision, ConPTY, explicit inherited-handle lists |
| Desktop structure | Cached/event-driven UIA plus Remote Operations |
| Desktop pixels | Windows.Graphics.Capture with dirty-region awareness |
| Browser | Raw generated Chrome DevTools Protocol bindings; optional Playwright .NET |
| Code | ripgrep + Tree-sitter/ast-grep + on-demand language servers |
| Documents | MarkItDown, Open XML SDK, ClosedXML; Docling only when needed |
| Data | Embedded/on-demand DuckDB |
| Audio | NAudio capture plus whisper.cpp transcription |
| Local vision | PaddleOCR/Tesseract; heavier grounding only on demand |
| Updates | VeloPack-style staged atomic updates |
| Measurement | Small laptop-native EyeBench built from real Eye tasks |

## OpenAI and MCP

### Adopt / primary

- `modelcontextprotocol/csharp-sdk` — official MCP C# implementation and hosting direction.
- `openai/tunnel-client` — official outbound transport; keep external rather than rebuilding tunnel behavior inside Eye.

### Reference / development tooling

- `modelcontextprotocol/modelcontextprotocol` — protocol schemas and capability negotiation.
- `modelcontextprotocol/servers` — tool/resource/error-model examples.
- `modelcontextprotocol/inspector` — useful external contract-testing and inspection tool.
- `openai/openai-cua-sample-app` — mixed visual/programmatic computer-use harness patterns.
- `openai/codex` — terminal execution, session and tool ergonomics ideas.
- `openai/openai-agents-python` and `openai/openai-agents-js` — typed tools/handoffs/result patterns; do not embed their agent runtimes merely to duplicate ChatGPT.
- `openai/skills` — organization/progressive-disclosure reference; verify individual file licenses before copying.

The OpenAI side and the Eye side should remain cleanly separated: ChatGPT supplies intelligence/planning; Eye supplies machine capabilities; the secure tunnel supplies transport.

## Windows execution, IPC and system control

### Adopt

- `microsoft/CsWin32` — preferred generated Win32/COM binding layer; replace most handwritten `NativeMethods.cs` declarations over time.
- `microsoft/vs-streamjsonrpc` — bidirectional service/worker calls, events, cancellation, generated proxies.
- `dotnet/Nerdbank.Streams` — multiplex terminal streams, images, audio and file data over one underlying pipe without inventing framing.
- `velopack/velopack` — staged/atomic Windows updates and rollback.
- `App-vNext/Polly` — narrow use for tunnel/network retries only; never blindly replay arbitrary machine mutations.

### Adopt selectively / reference

- `dahall/Vanara` — broad Windows API coverage; use only modules that materially reduce custom COM/interop work.
- `UiPath/coreipc` — useful bidirectional IPC reference/alternative, though StreamJsonRpc is the favored Eye fit.
- `MessagePack-CSharp` or `Cysharp/MemoryPack` — later only if JSON serialization becomes measured overhead.
- `libgit2/libgit2sharp` — structured repository reads/diffs where valuable; retain raw `git` for full coverage.

### Reference / external

- `microsoft/win32metadata` — source metadata behind modern generated Windows bindings.
- `microsoft/terminal` — ConPTY lifecycle, handle inheritance, VT and terminal behavior.
- `lowleveldesign/process-governor` — Job Objects, completion ports, groups and limits.
- `winsiderss/systeminformer` — process/token/handle/thread/job/service inspection; do not adopt its driver as an Eye requirement.
- Google Project Zero sandbox attack-surface tools / NtCoreLib — advanced token/object diagnostics, external/reference only.
- `microsoft/PowerToys` — power requests, windows, keyboard/mouse, clipboard and capture patterns.
- Microsoft Windows classic/universal/AppSDK/DirectX samples — canonical native API patterns; verify exact sample licensing before copying.
- `microsoft/winget-cli` — invoke externally for package discovery/installation.
- `PowerShell/PowerShell` — invoke installed PowerShell; do not embed a separate engine into Eye.
- `microsoft/WSL` / `microsoft/wslg` — interop and diagnostics references.
- `microsoft/perfview`, `dotnet/diagnostics`, `microsoft/clrmd` — ETW/EventPipe/counters/dumps/managed inspection.
- `microsoft/BuildXL` — USN journal, content-addressed cache and incremental graph ideas; do not absorb its build engine.
- `microsoft/MSBuild` — external/structured build evaluation.
- `dotnet/runtime` / `dotnet/aspnetcore` — SafeHandle, pipelines, async I/O, streaming and host-lifetime patterns.
- `winsw/winsw` — service recovery/logging ideas; Eye still installs directly with SCM.
- `dahall/TaskScheduler` — optional external/native schedules and triggers without building a workflow engine.
- `murrayju/CreateProcessAsUser` and `dotnet/pinvoke` — active-session and STARTUPINFOEX references; CsWin32 remains preferred.

### Later only

- `microsoft/yarp` and `microsoft/msquic` — only if Eye develops a measured local proxy or non-MCP high-throughput transport requirement.
- `microsoft/Windows-driver-samples` — only if a real hardware requirement eventually demands a driver.

## Desktop automation and observation

### Favored stack

1. HWND/process/window inventory.
2. Event-driven UI Automation with cache requests and Remote Operations.
3. Windows.Graphics.Capture with dirty-region updates.
4. OCR/visual grounding only when structural UI information is unavailable.

This should reduce full-screen image transfer and repeated whole-tree scraping.

### Projects

- `microsoft/Microsoft-UI-UIAutomation` — UIA Remote Operations and batching; adopt/reference.
- `microsoft/WinAppCli` — immediately useful external inspection/action reference while Eye's worker matures.
- `FlaUI/FlaUI` and `FlaUInspect` — UIA2/UIA3 conditions, waits, events and inspection patterns; selectively adopt/reference.
- `microsoft/WinAppDriver` — selector/action semantics reference.
- `pywinauto/pywinauto` — external/fallback strategy reference.
- `SuperMarioYL/uia-agent` — pruned trees, stable identities and compact action spaces; reference only until exact license is verified.
- `ibraheem-mustafa-dev/windowsagent` — combined accessibility/CDP representation; reference, not core dependency.
- `robmikh/Win32CaptureSample` — Windows.Graphics.Capture/free-threaded/snapshot/dirty-region patterns.
- `LibreHardwareMonitor/LibreHardwareMonitor` — external adapter for temperatures, clocks, fans and utilization; MPL boundary preserved.
- `FreeRDP/FreeRDP` — later reference for alternate sessions/channels if a concrete need appears.

## Browser control

Permanent primitive: installed Chrome under the active user, dedicated Eye profile/data directory, loopback CDP controlled from Eye.

### Adopt

- `ChromeDevTools/devtools-protocol` — generate permanent typed CDP bindings.
- `microsoft/playwright-dotnet` — optional accelerator for locators, waits, downloads and traces when it materially reduces work.

### Reference / external

- `ChromeDevTools/devtools-frontend` — target/session/network/tracing/page-state patterns.
- `microsoft/playwright` and `microsoft/playwright-mcp` — accessibility snapshots and structured action semantics; do not expose a competing permanent MCP server.
- `browser-use/browser-harness` — optional one-WebSocket/CDP/self-healing external harness ideas.
- `browser-use/browser-use` — browser-state recovery and DOM/action design; do not embed its agent.
- `vercel-labs/agent-browser` — compact accessibility snapshots, stable element references and iframe handling.
- `SeleniumHQ/selenium`, `puppeteer/puppeteer`, `chromedp/chromedp`, `cyrus-and/chrome-remote-interface` — protocol/lifecycle/error-handling/minimal-CDP references.

No permanent Node daemon and no separately downloaded browser fleet.

## Code intelligence

### Baseline

- `BurntSushi/ripgrep` — default raw search.
- `tree-sitter/tree-sitter` — on-demand syntax-aware parsing, symbols and chunking.
- `ast-grep/ast-grep` — external structural search and safe mechanical rewrites.

### Generic language-service direction

Use the Language Server Protocol to expose capability-shaped operations such as:

```text
code.symbols
code.references
code.rename
code.diagnostics
```

Spawn language engines only for relevant workspaces:

- OmniSharp/Roslyn for C# where appropriate.
- Pyright for Python.
- TypeScript language tooling for JS/TS.
- rust-analyzer for Rust.
- clangd/LLVM tooling for C/C++ when needed.

### Later

- `sourcegraph/zoekt` — large-corpus trigram/symbol search only after ripgrep becomes insufficient and Windows support is verified.
- SCIP — persistent cross-language symbol relationships; verify exact revision/license first.

## Files, documents and structured data

### Data

- `duckdb/duckdb` — favored embedded/on-demand SQL over CSV, JSON, Parquet, logs and artifacts; no database daemon.
- `asg017/sqlite-vec` — later embedded vector search if semantic retrieval proves useful.
- `unum-cloud/USearch` — later higher-performance vector indexing with C# support.
- FAISS/hnswlib — avoid until corpus scale actually justifies them.

### Documents

- `microsoft/markitdown` — external common-document conversion to structured Markdown.
- `docling-project/docling` — heavier layout/table/OCR extraction only when MarkItDown is insufficient.
- `UglyToad/PdfPig` — native C# PDF text/geometry extraction.
- `dotnet/Open-XML-SDK` and `ClosedXML/ClosedXML` — direct Office package/spreadsheet manipulation.
- LibreOffice and Pandoc — external-only broad conversion capabilities; do not import/link their implementation into Eye.

## Audio, vision and local GPU workloads

### Favored

- `naudio/NAudio` — microphone, system audio, process loopback, WASAPI sessions and Media Foundation.
- `ggml-org/whisper.cpp` — short-lived local transcription with CUDA/CPU/Vulkan options.
- `PaddlePaddle/PaddleOCR` — on-demand multilingual OCR/layout/table recognition.
- Tesseract — lighter fallback OCR.
- `microsoft/onnxruntime` — common runtime for CUDA/DirectML/Windows ML inference.
- OpenCV — selective preprocessing/matching/ordinary CV.

### Optional/reference

- `ggml-org/llama.cpp` — occasional local GGUF inference, embeddings or reranking, not an always-running planner.
- DirectML — fallback GPU execution reference; RTX/CUDA through ONNX Runtime is favored where suitable.
- `xlang-ai/OpenCUA` — normalized desktop action spaces, trajectories and evaluation organization.
- UI-TARS projects — screenshot normalization/action batching/visual-agent recovery reference.
- Microsoft OmniParser — isolate/reference due CC-BY attribution considerations.
- UGround — GUI grounding/evaluation reference.
- Awesome-GUI-Agents — discovery index only; validate each linked project/model independently.
- FFmpeg and OBS Studio — external-only media conversion/recording capabilities, respecting their licenses/build terms.

The laptop-class 8 GB RTX GPU should be used for short-lived Whisper, OCR, embeddings and small/quantized vision jobs. Do not reserve VRAM permanently for speculative local planners or large GUI models.

## Agent systems to mine, not embed

Useful architecture ideas may be studied from:

- Microsoft UFO
- Microsoft Magentic-UI
- OpenHands
- Cline
- Aider
- Continue
- mini-swe-agent / SWE-agent
- smolagents
- PydanticAI
- LangGraph
- AutoGen
- Roo Code
- Open Interpreter

The reason to avoid embedding them is architectural duplication: ChatGPT is already the planner/agent. Eye should not grow a second agent runtime, workflow graph, checkpoint bureaucracy, plugin economy or container-first execution substrate merely because those projects contain useful patterns.

Open Interpreter deserves special license care because current code is AGPL while older revisions used MIT; study concepts without casually mixing revisions.

## Evaluation sources

Use small selected subsets on the actual laptop, not the full virtualization stacks:

- OSWorld / OSWorld-V2 — desktop/long-horizon tasks.
- WindowsAgentArena — Windows-native app tasks.
- BrowserGym — browser action/task patterns.
- SWE-bench — real repository issue resolution.
- Terminal-Bench — terminal/systems tasks.

EyeBench should measure practical outcomes only:

- task success;
- elapsed time;
- Eye calls;
- retries/restarts;
- bytes transferred;
- required user intervention.

## Transfers, remote access and source hygiene

- `rclone/rclone` — optional external multi-storage adapter.
- `curl/curl` — raw external network transfer utility.
- `PowerShell/Win32-OpenSSH` — SSH/SFTP when required.
- `tailscale/tailscale` — optional direct networking only if the OpenAI tunnel cannot satisfy a future need.
- `CycloneDX/cyclonedx-dotnet` — build-time NuGet inventory/SBOM.
- ORT, ScanCode Toolkit, Syft — later build-time license/source discovery if imported code/dependency volume becomes substantial.

## High-value Windows facilities already present

The following native facilities are strategically valuable because Windows already implements the difficult lifecycle/consistency behavior:

| Facility | Eye capability |
| --- | --- |
| BITS | Reboot-resilient transfers with status/progress/cancel |
| VSS | Consistent reads of locked or changing files |
| Restart Manager | Identify and optionally coordinate processes holding file locks |
| Process Snapshotting | Consistent process/thread/handle/memory diagnostics |
| ReFS block cloning | Very cheap copy-on-write workspace snapshots on `X:` |
| ProjFS | Lazy materialization of very large trees |
| CopyFile2 | Native progress/cancel-aware file copies |
| Virtual Disk API | Create/attach/inspect VHD/VHDX/ISO images |
| UIA Remote Operations | Batch accessibility work in the target provider |
| UIA cache requests | Fetch only needed UIA properties/patterns |
| UIA events | React to state changes instead of polling entire desktops |

Candidate typed operations include:

```text
transfer.start/status/wait/cancel
fs.snapshot/clone/copy/lockers
disk.attach/detach/inspect
process.snapshot
ui.observe/subscribe/query/act
data.query
audio.capture/transcribe
code.symbols/references/rename/diagnostics
```

These are candidate operation families, not automatically published contract entries.

## Updated build order

1. Freeze the five-facade generated MCP contract and contract snapshot test.
2. Replace handwritten Win32 declarations with CsWin32.
3. Establish StreamJsonRpc plus multiplexed worker streams.
4. Finish Job Object / active-user / ConPTY execution semantics.
5. Build event-driven cached UIA plus dirty-region capture.
6. Generate typed CDP bindings; add optional Playwright .NET.
7. Expose high-value Windows-native facilities such as BITS, VSS, Restart Manager, ReFS clone and process snapshots as concrete needs appear.
8. Add code/document/data/audio adapters based on actual workloads.
9. Add whisper/OCR/semantic retrieval only on demand.
10. Add atomic updating and the small EyeBench suite.

## Explicit non-goals

Do not add, absent new measured evidence:

- Kubernetes;
- Docker as the base Eye runtime;
- another autonomous-agent framework;
- a generic workflow engine;
- a plugin marketplace;
- an always-running local model;
- a kernel driver;
- a permanent Node daemon;
- multiple competing MCP servers;
- heavyweight vector infrastructure without a proven workload.

The project should gain breadth by exposing native Windows capabilities and launching focused external engines on demand, not by turning `eye.exe` into a monolith.
