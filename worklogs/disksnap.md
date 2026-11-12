# Worklog: DiskSnap
Spec: specs/disksnap.md
Status: in progress

## Handover
- Current milestone: M0 done; M1 is next.
- Done and verified: M0 scaffold and snapshot IO are complete; AC1 and AC2 pass.
- Not done / partial: M1 has not started; its exit criteria are listed in the spec.
- First step next session: Start M1 scanner core.
- Read first: `README.md`, `specs/disksnap.md`, `DECISIONS.md`.
- Traps: The agent environment cannot execute the test assembly (WDAC `0x800711C7`); the user runs `dotnet test` for verification.
- Open questions: None.

## Sessions (newest first)
### 2026-11-13 · M6
- Scope change: added R21, R22, AC15, AC16, and M6 for LLM report export.

### 2026-11-13 · M0
- Changed: Fixed the JSONL schema test to read and validate the third node before asserting end-of-file.
- Verified: User ran `dotnet test` on 2026-11-13; passed. `dotnet build DiskSnap.sln -c Release` succeeded with 0 warnings and 0 errors.
- Risks / open: None for M0; continue with M1 as scoped in the [spec](../specs/disksnap.md#milestones).

### 2026-11-12 · M0
- Changed: Created `DiskSnap.sln`, `src/DiskSnap.Core`, `src/DiskSnap.App`, `tests/DiskSnap.Core.Tests`, and `.gitignore`. Added snapshot JSONL/gzip models, reader/writer, and a round-trip/schema test.
- Verified: `dotnet build DiskSnap.sln -c Release` succeeded with 0 warnings and 0 errors. Initial test launch was blocked by Windows Application Control (`0x800711C7`); subsequent user test run executed and found the JSONL assertion bug fixed in the next session.
- Risks / open: The JSONL schema check had an EOF assertion bug; fixed and verified in the 2026-11-13 session above.
