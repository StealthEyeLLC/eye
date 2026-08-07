# CUTOVER.md

**Status:** Pending local-owner action boundary  
**Machine:** `STEALTHEYELLC`

This is the operational checklist for the first physical-access window after the unattended platform preparation.

Do **not** skip directly to deleting the old user profile or old fixed VHDX.

## Preconditions already satisfied

- Old `se` repository/history is preserved on `E:`.
- A complete verified Git bundle exists.
- New local administrator account `StealthEye` exists.
- New profile has clean Git identity/config and GitHub SSH material.
- Next-boot pagefile is configured on `C:` at 16–32 GB.
- Docker has been removed.
- HEC tunnel is disabled and direct Eye access works without it.
- Machine-wide developer tooling is established.
- Bulk model/data layout on `E:` is established.
- Fallback dynamic ReFS Dev Drive VHDX has been tested and is detached.

## Phase 1 — local unlock and new-user transition

1. Owner physically unlocks the current Windows session.
2. Complete the final credential/sign-in configuration for the local `StealthEye` account.
3. Log into `StealthEye` once.
4. Verify `C:\Users\StealthEye` is the active interactive profile.
5. Verify Git/GitHub identity from that user context.
6. Verify the current direct Eye control path remains healthy.

Do not delete `steal` yet.

## Phase 2 — controlled reboot

7. Perform one controlled reboot.
8. Confirm the machine returns in the intended interactive-account state.
9. Confirm the Eye service and OpenAI tunnel transport return successfully.
10. Confirm `C:\pagefile.sys` is now the configured pagefile.
11. Confirm the old `X:\pagefile.sys` is no longer active.

If direct Eye connectivity does not return, stop destructive storage work until the transport/service path is restored.

## Phase 3 — remove old fixed X VHDX

12. Reconfirm the preservation archive and Git bundle on `E:` are readable.
13. Detach/unmount the old `X:` VHD if still attached.
14. Remove `C:\Sovereign Node.vhdx`.
15. Confirm the expected large amount of `C:` free space has returned.

The old VHDX is redundant only because its important payload/history has already been independently preserved.

## Phase 4 — create final development X

16. Re-query the supported shrink range for the Windows `C:` partition.
17. Choose the final physical Dev Drive size based on actual post-cleanup space. Current favored target is approximately 300 GB, not a fixed requirement.
18. Shrink `C:` accordingly.
19. Create a new partition from that internal NVMe space.
20. Format/configure it as a trusted ReFS Dev Drive.
21. Assign drive letter `X:`.
22. Verify normal user and LocalSystem read/write access.
23. Verify representative .NET build/repository workload on `X:`.

If the physical partition path is undesirable, use the already-tested `C:\StealthEye-Dev.vhdx` fallback instead.

## Phase 5 — new-account development environment

24. Establish a fresh WSL distribution under `StealthEye` rather than reusing the old `steal` registration.
25. Current favored distro: Ubuntu 24.04 LTS; confirm before installation if another choice has emerged.
26. Install/configure only the Linux packages actually required.
27. Establish user-local Python/Ollama/LM Studio components only as needed rather than copying old profile installations.
28. Keep large model payloads on `E:\StealthEye\models\...` where appropriate.

## Phase 6 — old-user review and retirement

29. Review `C:\Users\steal` for user-facing data that has not been intentionally preserved elsewhere.
30. Pay particular attention to:
    - Documents
    - Downloads
    - iCloudDrive
    - iCloudPhotos
31. Preserve anything the owner wants to keep.
32. Only then retire/delete the old `steal` account/profile.
33. Remove obsolete transitional `StealthEye Session` tasks/helpers once the final no-permanent-helper architecture no longer depends on them.
34. Remove inert HEC files/keys/task residue.

## Phase 7 — repository/build transition

35. Clone `StealthEyeLLC/eye` to `X:\Repos\eye`.
36. Confirm the documentation-first repository state.
37. Perform the final small architecture pass against `docs/EYE_CANON.md`, `docs/EYE_DECISIONS.md`, live probes and current vendor docs.
38. Promote/resolve provisional decisions only where necessary to start implementation.
39. Begin the minimal clean Eye implementation.

Do **not** import the old `se` implementation wholesale.

## Success condition

The platform cutover is complete when:

- the intended `StealthEye` user is the active interactive identity;
- machine can remain available without avoidable sign-in/sleep friction;
- direct ChatGPT -> Secure MCP Tunnel -> Eye service access survives reboot;
- `C:` no longer contains the old fixed 400 GB VHDX;
- `X:` is the final development Dev Drive;
- `E:` contains bulk models/data/archives;
- a fresh WSL environment belongs to `StealthEye`;
- old `steal` and HEC residue are safely retired;
- `X:\Repos\eye` is the clean implementation workspace.
