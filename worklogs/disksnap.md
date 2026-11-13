# Worklog: DiskSnap
Spec: specs/disksnap.md
Status: in progress

## Handover
- Current milestone: M1 in progress; exit criteria are AC3-AC8 in the [spec](../specs/disksnap.md#acceptance-criteria).
- Done and verified: Scanner core and AC3-AC8 temp-fixture tests are implemented; Release solution build succeeds; the bulk directory query was verified on a 1,000-file temp fixture.
- Not done / partial: Runtime verification of AC3-AC8 is pending because WDAC blocked loading `DiskSnap.Core.dll`; the criteria remain unchecked until the user runs the test suite.
- First step next session: Run `dotnet test tests\DiskSnap.Core.Tests\DiskSnap.Core.Tests.csproj -c Release`, record the result on the M1 Verified line, and update the AC checkboxes before starting M2.
- Read first: `README.md`, `specs/disksnap.md`, `DECISIONS.md`, this Handover.
- Traps: Do not scan a real drive or access `%LOCALAPPDATA%\DiskSnap`; tests use temp directories only. Test execution is blocked by WDAC `0x800711C7` in this environment.
- Open questions: User test result for AC3-AC8.
- User: test failed due to SAC. proj holding until I can turn it off.

## Sessions (newest first)
### 2026-11-13 · M1
- Changed: Added the Windows bulk-query scanner, link/cloud/hard-link handling, skipped-path reporting, thresholds, rules, and temp-only AC3-AC8 tests.
- Verified: `dotnet build DiskSnap.sln -c Release` succeeded with 0 warnings and 0 errors. A temporary 1,000-file fixture returned the expected 4,096,000 allocated bytes in a 0.5 ms bulk query. `dotnet test tests\DiskSnap.Core.Tests\DiskSnap.Core.Tests.csproj -c Release --no-build` was blocked when Windows Application Control denied loading `DiskSnap.Core.dll` (`0x800711C7`); no tests executed. User-run result pending.
- Risks / open: Runtime AC3-AC8 verification remains pending; no real-drive scan was performed.

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
