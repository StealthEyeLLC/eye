# STEALTHEYE MCP Public Contract Reference

> Generated from contracts/eye-mcp-v2.json. Do not edit by hand.

- Contract: stealtheye.eye.mcp
- Version: 2.1.0
- Status: canonical-target
- Publication state: not-live-until-generated
- Canonical JSON SHA-256: 77cfb617107a41871401b507f94982c5ae2c4b2c617f42a8c9fb7fdb7c5dee6c
- Host/engine protocol: 1.0.0
- Worker protocol: 1.0.0

## Server instructions

STEALTHEYE is a privileged Windows capability substrate controlled by ChatGPT. Prefer precise typed operations, then direct CLI/file operations, then browser CDP, then Windows UI Automation, and use raw eye_run only when no narrower operation fits. Long work should become durable jobs; wait on events instead of polling; move large data by artifact reference. Reuse stable IDs, incarnations, and cursors. For consequential effects, use stable task_id/action_id plus a deterministic postcondition so retries are idempotent and evidence-backed. ChatGPT remains the planner; STEALTHEYE does not autonomously plan or declare unverified completion.

## Public tools

| Tool | Effect class | Machine effects | Operations |
| --- | --- | --- | --- |
| eye_inspect | inspect | read-only-or-observational | system.status, action.status, capabilities, engine.status, job.status, job.read, job.wait, job.result, job.attach, artifact.info, artifact.preview, artifact.read_range, artifact.diff, ui.observe, ui.query, browser.observe |
| eye_run | run | local-execution | run, job.start, job.write, job.resize, job.cancel |
| eye_change | change | local-write | engine.activate, engine.restart, engine.rollback, artifact.export, artifact.delete |
| eye_interact | interact | interactive | ui.act, browser.navigate, browser.evaluate |
| eye_external | external | open-world | (none) |
| eye_live | ui | none | (none) |

## Engine-owned public operations

- browser.evaluate
- browser.navigate
- browser.observe
- ui.act
- ui.observe
- ui.query

## Runtime generation invariants

- Model-facing tool schemas are generated from the canonical contract.
- tools/list is frozen by a normalized protocol snapshot.
- capabilities projects operation membership from the canonical tool descriptors.
- Server initialization instructions come from the canonical contract.
- Host/engine compatibility is bound to the canonical contract hash.
