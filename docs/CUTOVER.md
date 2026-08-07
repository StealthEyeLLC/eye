# CUTOVER.md

**Status:** Core cutover complete; only old-profile/connector choices remain  
**Machine:** `STEALTHEYELLC`

## Completed — preservation

- [x] Old `se` repository/history preserved on `E:`.
- [x] Complete verified Git bundle preserved.
- [x] Important old `X:` payload independently archived.

## Completed — Windows identity

- [x] Local administrator account `StealthEye` created.
- [x] `C:\Users\StealthEye` verified as active interactive profile.
- [x] Automatic console sign-in configured.
- [x] Restart verified to go directly to the `StealthEye` desktop.
- [x] Direct Eye service/tunnel path survives reboot.
- [x] Git author identity set to `StealthEye <stealtheye.eye@gmail.com>`.

The local account password is intentionally not recorded here.

## Completed — pagefile and old X removal

- [x] `C:\pagefile.sys` is the active pagefile.
- [x] Old ~192 GiB `X:\pagefile.sys` removed from the active configuration by reboot.
- [x] Preservation archive and Git bundle reconfirmed.
- [x] Old `C:\Sovereign Node.vhdx` removed.

## Completed — physical Dev Drive

- [x] Re-queried supported `C:` shrink after old VHD deletion.
- [x] Shrunk `C:` by 300 GiB.
- [x] Created a physical 300 GiB partition on the internal Samsung NVMe.
- [x] Assigned `X:`.
- [x] Formatted as ReFS Dev Drive.
- [x] Label set to `Eye Dev`.
- [x] Trusted Dev Drive status verified.
- [x] Temporary fallback `C:\StealthEye-Dev.vhdx` deleted.

```text
X:  300 GiB  ReFS Dev Drive  "Eye Dev"
```

## Completed — repository location

- [x] Created `X:\Repos\eye`.
- [x] Cloned `StealthEyeLLC/eye` over SSH.
- [x] Clone/pull verified.
- [x] Old `se` source not imported.

Current laptop SSH authentication is read-only for the repo: a live push was rejected as a deploy key. This does not affect the connected GitHub control path, but steady-state laptop Git write authority remains an explicit design choice.

## Completed — fresh WSL

- [x] Installed Ubuntu 24.04 under the new Windows account.
- [x] Observed Ubuntu 24.04.4 LTS.
- [x] WSL2 verified.
- [x] systemd enabled/running.
- [x] root configured as default Linux user.
- [x] Plain launch verified without Linux credential ceremony.

## Completed — HEC / transitional cleanup

- [x] Disabled HEC tunnel proven unnecessary to direct Eye operation.
- [x] `HEC Laptop Tunnel` scheduled task removed.
- [x] `C:\Users\steal\HEC` removed.
- [x] Dedicated `laptop-to-hec-vps-ed25519` key pair removed.
- [x] Old HEC handshake file removed.
- [x] Old `steal`-bound `StealthEye Session` task removed.
- [x] `StealthEye Session - New Account` retained because the current prototype still uses it.
- [x] Tailscale service stopped; direct Eye path remained healthy.
- [x] Tailscale service disabled. Package retained temporarily for easy reversal.

## Completed — old-profile inventory

The old profile was inspected without deleting or hydrating cloud content.

Desktop/Documents/Downloads contain only small amounts of old development/prototype material.

The remaining retention question is iCloud:

- `iCloudDrive`: 15,766 reparse-backed files, ~2.78 GB logical.
- `iCloudPhotos`: 472 files, ~3.02 GB logical; 463 marked offline.

Do not blindly copy these trees merely to force cloud placeholders to download.

## Remaining — old profile

Choose one:

- trust the existing iCloud/cloud copy and retire the old local profile; or
- intentionally hydrate/archive selected iCloud material before retirement.

Only after that choice, remove the old `steal` account/profile and old per-user WSL registration.

## Remaining — connected identity

Google Drive, Calendar and Contacts are connected under the Eye Google identity. The ChatGPT Gmail connector is currently pointed at the owner's personal Gmail instead of `stealtheye.eye@gmail.com`.

Switch Gmail to the Eye identity before treating Eye mail authority as live. Do not operate the personal mailbox as Eye.

## Remaining — optional platform choices

- Decide whether `E:` remains exFAT or is later reformatted NTFS after another safe copy exists.
- Install Linux/user-local packages only as actual work requires them.
- Decide whether local Ollama/LM Studio/Python applications are needed under the new profile.
- Verify the favored DPAPI-NG credential blob across a later reboot before making it canonical.
- Uninstall disabled Tailscale later if no unrelated need appears.

## Next project phase

1. Resolve the iCloud/profile and Gmail connector choices.
2. Perform the final small architecture pass against `EYE_CANON.md`, `EYE_DECISIONS.md`, live probes and current vendor docs.
3. Resolve only the provisional decisions necessary to start.
4. Begin the minimal clean implementation in `X:\Repos\eye`.
5. Do **not** import the old `se` implementation wholesale.

## Current success state

Already true:

- `StealthEye` is the active interactive identity;
- restart goes straight to desktop;
- direct ChatGPT -> Secure MCP Tunnel -> Eye service survives reboot;
- old fixed 400 GB VHDX is gone;
- `X:` is the final physical Dev Drive;
- `E:` contains bulk models/data/archives;
- fresh WSL belongs to `StealthEye`;
- HEC residue is removed;
- Tailscale is not required by Eye and is disabled;
- `X:\Repos\eye` is the clean permanent workspace.
