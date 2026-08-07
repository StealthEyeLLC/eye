# CUTOVER.md

**Status:** Core cutover complete; residual cleanup remains  
**Machine:** `STEALTHEYELLC`

This checklist now records the completed physical cutover and the remaining cleanup boundary.

## Completed — preservation

- [x] Old `se` repository/history preserved on `E:`.
- [x] Complete verified Git bundle preserved.
- [x] Important old `X:` payload independently archived.

## Completed — Windows identity

- [x] Local administrator account `StealthEye` created.
- [x] `C:\Users\StealthEye` created and verified as active interactive profile.
- [x] GitHub SSH identity works under the new account.
- [x] Git author identity corrected to `StealthEye <stealtheye.eye@gmail.com>`.
- [x] Automatic console sign-in configured with Microsoft Sysinternals Autologon.
- [x] Restart verified to go directly to the `StealthEye` desktop.
- [x] Direct Eye service/tunnel path survives reboot.

The local account password is intentionally not recorded here.

## Completed — pagefile and old X removal

- [x] Reboot applied the `C:` pagefile transition.
- [x] `C:\pagefile.sys` observed at 16 GiB current allocation.
- [x] Old ~192 GiB `X:\pagefile.sys` no longer exists/is active.
- [x] Preservation archive and Git bundle reconfirmed before destructive storage work.
- [x] Old `C:\Sovereign Node.vhdx` removed.
- [x] Expected ~400 GB of physical `C:` free space returned.

## Completed — final physical Dev Drive

- [x] Re-queried supported `C:` shrink after old VHD deletion.
- [x] Supported maximum shrink observed at ~304.9 GiB.
- [x] Shrunk `C:` by exactly 300 GiB.
- [x] Created a physical partition on the internal Samsung NVMe.
- [x] Assigned `X:`.
- [x] Formatted using Windows Dev Drive semantics.
- [x] ReFS verified.
- [x] Label set to `Eye Dev`.
- [x] `fsutil devdrv query X:` reports a trusted developer volume.
- [x] Deleted the temporary fallback `C:\StealthEye-Dev.vhdx`.

Final development volume:

```text
X:  300 GiB  ReFS Dev Drive  "Eye Dev"
```

## Completed — repository location

- [x] Created `X:\Repos`.
- [x] Cloned `StealthEyeLLC/eye` to `X:\Repos\eye` over SSH.
- [x] Confirmed clean `main...origin/main` state immediately after clone.
- [x] Kept old `se` implementation out of the clean repository.

## Completed — fresh WSL

- [x] Verified the new Windows account initially had no WSL distributions.
- [x] Installed `Ubuntu-24.04`.
- [x] Observed Ubuntu 24.04.4 LTS.
- [x] Verified WSL2 kernel `6.6.87.2-microsoft-standard-WSL2`.
- [x] Preserved/enabled systemd.
- [x] Configured root as the default Linux user.
- [x] Verified plain launch runs as UID 0.
- [x] Verified `systemctl is-system-running` reports `running`.

## Remaining — old user review

Do **not** delete `C:\Users\steal` blindly.

Review at least:

- Documents
- Downloads
- iCloudDrive
- iCloudPhotos
- any other obvious user-facing data

Preserve anything the owner wants, then retire the old `steal` account/profile and its old per-user WSL registration.

## Remaining — transitional infrastructure

- Remove inert HEC files/keys/task residue.
- Keep the current prototype `StealthEye Session - New Account` helper only while prototype user-context operations depend on it.
- Remove that helper when the clean v2 LocalSystem service implements the proven native active-user launch path.
- Do not disturb the working direct Secure MCP Tunnel until the clean replacement transport startup is proven.

## Remaining — optional platform choices

- Decide whether `E:` remains exFAT or is later reformatted NTFS after another safe copy exists.
- Install Linux/user-local packages only as actual work requires them.
- Decide whether local Ollama/LM Studio/Python applications are needed under the new profile.
- Verify the favored DPAPI-NG credential blob across a later reboot before making it canonical.

## Next project phase

After residual old-profile/HEC cleanup:

1. Perform the final small architecture pass against `EYE_CANON.md`, `EYE_DECISIONS.md`, live probes and current vendor docs.
2. Resolve only the provisional decisions necessary to start.
3. Begin the minimal clean implementation in `X:\Repos\eye`.
4. Do **not** import the old `se` implementation wholesale.

## Current success state

Already true:

- intended `StealthEye` user is the active interactive identity;
- machine can restart straight to desktop;
- direct ChatGPT -> Secure MCP Tunnel -> Eye service survives reboot;
- old fixed 400 GB VHDX is gone;
- `X:` is the final physical development Dev Drive;
- `E:` contains bulk models/data/archives;
- fresh WSL belongs to `StealthEye`;
- `X:\Repos\eye` is the clean permanent workspace.

Remaining cleanup is no longer a blocker for the core account/storage cutover.
