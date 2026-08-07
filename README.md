# Eye

Eye is the laptop-native execution substrate for **StealthEye**: one Windows service through which ChatGPT can operate the dedicated StealthEye machine with broad, predictable authority.

## Identity

- Product: **StealthEye**
- Repository: **`StealthEyeLLC/eye`**
- Executable: **`eye.exe`**
- Planned local repository: **`X:\Repos\eye`**
- Core implementation: **C# / .NET on Windows**

## Core invariant

If the owner has intentionally granted an authority to Eye, Eye should not manufacture additional authority friction on top of that grant.

Real boundaries imposed by Windows, OpenAI, providers, hardware, networking, or the execution environment still apply.

## Public MCP surface

Eye uses **five stable model-facing MCP facades** over one internal operation registry and dispatcher:

| Tool | Purpose |
| --- | --- |
| `eye_inspect` | Local observation/read/query |
| `eye_run` | Windows/WSL/process/PowerShell/ConPTY execution |
| `eye_change` | Precisely typed local mutations |
| `eye_interact` | Desktop/application/browser interaction |
| `eye_external` | Effects that intentionally leave the machine |

The facade split exists for schema accuracy, tool selection, and truthful effect metadata. It is **not a privilege hierarchy**. `eye_run` remains the raw local execution escape hatch when no narrower typed operation exists.

The public contract is versioned and frozen in `contracts/eye-mcp-v1.json`. Generated descriptors, request/result types, dispatcher registration, capability metadata, documentation, and a normalized `tools/list` snapshot are intended to derive from that contract.

See [`docs/MCP_CONTRACT.md`](docs/MCP_CONTRACT.md).

## Target request path

```text
ChatGPT
  -> OpenAI Secure MCP Tunnel
  -> tunnel-client on STEALTHEYELLC
  -> loopback MCP
  -> eye.exe Windows service (LocalSystem)
```

The final runtime is intentionally small:

- one permanent LocalSystem Windows service;
- five precise public MCP facades over one internal dispatcher;
- native Windows process/session APIs for active-user execution;
- native Job Objects and ConPTY;
- StreamJsonRpc named-pipe control for on-demand workers;
- Nerdbank.Streams-style multiplexed channels for bulk worker data;
- WSL launched through the active-user token;
- installed Chrome controlled through a dedicated StealthEye profile and loopback CDP;
- short-lived on-demand interactive-session workers for desktop/UI operations;
- Secure MCP Tunnel kept outside Eye as transport only.

No permanent user-session daemon is intended in the final design.

## Implementation direction

The current v2 direction is deliberately Windows-native and capability-oriented:

- **Win32:** CsWin32-generated bindings/SafeHandles
- **processes:** Job Objects, explicit inherited handles, native ConPTY, active-user `CreateProcessAsUser`
- **worker IPC:** StreamJsonRpc over named pipes
- **bulk streams:** multiplexed binary channels
- **desktop:** HWND inventory + event-driven cached UIA/Remote Operations + Windows.Graphics.Capture dirty regions
- **browser:** generated raw CDP bindings, optional Playwright .NET accelerator
- **code:** ripgrep + Tree-sitter/ast-grep + on-demand language servers
- **documents/data:** MarkItDown, Open XML/ClosedXML, PdfPig, DuckDB as workloads justify them
- **audio/vision:** NAudio, whisper.cpp, OCR/ONNX/OpenCV on demand
- **Windows facilities:** BITS, VSS, Restart Manager, ReFS block cloning, process snapshots and virtual-disk APIs where useful
- **updates:** staged/atomic update path
- **measurement:** a small laptop-native EyeBench built from real tasks

Eye should remain a compact privileged capability substrate operated by ChatGPT. It should not absorb an autonomous-agent framework merely because one is available.

## Explicit non-goals

Do not turn Eye into:

- a generic workflow engine;
- an agent framework;
- a plugin marketplace;
- a policy/approval framework;
- a generic multi-machine orchestrator;
- a Docker/Kubernetes-based runtime;
- an always-running local-model stack;
- a collection of competing permanent MCP servers.

## Source documents

- [`docs/EYE_CANON.md`](docs/EYE_CANON.md) — canonical architecture source.
- [`docs/EYE_DECISIONS.md`](docs/EYE_DECISIONS.md) — canonical/provisional/open decision ledger.
- [`docs/MCP_CONTRACT.md`](docs/MCP_CONTRACT.md) — five-facade public contract design and freeze rules.
- [`contracts/eye-mcp-v1.json`](contracts/eye-mcp-v1.json) — versioned public-contract source.
- [`docs/OSS_LANDSCAPE.md`](docs/OSS_LANDSCAPE.md) — 2026 open-source/dependency landscape and adopt/reference/isolate guidance.
- [`docs/RESEARCH.md`](docs/RESEARCH.md) — vendor/API findings that materially affect implementation.
- [`docs/EYE_PLATFORM.md`](docs/EYE_PLATFORM.md) — target machine/platform roles and live-validation boundary.
- [`docs/HARDWARE.md`](docs/HARDWARE.md) — machine hardware/runtime snapshot.
- [`docs/WORKLOG.md`](docs/WORKLOG.md) — durable milestone/evidence log.
- [`docs/CUTOVER.md`](docs/CUTOVER.md) — v2 implementation and runtime cutover checklist.
- [`AGENTS.md`](AGENTS.md) — repository guardrails, including the public-contract freeze.

## Credentials

Secret **names and roles** may be documented; secret **values must never be committed**.

Current bootstrap secret names:

- `EyeRuntime`
- `OpenAIAdmin`

Eye's operational Google identity is `stealtheye.eye@gmail.com`.

## Current implementation stage

The repository contains the first v2 service/process implementation and architecture documentation. The immediate engineering priority is to replace the transitional hand-written MCP metadata/interop with the frozen generated contract and CsWin32 foundation, then finish native execution, worker IPC, desktop observation/control, and browser CDP in that order.
