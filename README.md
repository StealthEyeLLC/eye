# Eye

Eye is the laptop-native privileged capability substrate for **StealthEye**: one Windows service through which ChatGPT can operate the dedicated StealthEye machine with broad, predictable authority.

## Identity

- Product: **StealthEye**
- Project: **Eye**
- Repository: **`StealthEyeLLC/eye`**
- Executable / CLI: **`eye.exe` / `eye`**
- Windows service: **`StealthEye`**
- Local repository target: **`X:\Repos\eye`**
- Core implementation: **C# / .NET on Windows**

## Core invariant

If the owner has intentionally granted an authority to Eye, Eye should not manufacture additional authority friction on top of that grant.

Real boundaries imposed by Windows, ChatGPT/OpenAI, providers, hardware, networking, power, or the execution environment still apply.

## Public MCP surface

The canonical v2 model-facing surface is five effect-class capability tools plus one UI-only tool:

| Tool | Purpose |
| --- | --- |
| `eye_inspect` | Read, search, observe, query, subscribe, wait, diagnose |
| `eye_run` | SYSTEM/user/WSL processes, PowerShell, terminal, ConPTY, raw local execution |
| `eye_change` | Precisely typed local file/machine/service/package/storage/configuration mutations |
| `eye_interact` | Windows applications, UIA, input, clipboard, Chrome/CDP interaction |
| `eye_external` | HTTP, uploads, sends, posts, provider administration, remote transfer |
| `eye_live` | Open the mission/job/trigger/artifact/relay component; no machine operation itself |

`wait` and `transfer` remain first-class typed operation families beneath the facades rather than additional top-level tools.

The six names are frozen for v2. The canonical target contract is [`contracts/eye-mcp-v2.json`](contracts/eye-mcp-v2.json); v1 is retained as immutable historical contract material.

See [`docs/MCP_CONTRACT.md`](docs/MCP_CONTRACT.md).

## Final runtime topology

There is exactly one permanent Windows SCM service:

```text
Windows SCM
  -> eye.exe stable host (LocalSystem)
       -> active versioned capability-engine child process
       -> previous engine retained for rollback
       -> on-demand active-session workers
       -> host-owned jobs / ConPTY / artifacts / triggers / state
```

The capability engine is a **separate supervised process**, not a DLL loaded into the service. A bad native UIA/CDP/media/GPU/dependency build must not be able to directly crash the stable MCP/repair host.

The stable host owns the control path and recovery substrate: MCP, contract validation, raw SYSTEM/user/WSL repair execution, Job Objects, ConPTY, durable jobs, artifacts, triggers, Mission Blackboard, stable identities/cursors, minimal Eye Live, engine supervision, and A/B rollback.

The replaceable engine owns rapidly evolving capabilities such as UIA, capture, CDP, file/code/document/data/media/provider adapters, and version-matched worker behavior.

If the engine is dead, ChatGPT must still be able to inspect Eye, run repair commands, access/cancel jobs and terminals, read artifacts, inspect mission/trigger state, and roll the engine back.

## Target request path

```text
ChatGPT
  -> OpenAI Secure MCP Tunnel
  -> tunnel-client on STEALTHEYELLC
  -> loopback MCP
  -> stable Eye host (LocalSystem)
```

The Secure MCP Tunnel is transport only. Eye remains useful on the laptop when ChatGPT/tunnel connectivity is absent; local jobs, terminals, triggers, artifacts, state, and engine supervision continue.

## Core machine primitives

Eye is built around four machine primitives:

- **Observe** — structured machine/UI/browser/process/file/job state and deltas.
- **Act** — raw execution or precise typed changes/interactions/external effects.
- **Wait** — event-driven waiting instead of repeated polling.
- **Transfer** — artifacts and large data by reference rather than giant tool results.

Durable jobs, artifacts, stable identities, incarnation generations, observation cursors, and the Trigger Broker are stable-host primitives rather than adapters.

## Implementation direction

- **Win32:** CsWin32-generated bindings and SafeHandles
- **processes:** Job Objects, explicit handle lists, active-user `CreateProcessAsUser`, native ConPTY
- **state:** small host-owned SQLite metadata store
- **worker IPC:** StreamJsonRpc over named pipes
- **bulk streams:** multiplexed binary channels
- **desktop:** HWND inventory -> event-driven cached UIA/Remote Operations -> Windows.Graphics.Capture dirty regions -> OCR fallback
- **browser:** installed Chrome + dedicated profile + generated raw CDP; optional Playwright .NET accelerator
- **code:** ripgrep -> Tree-sitter/ast-grep -> on-demand language servers
- **documents/data:** MarkItDown, PdfPig, Open XML/ClosedXML, DuckDB; heavier engines on demand
- **audio/vision:** NAudio, whisper.cpp, OCR/ONNX/OpenCV on demand
- **Windows facilities:** BITS, VSS, Restart Manager, ReFS block cloning, CopyFile2, Process Snapshotting, Virtual Disk APIs
- **updates:** host-supervised staged A/B engine activation and rollback
- **measurement:** small laptop-native EyeBench based on real tasks

Eye should remain a compact capability substrate operated by ChatGPT. It should not absorb a second autonomous-agent framework.

## Explicit non-goals

No required HEC/VPS dependency, Docker/Kubernetes base, Codex dependency, Work dependency, paid-API controller, permanent local planner, permanent Node automation daemon, generic workflow engine, plugin marketplace, competing MCP servers, or extra Eye-internal approval/receipt bureaucracy.

## Canonical sources

- [`docs/BUILD_BLUEPRINT.md`](docs/BUILD_BLUEPRINT.md) — final implementation blueprint.
- [`docs/EYE_CANON.md`](docs/EYE_CANON.md) — canonical product and architecture rules.
- [`docs/EYE_DECISIONS.md`](docs/EYE_DECISIONS.md) — decision ledger.
- [`docs/MCP_CONTRACT.md`](docs/MCP_CONTRACT.md) — public-contract semantics and freeze rules.
- [`contracts/eye-mcp-v2.json`](contracts/eye-mcp-v2.json) — canonical target v2 public-contract manifest.
- [`docs/OSS_LANDSCAPE.md`](docs/OSS_LANDSCAPE.md) — dependency/research landscape.
- [`docs/RESEARCH.md`](docs/RESEARCH.md) — vendor/API findings.
- [`docs/EYE_PLATFORM.md`](docs/EYE_PLATFORM.md) and [`docs/HARDWARE.md`](docs/HARDWARE.md) — platform/hardware references.
- [`docs/WORKLOG.md`](docs/WORKLOG.md) — durable milestone/evidence log.
- [`docs/CUTOVER.md`](docs/CUTOVER.md) — implementation/cutover checklist.
- [`AGENTS.md`](AGENTS.md) — repository guardrails.

## Credentials

Secret names and roles may be documented; secret values must never be committed.

Current bootstrap secret names:

- `EyeRuntime`
- `OpenAIAdmin`

Eye's operational Google identity is `stealtheye.eye@gmail.com`.

## Current implementation stage

The checked-in runtime is still an early command-execution prototype relative to the canonical blueprint. The new architecture is now frozen; implementation work should build beneath it rather than expand it.
