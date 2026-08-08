# AGENTS.md

## Public contract freeze

The public ChatGPT/MCP contract is frozen by default.

Historical v1 contract:

```text
contracts/eye-mcp-v1.json
```

Canonical target v2 contract:

```text
contracts/eye-mcp-v2.json
```

Do not edit public tool names, descriptions, annotations, input/output schemas, generated descriptors, server instructions, or public-contract artifacts unless the owner explicitly requests a public Eye contract revision.

The canonical v2 top-level surface is exactly:

```text
eye_inspect
eye_run
eye_change
eye_interact
eye_external
eye_live
```

The first five are effect-class capability facades. `eye_live` is UI-only and performs no machine operation itself.

`wait` and `transfer` are operation families beneath the facades, not additional top-level tools.

Routine implementation must not create a seventh public tool or silently mutate the six-tool contract.

## Architecture source of truth

Before architectural work, read in this order:

1. `docs/BUILD_BLUEPRINT.md`
2. `docs/EYE_CANON.md`
3. `docs/EYE_DECISIONS.md`
4. `docs/MCP_CONTRACT.md`
5. `contracts/eye-mcp-v2.json`
6. `docs/EYE_OPERATOR_SKILL.md`
7. `docs/RESEARCH.md`
8. `docs/OSS_LANDSCAPE.md`

A successful experiment, attractive dependency, or useful research idea is evidence, not automatically a canonical design change.

When canon changes, update the source documents rather than leaving contradictory instructions in old notes.

## Eye Operator skill rule

`docs/EYE_OPERATOR_SKILL.md` is the canonical source for the ChatGPT-side **Eye Operator** operating doctrine.

The skill is a first-class project deliverable, but it is not part of `eye.exe`, adds no authority of its own, and must never become a runtime dependency for Eye.

The MCP contract and server instructions remain the correctness/routing boundary. The skill carries deeper operating procedure: modality choice, durable jobs, waits, artifacts, stable handles/cursors, Eye Live usage, Blackboard/Relay behavior, machine conventions, and recovery guidance.

Do not let a generated/packaged skill redefine the public contract independently. When contract or canonical operating semantics change, update the canonical source first and keep the packaged skill aligned.

## Architecture stop rule

The architecture is considered mature enough to build.

Default question for new capability work:

> Where does this fit underneath the existing blueprint?

Do not add new architectural layers unless the owner explicitly authorizes a canonical revision.

## One-service fault boundary

There is exactly one permanent LocalSystem SCM service.

The stable host supervises a **separate versioned capability-engine child process**. Do not load the replaceable capability engine as a DLL into the stable host.

Risky UIA/CDP/capture/media/GPU/provider feature code belongs in the replaceable engine or version-matched short-lived workers, not in the stable host.

The stable host owns the minimal repair/control substrate:

- MCP endpoint and public contract;
- raw SYSTEM/user/WSL repair execution;
- Job Objects and ConPTY;
- durable jobs/streams;
- artifacts;
- triggers;
- Mission Blackboard;
- stable identities/cursors;
- minimal Eye Live;
- engine protocol/supervision/A-B rollback;
- tiny authoritative state.

Routine feature development should almost never modify the stable host.

## Degraded-mode invariant

A broken or absent capability engine must not remove the only ChatGPT control path.

Without the engine, the host must retain status/capabilities, engine restart/activate/rollback, raw SYSTEM/user/WSL execution, jobs/terminals, artifact reads, mission/trigger state, and minimal Eye Live monitoring.

Do not move these capabilities into the replaceable engine.

## Host/engine protocol rule

The protocol is small and independently versioned. Activation requires a compatible protocol/build/contract-hash/supported-operation/worker-protocol handshake.

New engine versions should normally remain compatible with the existing host. Host changes are deliberately rare.

## Durable work rule

Long-running work is host-owned.

Do not implement long work by holding one MCP request open indefinitely or buffering all output in RAM.

Use durable jobs, Job Objects, cursor-based output, artifact spill, native waits, and host-owned ConPTY where appropriate.

## Stable identity rule

Use:

```text
stable object ID + incarnation generation + observation cursor
```

Do not treat a mutable label/title/path/PID/HWND alone as durable identity when reuse/replacement is possible.

Prefer deltas over repeated full snapshots.

## Eye Live parity rule

Eye Live is optional UI acceleration. Everything meaningful it displays or controls must remain available through ordinary MCP operations.

App-only UI helper tools must not pollute model tool selection.

Do not make core Eye capability depend on a widget mounting successfully.

## Blackboard rule

The Mission Blackboard stays compact and fixed-purpose: objective, facts/decisions, jobs/triggers, artifacts, unresolved questions, next action, relay messages.

Do not turn it into a transcript archive, task taxonomy, receipt database, generic DAG/workflow engine, or scheduler language.

## Design filter

Prefer, in order:

1. Existing Windows/.NET native facilities.
2. A host primitive only when required for control-path durability/recovery.
3. Replaceable engine capability for evolving feature logic.
4. Short-lived session worker for interactive/native-user work.
5. Small well-understood dependency with concrete benefit.
6. External/on-demand engine when embedding adds unnecessary permanent weight.

Do not add speculative architecture merely because a technology is available.

Eye is a privileged capability substrate operated by ChatGPT. It is not a second agent brain, generic workflow engine, plugin marketplace, policy framework, approval engine, generic multi-machine orchestrator, container platform, or permanent local planner.

## Authority posture

If the owner intentionally grants an authority to Eye, do not manufacture additional Eye-internal approval or privilege friction on top of that grant.

Real Windows, ChatGPT/OpenAI, provider, hardware, power, network, and execution-environment boundaries remain real.

## Login/account boundary

Do not change Windows login/account/autologon architecture unless the owner explicitly requests it.

## Secrets

Secret names and roles may be documented.

Secret values, passwords, private keys, recovery material, plaintext credentials, tokens, and decrypted secret blobs must not be committed.

## Dependency and licensing rule

Before importing third-party source or adding a runtime dependency, verify the exact pinned revision/license and any model/data sub-licenses.

Prefer permissive dependencies. Treat GPL/AGPL code as external/isolated unless the owner deliberately accepts the obligations. Do not copy code that has no usable license.
