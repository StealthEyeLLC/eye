# EYE_DECISIONS.md

**Status:** Decision ledger and architecture freeze guardrail  
**Baseline date:** 2026-08-07

This document prevents exploratory findings from silently becoming canonical Eye design.

Use three states:

- **Canonical** — approved direction; future work should preserve it unless explicitly changed.
- **Favored / provisional** — strong current preference supported by evidence, but exact form can still change.
- **Open** — intentionally unresolved.

## 1. Canonical decisions

### Identity

- Product: **StealthEye**
- Repository: **`StealthEyeLLC/eye`**
- Local repository: **`X:\Repos\eye`**
- Primary executable: **`eye.exe`**
- Public MCP tool: **`eye`**

### Public interface

Use one stable MCP tool shape:

```text
eye({ op, args })
```

### Core implementation language

C# / .NET is the core implementation language.

### Permanent runtime

One permanent Windows service:

```text
eye.exe service
```

Run it as LocalSystem.

Do not require a permanent user-session daemon in the final design.

### Authority posture

If the owner intentionally grants authority to Eye, StealthEye should not add avoidable internal authority friction on top of that grant.

Do not deliberately downscope broad credentials merely for architectural neatness.

### Transport separation

The OpenAI Secure MCP Tunnel is transport only.

Eye serves loopback MCP and remains independently useful without understanding tunnel lifecycle.

HEC/VPS infrastructure is not part of final Eye.

### Docker

Docker is not part of the target runtime.

### Windows interactive identity

The dedicated Windows interactive account is:

```text
StealthEye
```

It is a local administrator and is configured for automatic console sign-in to the desktop. Routine operation should not require manual sign-in.

Do not store the account password in repository documentation.

The previous `steal` account/profile has been retired after preserving the local material deliberately retained.

### Development volume

The final development drive is:

```text
X:
```

It is a **300 GiB physical ReFS Dev Drive partition** on the internal Samsung NVMe and is marked trusted by Windows Dev Drive tooling.

Do not replace it with a VHD/VHDX without a concrete reason.

### Native active-user execution

The LocalSystem service owns user-context execution. For commands that must run as the active interactive user, use the native Windows path:

- discover the active session;
- obtain its token with `WTSQueryUserToken`;
- build its environment with `CreateEnvironmentBlock`;
- launch with `CreateProcessAsUser`;
- capture stdio with inherited handles/pipes;
- assign the actual child to a service-owned Job Object;
- supervise process lifetime directly.

A permanent user-session helper is not part of v2.

### Terminal

Use native ConPTY for pseudoterminal execution.

Use current lifetime APIs available on this Windows build, including `ReleasePseudoConsole` where appropriate.

Do not carry the prototype Pty.Net dependency into v2 unless a concrete missing capability is demonstrated.

### Browser

Use installed Chrome with a dedicated StealthEye data/profile directory and loopback Chrome DevTools Protocol.

Prefer direct CDP control from the LocalSystem service over shipping a bundled browser or Playwright runtime when direct CDP is sufficient.

Keep the user's ordinary browser/profile separate.

### Desktop worker and UI Automation

Desktop-bound work uses short-lived `eye.exe worker` processes created on demand in the active session.

Use native Windows UI Automation / COM and native desktop/window/input APIs. Workers opt into Per-Monitor V2 DPI awareness before coordinate-sensitive work.

Do not keep a permanent desktop worker alive unless measured performance later proves it necessary.

### WSL baseline

The StealthEye Windows account uses:

```text
Ubuntu 24.04.4 LTS
WSL2
systemd enabled
root default user
```

Launch WSL through active-user execution from the service. Linux-native workloads requiring Unix permission/ownership semantics live inside the WSL Linux filesystem rather than on ReFS.

### Machine credential persistence

For secrets that the LocalSystem Eye service must retain on the laptop, use **DPAPI-NG with protection descriptor `LOCAL=user`, invoked by the LocalSystem service**.

Persist only the encrypted blob and non-secret metadata under a SYSTEM-owned machine path.

Live validation on `STEALTHEYELLC` established that:

