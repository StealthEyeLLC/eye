# EYE_DECISIONS.md

**Status:** Decision ledger and open-items guardrail  
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

### Windows interactive identity

The dedicated Windows interactive account is:

```text
StealthEye
```

It is a local administrator and is configured for automatic console sign-in to the desktop. Routine operation should not require manual sign-in.

Do not store the account password in repository documentation.

### Development volume

The final development drive is:

```text
X:
```

It is a **300 GiB physical ReFS Dev Drive partition** on the internal Samsung NVMe and is marked trusted by Windows Dev Drive tooling.

Do not replace it with a VHD/VHDX without a concrete reason.

### External Eye identity

Use:

```text
stealtheye.eye@gmail.com
```

as Eye's operational Google identity.

The intended ChatGPT connection set is Gmail, Drive, Calendar and Contacts under this identity.

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

### User execution

Favor:

- active-session discovery;
- `WTSQueryUserToken`;
- `CreateProcessAsUser`;
- native environment block creation;
- direct inherited pipes;
- Job Objects around the actual child process.

### Terminal

Favor native ConPTY and remove the old Pty.Net dependency if no missing capability appears.

Use the current ConPTY lifetime APIs available on the laptop, including `ReleasePseudoConsole` where appropriate.

### Browser

Favor installed Chrome + dedicated StealthEye profile + loopback CDP.

Avoid a bundled browser and Playwright runtime if direct CDP remains sufficient.

### Desktop worker

Favor short-lived on-demand `eye.exe worker` processes in the active session.

Do not keep a permanent worker alive unless measured performance later proves it worthwhile.

### UI Automation

Favor native Windows UI Automation / COM.

### WSL baseline

Current implemented baseline under the `StealthEye` Windows account:

```text
Ubuntu 24.04.4 LTS
WSL2
systemd enabled
root default user
```

This is intentionally friction-light and is the favored baseline. It can still change if implementation demonstrates a concrete reason for a non-root default user or different distro.

### Tunnel supervision

Favor running official `tunnel-client` externally under ordinary Windows startup/supervision rather than custom code inside Eye.

Exact final mechanism remains open between the official runtime-management path and a simple Windows startup/scheduled mechanism.

### Secret persistence on laptop

The laptop will need steady-state access to credentials Eye must use independently of GitHub Actions.

Favored mechanism: **DPAPI-NG `LOCAL=user` invoked by the LocalSystem Eye service**, with only encrypted blobs and non-secret metadata persisted on disk.

A live throwaway probe succeeded under SYSTEM and the same blob could not be decrypted by the interactive user. A direct `SID=S-1-5-18` protection attempt failed during encryption and is not favored.

Keep this provisional until a deliberately persisted throwaway blob survives and decrypts after a reboot.

## 3. Open decisions

### Old-profile retirement

The old `steal` account/profile remains until its user-facing data is reviewed.

Decide what, if anything, to preserve from:

- Documents
- Downloads
- iCloudDrive
- iCloudPhotos
- other old-profile application/user data

Then retire the old account and its old WSL registration.

### E: filesystem

Keep exFAT for now.

Possible NTFS reformat is optional and should occur only after protected data has another safe copy.

### WSL package set

The distro is established, but do not preinstall a huge Linux toolchain merely because it is available. Add packages as concrete work requires them.

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

The local Eye repo successfully clones over the StealthEye SSH identity.

Still decide what machine credential to use if `eye.exe` itself should administer GitHub beyond ordinary Git operations.

Do not choose a repo-scoped deploy key if the intended authority is broader than one repository.

### Tunnel startup implementation

The prototype's current tunnel supervision is transitional. Final v2 startup/supervision mechanism remains unresolved.

## 4. Build order

Current order:

```text
1. Finish residual old-profile/HEC platform cleanup
2. Freeze the small v2 architecture
3. Implement the minimal service in X:\Repos\eye
4. Add native user execution
5. Add terminal/ConPTY
6. Add WSL
7. Add browser/CDP
8. Add on-demand desktop worker/UI Automation
9. Add external authority operations only as concrete needs appear
10. Replace the old prototype service/tunnel arrangement
11. Remove transitional session helper and remaining prototype residue
```

The account, pagefile, physical Dev Drive, fresh WSL and repository-location cutover are complete.

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
