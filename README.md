# Eye

Eye is the laptop-native execution substrate for **StealthEye**: one Windows service and one stable MCP tool through which ChatGPT can operate the dedicated StealthEye machine with broad, predictable authority.

## Identity

- Product: **StealthEye**
- Repository: **`StealthEyeLLC/eye`**
- Executable: **`eye.exe`**
- Public MCP tool: **`eye`**
- Planned local repository: **`X:\Repos\eye`**

## Core invariant

If the owner has intentionally granted an authority to Eye, Eye should not manufacture additional authority friction on top of that grant.

Real boundaries imposed by Windows, OpenAI, GitHub, Google, hardware, networking, or the execution environment still apply.

## Target shape

```text
ChatGPT
  -> OpenAI Secure MCP Tunnel
  -> tunnel-client on STEALTHEYELLC
  -> loopback MCP
  -> eye.exe Windows service (LocalSystem)
```

The final runtime is intentionally small:

- one permanent LocalSystem Windows service;
- one stable MCP tool: `eye({ op, args })`;
- native Windows process/session APIs for active-user execution;
- native Job Objects and ConPTY;
- WSL launched through the active-user token;
- installed Chrome controlled through a dedicated StealthEye profile and loopback CDP;
- short-lived on-demand interactive-session workers for desktop/UI operations;
- Secure MCP Tunnel kept outside Eye as transport only.

No permanent user-session daemon is intended in the final design.

## Repository phase

This repository starts **documentation-first**. The previous `se` codebase is prototype/history material and is not being copied wholesale into Eye.

Implementation begins only after the remaining laptop/account/storage cutover and the small v2 architecture are settled.

## Source documents

- [`docs/EYE_CANON.md`](docs/EYE_CANON.md) — canonical product and architecture source.
- [`docs/EYE_PLATFORM.md`](docs/EYE_PLATFORM.md) — current laptop/platform state and cutover boundary.
- [`docs/EYE_DECISIONS.md`](docs/EYE_DECISIONS.md) — canonical vs provisional vs open decisions.
- [`docs/HARDWARE.md`](docs/HARDWARE.md) — live-observed machine hardware/runtime snapshot.
- [`docs/RESEARCH.md`](docs/RESEARCH.md) — current vendor-documentation findings that materially affect implementation.

## Credentials

Secret **names and roles** may be documented; secret **values must never be committed**.

Current bootstrap secret names:

- `EyeRuntime`
- `OpenAIAdmin`

Eye's operational Google identity is `stealtheye.eye@gmail.com`.

## Status

Platform transition and architecture definition are in progress. The repository intentionally contains no inherited prototype implementation yet.
