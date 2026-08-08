# MCP_CONTRACT.md

**Status:** Canonical public-interface design  
**Baseline date:** 2026-08-07  
**Canonical target contract:** v2

## Purpose

Eye exposes five effect-class capability tools plus one UI-only tool over one stable host and one replaceable capability engine.

```text
ChatGPT
  -> eye_inspect
  -> eye_run
  -> eye_change
  -> eye_interact
  -> eye_external
  -> eye_live
       |
       v
  generated contract layer in stable host
       |
       +--> host-owned primitives
       |
       +--> supervised versioned capability engine
```

The effect-class split exists for schema accuracy, tool selection, result consistency, and truthful metadata. It is not a privilege hierarchy. `eye_run` remains the broad local execution escape hatch.

`eye_live` is different: it exists to mount the optional Eye Live MCP Apps component and performs no machine operation itself.

## Public tools

| Tool | Purpose | Effect class |
| --- | --- | --- |
| `eye_inspect` | Read/search/observe/query/subscribe/wait/diagnose | Inspect / observational |
| `eye_run` | SYSTEM/user/WSL/process/PowerShell/terminal/ConPTY execution | Local execution |
| `eye_change` | Precisely typed local file/machine/service/package/storage/configuration mutations | Local write |
| `eye_interact` | Windows applications/UIA/input/clipboard/Chrome-CDP interaction | Interactive |
| `eye_external` | HTTP/uploads/sends/posts/provider administration/remote transfers | Open-world/external |
| `eye_live` | Open Eye Live mission/job/trigger/artifact/relay UI | UI only; no machine effect |

`wait` and `transfer` are operation families distributed beneath the appropriate facades. They are not top-level tools.

## Contract versions

`contracts/eye-mcp-v1.json` records the retired five-capability-tool design and is immutable historical material.

The canonical target source is:

```text
contracts/eye-mcp-v2.json
```

The six v2 tool names are frozen.

The checked-in implementation may lag the target contract while the stable host/generator/UI are built. Do not advertise v2 as live until the activation gates in `eye-mcp-v2.json` are satisfied.

## Generated contract rule

One canonical contract source generates or validates:

```text
contract
  -> MCP descriptors
  -> C# request/result DTOs
  -> stable-host validation
  -> operation/facade registration
  -> capability metadata
  -> server initialization instructions
  -> public documentation
  -> normalized tools/list snapshot
```

Generated artifacts never become an independent source of truth.

## Schema profile

Use exact per-operation variants and intentionally boring JSON Schema:

- shallow objects;
- primitives;
- arrays;
- enums/consts;
- bounds;
- required fields;
- `additionalProperties: false` by default.

Avoid deep composition, recursive schema structures, conditionals, clever nullable unions, and unnecessary schema indirection.

Omit optional values instead of sending `null` where practical.

Each structured operation result declares an exact `outputSchema`.

Routine domain errors return typed structured errors. Do not leak arbitrary exception types, stack traces, or implementation internals into model-visible results.

## Result semantics

The logical result envelope contains a stable operation/status identity and the typed result, job, artifact, stream, or error information needed for continuation.

Conceptually:

```json
{
  "ok": true,
  "operation": "...",
  "result": {}
}
```

or:

```json
{
  "ok": false,
  "operation": "...",
  "error": {
    "code": "invalid_argument",
    "message": "...",
    "retryable": false,
    "expected": {}
  }
}
```

Operation-specific generated schemas determine which fields are legal. The public wire contract should not rely on `Task<object>` or arbitrary result blobs.

Large output becomes an artifact plus a useful inline excerpt rather than an enormous result payload.

## Stable identifiers

Published stateful resources use stable typed IDs and cursors, including:

```text
job_id
artifact_id
stream_id
stable object ID
incarnation generation
observation cursor
```

The identity rule is:

```text
stable object ID + incarnation generation + observation cursor
```

This lets Eye distinguish ordinary state changes from destruction/replacement or OS identifier reuse.

## Host-owned operation families

The stable host must publish enough operation capability to keep Eye repairable without a healthy feature engine.

Required categories include:

- system/capability status;
- engine status/restart/activate/rollback;
- raw SYSTEM/user/WSL execution;
- durable job/terminal control;
- artifact reads;
- mission/trigger state;
- minimal Eye Live monitoring.

Exact operation names and schemas are added to the v2 contract only through explicit contract revisions.

## `eye_live` UI rules

Eye Live is optional UI over ordinary Eye operations.

For new UI implementations:

- link the tool to its UI resource with `_meta.ui.resourceUri`;
- use the MCP Apps `ui/*` JSON-RPC bridge;
- use `ui/message` for a UI-initiated follow-up message;
- keep helper tools app-only with `_meta.ui.visibility: ["app"]` when they are not intended for model selection;
- keep ordinary MCP parity so Eye remains usable when the UI is absent.

Do not attach the component to every Eye capability operation. `eye_live` exists specifically so the UI mounts only when continuation/supervision is useful.

## File bridge

Where a public operation accepts a ChatGPT-provided file, use supported top-level file fields and `_meta["openai/fileParams"]` so ChatGPT can pass a file object directly rather than forcing the user to invent a laptop path.

Imported files become Eye artifacts or are materialized to an explicitly requested destination.

Large outbound data should use artifact/export/file-reference mechanisms rather than inline base64 or giant JSON payloads.

## Server instructions

The stable host supplies compact MCP initialization instructions teaching the shared routing rules, including:

- which facade owns each effect;
- `eye_run` as the universal local fallback;
- automatic durable-job behavior;
- native waits instead of manual polling;
- artifact continuation;
- stable-handle reuse;
- Eye Live continuation;
- contract discipline.

Keep the first 512 characters self-contained.

The detailed operator modality hierarchy belongs in the Eye Operator skill, not in giant tool descriptions.

## Metadata style

Descriptions should be precise, neutral, and action-oriented. Describe the operation and where its effect occurs. Avoid theatrical descriptions of authority that add no capability and make classification less accurate.

Use truthful effect annotations. Eye itself does not add approval layers merely because a tool is broad, but host-platform confirmations/policies remain outside Eye's control.

## Compatibility and revision policy

- v1 remains immutable historical material.
- The six v2 top-level names are frozen.
- Ordinary feature work must not silently alter public metadata/schema.
- An additive public operation/schema change requires an explicit owner-authorized v2 contract revision.
- A breaking change to the six-tool surface requires a new major contract version.
- Internal implementation/engine changes that preserve the public contract do not require a public revision.
- The server may accept documented legacy aliases internally when cheap and unambiguous.
- A normalized `tools/list` snapshot must fail when unintended public drift occurs.

## Growth rule

Do not publish speculative capability variants merely to make the schema look complete.

New capabilities should fit beneath the existing six-tool surface. Publish exact operation variants when the implementation or near-term workload justifies them.

Architectural expansion beyond the six-tool design requires explicit owner authorization.
