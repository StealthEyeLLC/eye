# EYE_DECISIONS.md

**Status:** Decision ledger and open-items guardrail  
**Baseline date:** 2026-08-07

This document exists to prevent exploratory findings from silently becoming canonical Eye design.

Use three states:

- **Canonical** — approved direction; future work should preserve it unless explicitly changed.
- **Favored / provisional** — strong current preference supported by evidence, but exact form can still change.
- **Open** — intentionally unresolved.

## 1. Canonical decisions

### Identity

- Product name: **StealthEye**
- New repository: **`StealthEyeLLC/eye`**
- Planned local repository: **`X:\Repos\eye`**
- Primary executable: **`eye.exe`**
- Public MCP tool: **`eye`**

### Public interface

Use one stable MCP tool shape:

```text
eye({ op, args })
```

### Core implementation language

C# / .NET remains the preferred core.

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

### External Eye identity

Use:

```text
stealtheye.eye@gmail.com
```

as Eye's operational Google identity.

ChatGPT access is connected for Gmail, Drive, Calendar, and Contacts.

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

These are strong current directions but can still be changed without contradicting the canonical core.

### User execution

Favor:

- active-session discovery,
- `WTSQueryUserToken`,
- `CreateProcessAsUser`,
- native environment block creation,
- direct inherited pipes,
- Job Objects around the actual child process.

### Terminal

Favor native ConPTY and remove the old Pty.Net dependency if no missing capability appears.

### Browser

Favor installed Chrome + dedicated StealthEye profile + loopback CDP.

Avoid a bundled browser and Playwright runtime if direct CDP remains sufficient.

### Desktop worker

Favor short-lived on-demand `eye.exe worker` processes in the active session.

Do not keep a permanent worker alive unless measured performance later proves it worthwhile.

### UI Automation

Favor native Windows UI Automation / COM.

### Final X drive

Favor a real physical ReFS Dev Drive partition on the internal Samsung NVMe.

Approximate target size: ~300 GB.

Exact size is not final.

### WSL

Favor a fresh Ubuntu 24.04 LTS distribution under `StealthEye`.

Exact distro/default-user policy remains open.

### Tunnel supervision

Favor running official `tunnel-client` externally under ordinary Windows startup/supervision rather than custom code inside Eye.

Exact mechanism remains open between the official runtime-management path and a simple Windows scheduled/startup mechanism.

### Secret persistence on laptop

The laptop will need steady-state access to whatever credentials Eye must use independently of GitHub Actions.

Favored mechanism: **DPAPI-NG `LOCAL=user` invoked by the LocalSystem Eye service**, with only encrypted blobs and non-secret metadata persisted on disk.

A live throwaway probe on `STEALTHEYELLC` succeeded under SYSTEM and the same blob could not be decrypted by the current interactive user. A direct `SID=S-1-5-18` protection attempt failed during encryption and is no longer the favored form.

Keep this provisional until reboot persistence is verified during the controlled cutover.

## 3. Open decisions

### Account sign-in final form

The new `StealthEye` account exists and lock/power policy is largely configured.

The exact final passwordless/automatic-logon implementation is still blocked on one local interactive cutover.

### Physical Dev Drive size

Do not freeze the final `X:` size until after reboot, old fixed VHDX removal, and a fresh supported-shrink query.

### E: filesystem

Keep exFAT for now.

Possible NTFS conversion/reformat is optional and should happen only after protected data has another safe copy.

### Fresh WSL policy

Still decide:

- Ubuntu 24.04 or another distribution,
- default user/root policy,
- which packages are genuinely needed.

### Google direct API access from eye.exe

ChatGPT already has Google integrations.

Still decide whether the standalone `eye.exe` runtime also needs its own direct Google API credentials for Gmail/Drive/Calendar/Contacts.

Do not add this solely because it is possible; add it if standalone laptop operation benefits.

### OpenAI admin operation surface

The owner intends broad OpenAI admin authority.

Still decide whether Eye exposes:

- a general raw OpenAI API request operation, or
- a small number of broad organization-management operations.

Do not intentionally narrow the underlying credential's authority.

### GitHub machine authority

The repository is public, so clone/pull does not require authentication.

Still decide what machine credential to use if Eye itself should push/administer GitHub broadly.

Do not choose a repo-scoped deploy key if the intended authority is broader than one repository.

## 4. Build order

The project order remains:

```text
1. Finish laptop/account/storage cutover
2. Freeze the small v2 architecture
3. Create the first deliberate source/docs commit in StealthEyeLLC/eye
4. Implement the minimal service
5. Add native user execution
6. Add terminal/ConPTY
7. Add WSL
8. Add browser/CDP
9. Add on-demand desktop worker/UI Automation
10. Add external authority operations only as concrete needs appear
11. Replace the old prototype service/tunnel arrangement
12. Retire obsolete prototype/HEC residue
```

The documentation-only repository initialization has now begun while the remaining laptop cutover waits for physical access. This does not authorize implementation before steps 1–2 are complete.

Do not begin by porting old `se` implementation wholesale.

## 5. First-repo-commit rule

The first commit established deliberate Eye identity/design documentation rather than carrying prototype baggage.

Implementation commits should follow only after the laptop cutover and v2 architecture are settled.

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

When a new experiment succeeds, it is **evidence**, not automatically a canonical design change.

Promote it into this ledger only when the owner explicitly accepts the architectural direction or the conversation clearly establishes it as the intended target.

When a canonical decision changes, update the source docs rather than leaving contradictory instructions scattered across old notes.
