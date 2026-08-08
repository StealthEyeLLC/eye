# EYE_OPERATOR_SKILL.md

**Status:** Canonical source for the Eye Operator ChatGPT skill  
**Baseline date:** 2026-08-07  
**Product:** StealthEye  
**Project:** Eye  
**Repository:** `StealthEyeLLC/eye`

This document defines the operating doctrine that the **Eye Operator** ChatGPT skill should teach. It is a project source document, not the skill package itself and not a build plan.

The skill lives on the **ChatGPT side**. It does not run inside `eye.exe`, does not add machine authority, and is not required for the standalone Eye runtime to function. Its purpose is to make a fresh regular ChatGPT tab operate Eye correctly and efficiently without relearning the architecture from scratch.

## 1. Identity

| Item | Canonical value |
| --- | --- |
| Skill name | **Eye Operator** |
| Product | **StealthEye** |
| Project | **Eye** |
| Repository | **`StealthEyeLLC/eye`** |
| Runtime executable / CLI | **`eye.exe` / `eye`** |
| Windows service | **`StealthEye`** running as LocalSystem |
| Machine | **`STEALTHEYELLC`** |
| Windows interactive identity | **`StealthEye`** / `C:\Users\StealthEye` |
| Local repository target | **`X:\Repos\eye`** |
| Canonical public contract | **`contracts/eye-mcp-v2.json`** |
| Skill source of truth | **`docs/EYE_OPERATOR_SKILL.md`** |

## 2. Role in the system

Canonical relationship:

```text
Regular ChatGPT tab
  + Eye Operator skill
        |
        v
Eye developer-mode MCP app
        |
        v
OpenAI Secure MCP Tunnel
        |
        v
Stable Eye host (LocalSystem)
        |
        v
Versioned capability engine + on-demand workers
```

The skill teaches **how to operate** Eye. The MCP server supplies the actual tools and machine capabilities. The stable host remains useful without ChatGPT or the skill being connected.

The skill is preferred for normal ChatGPT operation but must never become a runtime dependency or a substitute for precise MCP schemas.

## 3. Six public Eye tools

The canonical model-facing surface is exactly:

| Tool | Operator meaning |
| --- | --- |
| `eye_inspect` | Read, search, observe, query, subscribe, wait, and diagnose. |
| `eye_run` | SYSTEM/user/WSL processes, PowerShell, terminals, ConPTY, and raw local execution. |
| `eye_change` | Precisely typed local file, machine, service, package, storage, and configuration changes. |
| `eye_interact` | Windows applications, UIA, input, clipboard, and Chrome/CDP interaction. |
| `eye_external` | HTTP, uploads, sends, posts, provider administration, remote transfer, and other open-world effects. |
| `eye_live` | Open the optional mission/job/trigger/artifact/relay component. It performs no machine operation itself. |

`wait` and `transfer` are first-class operation families beneath these facades, not additional top-level tools.

The facade split exists for accurate schemas and effect metadata. It is not a privilege hierarchy. `eye_run` remains the universal local escape hatch when no narrower typed operation fits.

## 4. Preferred modality hierarchy

The skill should teach ChatGPT to choose the least indirect reliable modality that solves the task:

1. **Precise native typed Eye operation.**
2. **CLI, API, or direct file manipulation.**
3. **Chrome DevTools Protocol.**
4. **Windows UI Automation.**
5. **Pixel / OCR / pointer / keyboard fallback.**
6. **Raw unrestricted execution through `eye_run` when no precise operation exists.**

This is an efficiency/reliability hierarchy, not an authority restriction.

## 5. Durable-work doctrine

The operator should assume that work may outlive one MCP request or one ChatGPT streaming turn.

Rules:

- Use the convenient synchronous path for genuinely short work.
- Let long work become a durable host-owned job automatically.
- Prefer `job.status`, cursor-based `job.read`, `job.wait`, `job.write`, `job.resize`, `job.cancel`, `job.attach`, and `job.result` rather than rerunning work.
- Do not treat a ChatGPT streaming error or tunnel disconnect as evidence that the underlying job stopped.
- Reattach to the existing job/terminal after reconnect whenever possible.
- Use Job Object semantics for owned process trees; cancel the owned tree rather than leaving descendants behind.
- Use native ConPTY for persistent interactive terminals.

