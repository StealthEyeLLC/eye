---
name: eye-operator
description: Operate the owner's StealthEye/Eye Windows workstation efficiently through the Eye MCP app. Use whenever ChatGPT needs to inspect, execute, change, automate, repair, supervise, or continue work on STEALTHEYELLC through Eye; manage durable jobs or terminals; use artifacts, waits/triggers, Eye Live, desktop/browser control, or engine recovery; or decide which Eye facade/modality should handle a laptop task. Follow the live MCP schemas as authoritative and use this skill for routing, continuation, recovery, storage, identity, and machine conventions.
---

# Eye Operator

Operate Eye as a high-authority machine substrate, not as an autonomous planner. Let the user's request drive the work; use Eye to execute it with the least indirect reliable modality.

## Authority and source order

- Treat the live Eye MCP schemas and server instructions as authoritative for exact operation names, arguments, and result shapes.
- Never redefine or guess a public operation because this skill mentions a capability family.
- Use `operation.list` / `operation.describe` only when an unfamiliar capability requires discovery. Do not rediscover familiar operations every turn.
- Do not add an Eye-internal approval framework. If the owner has already granted the underlying authority and the request is allowed, execute directly.
- Preserve real external boundaries: ChatGPT/OpenAI policy, Windows secure desktop/preboot, provider permissions, CAPTCHA/MFA/rate limits, hardware, power, and network availability.

## Route through the six facades

Use exactly the exposed Eye facade that matches the intended effect:

- `eye_inspect`: read, search, observe, query, wait, subscribe, and diagnose.
- `eye_run`: SYSTEM/user/WSL processes, PowerShell, terminals, ConPTY, and raw local execution.
- `eye_change`: typed local file, machine, service, package, storage, and configuration changes.
- `eye_interact`: Windows applications, UIA, input, clipboard, and Chrome/CDP interaction.
- `eye_external`: HTTP, uploads, sends, posts, provider administration, remote transfer, and other effects leaving the laptop.
- `eye_live`: optional UI-only mission/job/trigger/artifact/relay supervision. It performs no machine effect itself.

Treat this split as effect metadata, not a privilege hierarchy. Keep `eye_run` as the broad local fallback when no narrower typed operation fits.

## Choose the least indirect reliable modality

Prefer, in order:

1. Precise native typed Eye operation.
2. CLI, API, or direct file manipulation.
3. Chrome DevTools Protocol.
4. Windows UI Automation.
5. Pixel/OCR/pointer/keyboard fallback.
6. Raw local execution through `eye_run` when no precise operation exists.

Use structured state before pixels. Do not click through a UI when a direct API, file, CLI, CDP, or UIA path is more reliable.

## Execute short work; continue long work

- Use the synchronous execution path for genuinely short work.
- Expect Eye to promote work that outlives the fast window into a durable host-owned job.
- Reuse the returned `job_id`; do not blindly rerun a command because ChatGPT streaming or the tunnel disconnected.
- Prefer the available job operations for status, cursor reads, waits, terminal input/resize, cancellation, attach, and final result.
- Reattach to an existing terminal/job after reconnect whenever possible.
- Cancel the owned Job Object tree rather than leaving descendants behind.
- Use native ConPTY/session workers for persistent interactive terminals.
- Keep long-running ownership in the stable host, not the replaceable engine.

When a tool call fails at the transport layer after starting work, first check the known job/object state before retrying the underlying action.

## Wait for state transitions instead of polling

Use native waits/triggers when Eye exposes a signalable condition. Prefer event-driven waits for process/job exit, file creation/change, terminal/output state, service/port state, session state, window/UIA changes, Chrome navigation/network/download/DOM events, device/power/network/idle state, and time conditions.

Avoid repeated screenshot/status loops when an event or cursor can represent the same transition. Do not invent a wait operation that is absent from the live contract.

## Move large data by artifact reference

- Prefer artifact IDs for large output, screenshots, recordings, audio, dumps, traces, archives, downloads, generated documents, and query results.
- Return or consume a useful preview/excerpt plus the artifact ID rather than flooding the conversation.
- Use bounded preview/range/export operations when only part of an artifact is needed.
- Reuse an artifact ID instead of rediscovering the backing file.

Storage roles:

- `C:`: Windows/apps, stable host state, tiny authoritative metadata, protected secrets.
- `X:`: ReFS Dev Drive, repositories, hot workspaces, job spool, temporary/hot artifacts.
- `E:`: large/cold/durable artifacts, models, media, archives. Never treat it as disposable during destructive provisioning.
- WSL filesystem: Linux-native permission/metadata-sensitive work.

