# MCP_CONTRACT.md

**Status:** Canonical public-interface design  
**Baseline date:** 2026-08-07  
**Contract version:** v1

## Purpose

Eye keeps one internal capability engine while exposing five model-facing MCP facades grouped by effect class. The split exists for schema accuracy, tool selection, result consistency, and truthful effect metadata. It is not a privilege hierarchy.

```text
ChatGPT
  -> eye_inspect
  -> eye_run
  -> eye_change
  -> eye_interact
  -> eye_external
       |
       v
  generated contract layer
       |
       v
  one operation registry / dispatcher
       |
       v
  Eye LocalSystem service and on-demand workers
```

All five facades ultimately route to the same owner-authorized Eye capability substrate. `eye_run` remains the unrestricted local execution escape hatch when a more precise typed operation does not exist.

## Public tools

| Tool | Purpose | Effect class |
| --- | --- | --- |
| `eye_inspect` | Local observation: status, files, processes, windows, UIA state, screenshots, diagnostics | Read-only/local |
| `eye_run` | Windows/WSL/process/PowerShell/ConPTY execution | Raw execution escape hatch |
| `eye_change` | Precisely typed local machine, file, service, storage, and configuration mutations | Local write |
| `eye_interact` | Desktop input, application interaction, browser navigation and UI actions | Interactive |
| `eye_external` | Uploading, posting, sending, remote-provider administration, or other effects that leave the machine | Open-world/external |

The tools classify effects for the model-facing contract. They do not intentionally reduce the authority granted to Eye.

## Internal compatibility

Internally Eye retains a stable operation registry and dispatcher. The CLI and implementation may continue to represent an invocation as:

```text
(op, args)
```

The public MCP facades are generated views over that registry rather than five independent implementations.

## Canonical source

The versioned public contract lives at:

```text
contracts/eye-mcp-v1.json
```

That contract is the source for generated artifacts:

```text
contract
  -> MCP tool descriptors
  -> C# request/result types
  -> operation-to-facade registration
  -> capability metadata
  -> public contract documentation
  -> normalized tools/list snapshot
```

Ordinary implementation work must not silently change the public contract.

## Schema rules

Published operations should use exact input schemas:

- operation names are closed enums/const values rather than unrestricted text;
- argument objects use explicit property types;
- required properties are declared;
- defaults and practical limits are declared;
- `additionalProperties: false` is the default;
- routine domain failures are structured results rather than transport exceptions;
- an exact output schema is published for every operation/facade result.

The initial hand-written MCP facade is transitional. The generated contract layer replaces reflection-only `string op + arbitrary JSON` metadata as an early v2 milestone.

## Stable result envelope

Eye operations return one stable envelope shape:

```json
{
  "ok": true,
  "result": {}
}
```

or:

```json
{
  "ok": false,
  "error": {
    "code": "invalid_argument",
    "message": "run.file_name is required",
    "retryable": true,
    "expected": {
      "required": ["file_name"]
    }
  }
}
```

Operation-specific result payloads are typed inside `result`.

## Compatibility and versioning

- Breaking public changes create a new contract version rather than silently mutating v1.
- The server may accept documented legacy aliases internally when doing so is cheap and unambiguous.
- Generated artifacts must never become an independent source of truth.
- A normalized `tools/list` snapshot test must fail when a normal implementation change alters a public tool name, description, annotation, input schema, output schema, or operation assignment.

## Contract-change rule

A public contract revision requires explicit owner authorization. Adding an internal capability does not implicitly publish or reclassify it.

The repository-level guardrail is also recorded in `AGENTS.md`.

## Metadata style

Descriptions and annotations should be precise and neutral. Describe what an operation does and where its effects occur. Avoid theatrical descriptions of authority; they add no capability and make model/tool classification less precise.

## Growth rule

Do not pre-populate the public contract with speculative capabilities merely to make the schema look complete. Add operations when implementations or near-term measured workloads justify them.

The five facade names are stable; their operation sets can grow through explicit contract revisions.