Long-running work belongs to Eye's stable host, not the replaceable capability engine.

## 6. Wait instead of polling

The operator should use native waits/triggers whenever the desired condition can be signaled.

Prefer waiting for events such as:

- job/process exit;
- output or terminal text;
- file change/create/unlock;
- service or port state;
- session lock/unlock;
- window/UIA state;
- Chrome navigation/network/download/DOM events;
- device/power/network/idle state;
- time conditions;
- any-of condition sets.

Avoid repeated screenshot/status loops when Eye can wait for the actual state transition.

## 7. Artifact doctrine

Large data should move by reference.

Use artifact handles for:

- large terminal output;
- screenshots and recordings;
- audio;
- dumps and traces;
- archives;
- browser downloads;
- generated documents;
- query results;
- files imported from ChatGPT.

Prefer a useful preview or excerpt plus an artifact ID over dumping huge payloads into the conversation. Use range/preview/export operations when only part of an artifact is needed.

Storage convention:

```text
X: hot/high-churn job spool and temporary artifacts
E: large/cold/durable bulk artifacts, models, media and archives
C: tiny authoritative host metadata and protected secrets
```

## 8. Stable identities, incarnations, and cursors

Canonical object model:

```text
stable object ID + incarnation generation + observation cursor
```

The operator should reuse returned handles instead of rediscovering the same object repeatedly.

Interpretation:

- stable ID identifies the logical object;
- incarnation changes when the underlying object is destroyed/replaced or an OS identifier is reused;
- cursor advances as new observable state is produced.

Use deltas from a known cursor rather than requesting full state repeatedly.

## 9. Observation doctrine

Prefer structured state before pixels.

Desktop hierarchy:

```text
HWND / process / window inventory
  -> cached and event-driven UI Automation / Remote Operations
  -> Windows.Graphics.Capture dirty regions
  -> OCR / visual grounding / raw input fallback
```

Browser hierarchy:

```text
typed Chrome DevTools Protocol
  -> browser accessibility / DOM / network state
  -> optional Playwright .NET accelerator where it materially helps
  -> visual fallback only when necessary
```

The goal is to send ChatGPT the smallest state needed for the next decision rather than the entire machine state.

## 10. Machine-truth doctrine

Do not rely on stale project documents for dynamic facts when Eye can query the machine.

Use live machine/capability manifests for facts such as:

- current Windows version and uptime;
- active session / lock state;
- CPU, RAM, GPU and free VRAM;
- current volume sizes/free space;
- WSL availability/state;
- active jobs/terminals;
- installed software/adapters and versions;
- Chrome/CDP state;
- service, engine and tunnel health.

Use `operation.list` and `operation.describe` for unfamiliar capabilities. Do not repeatedly request capability descriptions for familiar operations.

## 11. Eye Live doctrine

Eye Live is optional acceleration for continuation and supervision.

Use it when a compact live component materially improves work involving:

- running jobs/terminals;
- live output tails;
- triggers;
- artifacts;
- mission state;
- relay inbox/outbox;
- same-chat continuation.

Do not make core work depend on the widget being mounted. Everything meaningful in Eye Live must remain reachable through ordinary MCP operations.

## 12. Mission Blackboard and Relay

The Mission Blackboard is a compact working-state store, not a workflow engine.

Canonical fields are limited to useful mission state such as:

- objective;
- current facts/decisions;
- active jobs/triggers;
- artifacts;
- unresolved questions;
- next action;
- compact relay messages.

Do not turn it into a transcript archive, DAG database, task taxonomy, receipt system, or generic scheduler.

When multiple ordinary Chat tabs participate, use compact relay messages and optional roles such as operator, researcher, critic, or comparator. A closed tab cannot be assumed to receive a spontaneous ChatGPT turn; preserve its relay queue until it returns.

## 13. Context capture

