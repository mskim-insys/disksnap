# DiskSnap

Personal Windows disk-usage snapshot tool: scan drives, store compressed snapshots, diff two snapshots to find which folder or file grew. Single user, Windows 10/11 x64 only.

## Read first
- README.md for current usage and behavior
- Active specs: specs/disksnap.md (requirements, acceptance criteria, milestones M0-M6)
- DECISIONS.md before changing the stack, snapshot format, size metric, or retrying a rejected approach
- worklogs/disksnap.md Handover block: create it when M0 starts (milestone work always gets a worklog)

## Commands
These become valid once M0 creates the solution; correct them here if they drift.
- Build: `dotnet build DiskSnap.sln -c Release`
- Test: `dotnet test DiskSnap.sln` (use an elevated terminal: some tests need admin or symlink privilege)
- Run GUI: `dotnet run --project src/DiskSnap.App` (GUI not yet implemented; elevation remains future work, see 2026-11-24-02)
- Run headless scan: `DiskSnap.exe --scan C: D:`

## Boundaries
- The scanner is strictly read-only. Never modify, move, or delete anything on a scanned drive.
- Tests use temp directories only. Never touch the real `%LOCALAPPDATA%\DiskSnap\` store from tests.
- The tool never executes cleanup commands. Cleanup guidance in the UI is display text only.
- Ask first: NuGet dependencies beyond the test framework and CommunityToolkit.Mvvm, a snapshot schema version bump, scanning a real drive (slow, needs admin), any code that deletes files outside the snapshot store.
- Do not edit: generated files under `bin/` and `obj/`.

## Workspace docs
- Rules for these files: WORKSPACE.md