- SYSTEM can protect and unprotect with `LOCAL=user`;
- the encrypted blob survives reboot and still decrypts under SYSTEM;
- the interactive `StealthEye` account can read the test blob but cannot decrypt it;
- a direct `SID=S-1-5-18` descriptor did not successfully protect on this machine and is not the chosen path.

No real credential was used in the validation.

### External Eye identity

Use:

```text
stealtheye.eye@gmail.com
```

as Eye's operational Google identity.

ChatGPT Gmail, Drive, Calendar and Contacts connections are now pointed at this Eye identity.

### OpenAI secret names

GitHub Actions secret names supplied by the owner:

```text
EyeRuntime
OpenAIAdmin
```

Do not place their values in source.

Neither is a GitHub deploy key.

### Old repository

The old `se` repository is prototype/history material.

Do not copy the old codebase wholesale into `eye`.

## 2. Favored / provisional decisions

### Tunnel supervision

Favor running official `tunnel-client` externally under ordinary Windows startup/supervision rather than custom code inside Eye.

The exact startup mechanism can remain a late implementation choice while the current working tunnel remains untouched.

## 3. Open decisions

These items do not block the frozen core architecture or initial implementation.

### E: filesystem

Keep exFAT for now.

Possible NTFS reformat is optional and should occur only after protected data has another safe copy.

### WSL package set

Do not preinstall a large Linux toolchain merely because it is available. Add packages as concrete work requires them.

### Google direct API access from eye.exe

ChatGPT has Google integrations, but standalone `eye.exe` direct Google API credentials remain optional.

Add them only if independent laptop-side Gmail/Drive/Calendar/Contacts operation provides a concrete benefit.

### OpenAI admin operation surface

The owner intends broad OpenAI admin authority.

Still decide whether Eye exposes:

- a general raw OpenAI API request operation; or
- a small set of broad organization-management operations.

Do not intentionally narrow the underlying credential's authority.

### GitHub machine authority

The local Eye repo successfully clones/pulls over the current StealthEye SSH identity, but a live push was rejected by GitHub as a deploy key.

The connected GitHub control path used by ChatGPT has repository admin/push authority. Choose a deliberate steady-state machine credential later if `eye.exe` itself should push or administer GitHub broadly.

Do not choose a repo-scoped deploy key if the intended authority is broader than one repository.

### Tunnel startup implementation

The prototype's current tunnel supervision is transitional. Final v2 startup/supervision remains intentionally late-bound until replacement is tested without risking the working control path.

### Tailscale package

Tailscale is disabled and was proven unnecessary to the Eye request path. The package may be uninstalled once there is no unrelated reason to retain it.

## 4. Architecture freeze and build order

The small v2 architecture is frozen as of 2026-08-07 for initial implementation.

Current order:

```text
1. Implement the minimal LocalSystem service in X:\Repos\eye
2. Add native active-user execution
3. Add native terminal/ConPTY
4. Add WSL execution
5. Add installed-Chrome/CDP browser control
6. Add on-demand desktop worker/UI Automation
7. Add external authority operations only as concrete needs appear
8. Replace the old prototype service/tunnel arrangement
9. Remove the transitional session helper and remaining prototype residue
```

Platform/account/storage/WSL/HEC/old-profile cleanup is complete. Exact tunnel startup and optional external authority surfaces are deliberately not blockers for the core build.

Do not begin by porting old `se` implementation wholesale.

## 5. First-repo-commit rule

The repository was initialized deliberately with v2 identity/design documentation rather than prototype baggage.

Implementation commits should preserve that clean break.

## 6. Architecture filter

Before adding a component, ask:

1. Does Windows/.NET already provide this natively?
2. Does it need to be permanent?
3. Does it reduce actual failure modes?
4. Is it required for a current capability?
5. Does it preserve the one-tool / one-service simplicity?
6. Is it adding avoidable authority friction?

If the component is mainly ceremony, future-proofing, policy layering, or generic platform machinery, do not add it yet.

## 7. Source-of-truth rule

A successful experiment is evidence, not automatically a canonical design change.

Promote it here only when the owner explicitly accepts the direction or the conversation clearly establishes it as the intended target.

When a canonical decision changes, update the source docs instead of leaving contradictory instructions scattered across old notes.