Treat the one-shot context helper as a high-value operator feature.

A context packet may include:

- active application/window;
- focused or selected text;
- clipboard;
- UIA context;
- screenshot/region;
- current Chrome target/DOM context;
- relevant filesystem path.

Use context capture to reduce manual explanation when the owner hands ChatGPT a live machine situation.

## 14. Laptop conventions

Canonical roles:

```text
Machine: STEALTHEYELLC
Windows user/profile: StealthEye / C:\Users\StealthEye
Repository: StealthEyeLLC/eye
Local repo: X:\Repos\eye
C: Windows/apps/stable host state/protected secrets
X: physical ReFS Dev Drive/repos/hot workspace/job spool
E: bulk data/models/media/archives/cold artifacts
WSL filesystem: Linux-native permission-sensitive work
```

Treat current capacity, driver versions, Windows build, firmware, monitor topology, and installed package versions as dynamic state to query live.

## 15. Resource-aware operation

For sustained work, prefer choices that respect the machine's current resources:

- check free GPU memory before starting local inference;
- avoid permanently reserving VRAM for a local planner;
- use X: for hot work and ReFS clones;
- use E: for large/cold artifacts/models/media;
- use the WSL filesystem for Unix metadata-heavy projects;
- use Windows power requests for long jobs where appropriate;
- use job priority/resource controls when they provide a concrete benefit.

## 16. Authority posture

Eye is intentionally high-authority. The skill should not invent an additional approval framework inside Eye.

If the owner has already granted the underlying authority, prefer executing the requested operation directly through the appropriate Eye capability.

Real boundaries still apply, including:

- Windows secure desktop and pre-boot state;
- ChatGPT/OpenAI behavior and policy;
- external provider permissions;
- CAPTCHA/MFA/rate limits/account restrictions;
- hardware, electricity, network, and device availability;
- model fallibility and finite context.

## 17. Standalone and failure behavior

Eye remains standalone on the Windows laptop.

The operator should understand the fault boundary:

```text
one LocalSystem SCM service
  -> small stable host
       -> separate supervised versioned capability engine
       -> on-demand session workers
```

If the capability engine is broken, the stable host should still provide status/capabilities, engine restart/activate/rollback, raw SYSTEM/user/WSL repair execution, job/terminal control, artifact reads, mission/trigger state, and minimal Eye Live monitoring.

The OpenAI Secure MCP Tunnel is transport only. A chat/tunnel disconnect does not own or define local job, terminal, artifact, trigger, mission, or engine state.

## 18. Operating constraints

The Eye Operator skill should reinforce these project constraints:

- Keep the six public v2 tool names frozen unless the owner explicitly authorizes a contract revision.
- Do not create new architectural layers for ordinary new capabilities; fit them beneath the existing blueprint.
- Do not change Windows login/account/autologon architecture unless explicitly requested.
- Do not treat E: as disposable during provisioning or destructive storage work.
- Never expose or commit secret values, private keys, passwords, recovery material, or decrypted secret blobs.
- Do not introduce Docker/Kubernetes as Eye's base, a permanent Node automation daemon, a second autonomous agent runtime, a generic workflow engine, a permanent local planner, or competing MCP servers.
- Prefer Windows/.NET native facilities before adding broad permanent dependencies.

## 19. Skill/server contract boundary

The MCP schema and server instructions carry correctness-critical routing and validation. The Eye Operator skill carries deeper operating procedure and modality guidance.

Server instructions should stay compact and self-contained. The skill may provide richer operational doctrine, examples, and recovery guidance without bloating every tool description.

The skill must not redefine public schemas independently. If the contract changes, update the canonical contract first, then update this source and the generated/packaged skill to match.

## 20. Handoff rule

For a fresh ChatGPT context using Eye, this document is the canonical source for **how ChatGPT should operate Eye**.

For machine/project identity and hardware constraints, use the project reference. For implementation architecture, use `docs/BUILD_BLUEPRINT.md`. For exact public schemas, use `contracts/eye-mcp-v2.json` and `docs/MCP_CONTRACT.md`.
