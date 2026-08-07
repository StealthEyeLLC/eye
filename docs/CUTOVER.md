# CUTOVER.md

**Status:** Platform cutover complete  
**Machine:** `STEALTHEYELLC`

## Completed — preservation

- [x] Old `se` repository/history preserved on `E:`.
- [x] Complete verified Git bundle preserved.
- [x] Important old `X:` payload independently archived.
- [x] Old-profile local material deliberately retained under `E:\StealthEye\archives\pre-profile-retirement-20260807`.

## Completed — Windows identity

- [x] Local administrator account `StealthEye` created.
- [x] `C:\Users\StealthEye` verified as active interactive profile.
- [x] Automatic console sign-in configured.
- [x] Repeated restart verified to go directly to the `StealthEye` desktop.
- [x] Direct Eye service/tunnel path survives reboot.
- [x] Git author identity set to `StealthEye <stealtheye.eye@gmail.com>`.
- [x] Previous local account `steal` removed.
- [x] Previous Win32 profile registration removed.
- [x] Residual `C:\Users\steal` directory removed.

The local account password is intentionally not recorded here.

## Completed — pagefile and old X removal

- [x] `C:\pagefile.sys` is the active pagefile.
- [x] Old ~192 GiB `X:\pagefile.sys` removed from active configuration.
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

Current laptop SSH authentication is read-only/deploy-key authority for the repo. Steady-state machine-side Git write/admin authority remains a later non-blocking authority choice.

## Completed — fresh WSL

- [x] Installed Ubuntu 24.04 under the StealthEye Windows account.
- [x] Observed Ubuntu 24.04.4 LTS.
- [x] WSL2 verified.
- [x] systemd enabled/running.
- [x] root configured as default Linux user.
- [x] Plain launch verified without Linux credential ceremony.
- [x] After old-profile retirement, current user WSL registration contains only `Ubuntu-24.04`.

## Completed — HEC / transitional cleanup

- [x] HEC tunnel proven unnecessary to direct Eye operation.
- [x] HEC laptop scheduled task removed.
- [x] HEC user folder/handshake/dedicated SSH key residue removed.
- [x] Final targeted scan found no HEC-specific service/task/standard install path.
- [x] Old `steal`-bound StealthEye session task removed.
- [x] `StealthEye Session - New Account` retained only because the current prototype still uses it.
- [x] Tailscale service stopped and direct Eye path reverified.
- [x] Tailscale service disabled; package retained temporarily for easy reversal.

## Completed — old-profile retirement

The old profile was inspected before deletion.

Cloud-only iCloud placeholders were not intentionally hydrated merely to make a redundant local archive.

Before retirement, local material was preserved under:

```text
E:\StealthEye\archives\pre-profile-retirement-20260807
```

including Desktop, Documents, Downloads, a profile inventory, and seven locally resident iCloud Photos media files (~44 MiB).

Then the old account/profile and residual directory were removed.

## Completed — Google connector identity

- [x] Gmail connected as `stealtheye.eye@gmail.com` and verified with live Eye-addressed mail.
- [x] Google Drive connected as the Eye identity.
- [x] Google Calendar connected as the Eye identity.
- [x] Google Contacts connected as the Eye identity.

## Completed — unattended machine secret validation

- [x] LocalSystem protected throwaway random plaintext with DPAPI-NG `LOCAL=user`.
- [x] Encrypted blob persisted across reboot.
- [x] LocalSystem decrypted it successfully after reboot.
- [x] Interactive `StealthEye` user could not decrypt it.
- [x] Temporary probe artifacts removed.
- [x] Mechanism promoted into canonical v2 design.

## Remaining optional/non-blocking platform choices

- Decide whether `E:` ever moves from exFAT to NTFS after another safe copy exists.
- Install Linux/user-local packages only as actual work requires them.
- Decide whether local Ollama/LM Studio/Python applications are needed under the new profile.
- Uninstall disabled Tailscale later if no unrelated need appears.
- Choose broader machine-side GitHub authority only when `eye.exe` needs it.

## Next project phase

The platform boundary is no longer a blocker.

The small v2 architecture is frozen for initial implementation. Build order is now:

```text
1. minimal LocalSystem service
2. native active-user execution
3. native ConPTY terminal
4. WSL execution
5. installed Chrome + loopback CDP
6. on-demand desktop worker / native UI Automation
7. external authority operations as concrete needs appear
8. replace prototype runtime and remove transitional session helper
```

Do **not** import the old `se` implementation wholesale.

## Success state

All core cutover conditions are true:

- `StealthEye` is the active interactive identity;
- restart goes straight to desktop;
- direct ChatGPT -> Secure MCP Tunnel -> Eye service survives reboot;
- old fixed 400 GB VHDX is gone;
- `X:` is the final physical Dev Drive;
- `E:` contains bulk models/data/archives;
- fresh WSL belongs to StealthEye;
- old `steal` account/profile is retired after preservation;
- HEC laptop residue is removed;
- Eye Google connectors use the Eye identity;
- DPAPI-NG unattended secret persistence is reboot-validated;
- `X:\Repos\eye` is the clean permanent workspace.
