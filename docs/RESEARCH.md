# RESEARCH.md

**Status:** Vendor-documentation findings that materially affect Eye implementation  
**Baseline date:** 2026-08-07

This file records external documentation findings separately from live experiments and canonical design. A finding can support a provisional choice without automatically making that choice canonical.

Prefer primary/vendor documentation. Re-check implementation-sensitive details when the corresponding code is written.

## 1. LocalSystem -> interactive-user process launch

Microsoft documents `WTSQueryUserToken` as obtaining the primary access token of a logged-on user for a specified session. Microsoft specifically notes that callers must run as LocalSystem with the required privilege and that the API is intended for highly trusted services.

Microsoft also documents `CreateEnvironmentBlock` as the way to obtain the selected user's environment for `CreateProcessAsUser` and documents `CreateProcessAsUser` support for an interactive `winsta0\default` desktop.

This matches the live Eye experiments showing a real LocalSystem SCM service launching the active user into the interactive session with captured stdout/stderr.

Primary references:

- https://learn.microsoft.com/en-us/windows/win32/api/wtsapi32/nf-wtsapi32-wtsqueryusertoken
- https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-createprocessasusera
- https://learn.microsoft.com/en-us/windows/win32/api/userenv/nf-userenv-createenvironmentblock

Implementation implication: native service-owned active-user execution is a supported Windows design direction; a permanent logon helper is not required merely to cross from LocalSystem into the active user session.

## 2. Native ConPTY lifetime / EOF handling

Microsoft added `ReleasePseudoConsole` with a minimum supported client of Windows 11 24H2 / build 26100.

The StealthEye laptop is currently on build 26200, so the v2 implementation can target this newer lifetime API rather than inheriting older pseudoconsole shutdown patterns where appropriate.

Primary reference:

- https://learn.microsoft.com/en-us/windows/console/releasepseudoconsole

Implementation implication: use current native ConPTY APIs and explicitly design pseudoconsole lifetime/EOF handling around the current Windows contract.

## 3. Chrome remote debugging

Chrome changed remote-debugging behavior starting with Chrome 136. Remote debugging switches against the normal Chrome data directory are restricted; Chrome recommends using a non-standard `--user-data-dir` for debugging isolation.

Primary reference:

- https://developer.chrome.com/blog/remote-debugging-port

Implementation implication: Eye's favored **dedicated StealthEye Chrome profile + loopback CDP** is not only cleaner than controlling the user's normal browser profile; it aligns with current Chrome remote-debugging behavior.

## 4. Physical Dev Drive vs VHD/VHDX

Microsoft's Dev Drive documentation states that a partition-based Dev Drive generally provides faster performance by accessing the physical disk directly without the additional virtual-disk layer. A VHD-based Dev Drive can have slightly lower performance due to virtual disk overhead.

Primary reference:

- https://learn.microsoft.com/en-us/windows/dev-drive/

Implementation/platform implication: the favored final physical ReFS Dev Drive `X:` on the internal Samsung NVMe remains the preferred direction, with the already-tested dynamic VHDX retained only as fallback.

The same documentation notes that WSL's `metadata` mount option is not supported on ReFS volumes. Therefore Linux-native workloads that depend heavily on Unix ownership/permission metadata should live in the WSL Linux filesystem rather than assuming `X:` can substitute for the distro filesystem.

## 5. SYSTEM-targeted secret protection candidate

Microsoft's CNG DPAPI / DPAPI-NG APIs support protection descriptors based on principals/SIDs. `NCryptCreateProtectionDescriptor` accepts SID-based protection rules, and `NCryptProtectSecret` / `NCryptUnprotectSecret` provide the protection/unprotection path.

Primary references:

- https://learn.microsoft.com/en-us/windows/win32/api/ncryptprotect/nf-ncryptprotect-ncryptcreateprotectiondescriptor
- https://learn.microsoft.com/en-us/windows/win32/api/ncryptprotect/nf-ncryptprotect-ncryptprotectsecret
- https://learn.microsoft.com/en-us/windows/win32/api/ncryptprotect/nf-ncryptprotect-ncryptunprotectsecret
- https://learn.microsoft.com/en-us/windows/win32/seccng/cng-dpapi-constants

Provisional implementation candidate:

```text
Protect Eye's steady-state secret blobs to the LocalSystem principal (SID S-1-5-18)
and persist only encrypted blobs + non-secret metadata on disk.
```

This should be live-tested before becoming canonical. The goal is not to downscope Eye's authority; it is to make the deliberately granted credentials available unattended to the SYSTEM-owned Eye service without writing plaintext secrets into the public repository or ordinary profile files.

## 6. OpenAI Secure MCP Tunnel

OpenAI's current documentation states that when an MCP server runs on a private network, on-premises, or on a developer machine, Secure MCP Tunnel is the supported mechanism for connecting it to supported OpenAI products without exposing that MCP server directly to the public internet.

Primary reference:

- https://help.openai.com/en/articles/12584461-developer-mode-and-mcp-apps-in-chatgpt

Implementation implication: retain the canonical separation:

```text
tunnel-client -> loopback Eye MCP endpoint
```

The tunnel remains transport. Eye should not grow a custom tunnel subsystem unless an actual requirement appears.

## 7. Research discipline

Before implementing an OS/provider-specific capability:

1. check the current primary vendor documentation;
2. compare it with a minimal live probe on `STEALTHEYELLC` where practical;
3. record surprising constraints or useful new APIs here;
4. promote a result to `EYE_DECISIONS.md` or `EYE_CANON.md` only when it becomes an accepted project decision.

This keeps web research useful without allowing documentation browsing to turn into speculative architecture.