## Preserve stable identity and cursor semantics

Think in `stable object ID + incarnation + observation cursor`.

- Reuse stable IDs for logical objects.
- Treat an incarnation change as replacement/recreation of the underlying object.
- Advance from returned cursors to consume deltas instead of rereading complete streams/state.
- For terminals, jobs, triggers, artifacts, windows, browser targets, and future mission objects, prefer handle reuse over rediscovery.

## Query dynamic machine truth live

Do not rely on stale project prose for facts Eye can query now. Live-query Windows build/uptime, active session/lock state, CPU/RAM/GPU/free VRAM, volumes/free space, WSL state, current jobs/terminals, installed software/adapters, Chrome/CDP state, service/engine/tunnel health, and other changing capabilities.

Static machine conventions:

- Machine: `STEALTHEYELLC`
- Interactive Windows identity/profile: `StealthEye` / `C:\Users\StealthEye`
- Repository: `StealthEyeLLC/eye`
- Canonical local repo: `X:\Repos\eye`
- Runtime/CLI: `eye.exe` / `eye`
- Windows SCM service: `StealthEye`, LocalSystem

For local inference or sustained workloads, inspect current resources first when they materially affect success. Do not permanently reserve GPU/CPU resources for a planner.

## Use desktop and browser structure before visual fallback

Desktop preference:

`HWND/process/window inventory -> cached/event-driven UIA -> Windows capture dirty regions -> OCR/visual grounding/raw input`

Browser preference:

`typed Chrome CDP -> DOM/accessibility/network state -> optional Playwright .NET accelerator if useful -> visual fallback`

Send the smallest state needed for the next decision rather than dumping the whole desktop/browser state.

## Recover through the stable host

Understand the fault boundary:

`one LocalSystem service -> stable host -> supervised versioned engine + on-demand workers`

If the engine is broken or unavailable, continue using host-owned capabilities that remain exposed: system/capability status, engine restart/activate/rollback, raw SYSTEM/user/WSL repair execution, jobs/terminals, artifact reads, trigger/mission state when implemented, and minimal Eye Live monitoring.

A tunnel/chat disconnect does not own local Eye state. Recover the local object first; do not equate transport failure with process/job failure.

## Use Eye Live as optional acceleration

Open `eye_live` when a compact live component materially helps supervise jobs, terminals, triggers, artifacts, mission state, relay state, or recovery. Keep all meaningful work reachable through ordinary MCP operations if the component is unavailable.

Treat Eye Live controls as ordinary MCP calls presented visually. Never assume the UI provides hidden machine authority.

## Blackboard, relay, and context capture

When these capabilities are present in the live contract:

- Keep Mission Blackboard compact: objective, current facts/decisions, active jobs/triggers, artifacts, unresolved questions, next action, and compact relay messages.
- Do not turn Blackboard into a transcript archive, DAG/task taxonomy, receipt system, or scheduler.
- Use Relay for compact multi-tab handoffs; do not assume a closed tab receives a spontaneous turn.
- Use one-shot context capture to gather the active window/app, focused/selected text, clipboard, relevant UIA state, screenshot/region, Chrome target/DOM context, and relevant path when it reduces user explanation.

## Respect project constraints

- Keep the six v2 tool names frozen unless the owner explicitly revises the public contract.
- Fit new capabilities beneath the existing architecture instead of inventing new architectural layers.
- Do not change Windows login/account/autologon unless explicitly requested.
- Never expose or commit passwords, tokens, private keys, recovery material, or decrypted secret blobs.
- Do not introduce Docker/Kubernetes as Eye's base, a permanent Node/browser automation daemon, a competing MCP server, a generic workflow/DAG engine, a second autonomous agent runtime, or a permanent local planner unless canon is explicitly revised.
- Prefer Windows/.NET native facilities before adding broad permanent dependencies.

## Practical recovery patterns

- **Streaming/tunnel error after launching work:** check `job.status`; read from the last cursor; reattach if interactive; rerun only when the original action is known not to have started.
- **Engine unavailable:** inspect engine status; use host repair operations; restart/rollback/activate through the ordinary typed operations; keep existing host jobs/artifacts/triggers intact.
- **Large command output:** consume bounded cursor reads or an artifact instead of asking for the entire stream again.
- **Unknown capability:** inspect the live operation catalog once, then use the discovered typed operation directly on subsequent calls.
- **GUI task:** try API/CLI/CDP/UIA before screenshots and raw pointer input.
- **Long interactive work:** start/attach a durable terminal and preserve its job ID/cursors across turns.