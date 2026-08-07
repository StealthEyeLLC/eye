# AGENTS.md

## Public contract freeze

The public ChatGPT/MCP contract is frozen by default.

Do not edit `contracts/eye-mcp-v1.json`, public tool names, public descriptions, effect annotations, input schemas, output schemas, or generated public-contract artifacts unless the owner explicitly requests a public Eye contract revision.

New internal operations must not alter the published contract implicitly.

The canonical model-facing surface is the five effect-class facade design:

- `eye_inspect`
- `eye_run`
- `eye_change`
- `eye_interact`
- `eye_external`

All five route to one internal operation registry/dispatcher. The facade split is for schema and effect accuracy, not for internally reducing owner-granted authority.

## Architecture source of truth

Before making architectural changes, read:

1. `docs/EYE_CANON.md`
2. `docs/EYE_DECISIONS.md`
3. `docs/MCP_CONTRACT.md`
4. `docs/RESEARCH.md`
5. `docs/OSS_LANDSCAPE.md`

A successful experiment or attractive dependency is evidence, not automatically a canonical design change.

When a canonical decision changes, update the source documents rather than leaving contradictory instructions in older notes.

## Design filter

Prefer, in order:

1. Windows/.NET native facilities already present on the machine.
2. A small well-understood dependency with a concrete measured benefit.
3. An external/on-demand engine when embedding would add permanent runtime weight.

Do not add speculative architecture merely because a technology is available.

Eye is a privileged capability substrate operated by ChatGPT. It is not an autonomous-agent framework, workflow engine, plugin marketplace, policy framework, approval engine, generic multi-machine orchestrator, or container platform.

## Authority posture

If the owner intentionally grants an authority to Eye, do not manufacture additional Eye-internal approval or privilege friction on top of that grant. Real Windows, OpenAI, provider, hardware, network, and execution-environment boundaries remain real.

## Secrets

Secret names and roles may be documented. Secret values, passwords, private keys, recovery material, and plaintext credentials must not be committed.

## Dependency and licensing rule

Before importing third-party source or adding a runtime dependency, verify the exact pinned revision/license and any model/data sub-licenses. Prefer permissive dependencies. Treat GPL/AGPL code as external/isolated unless the owner deliberately accepts the obligations. Do not copy code that has no usable license.
