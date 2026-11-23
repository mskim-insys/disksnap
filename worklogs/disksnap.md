# Worklog: DiskSnap
Spec: specs/disksnap.md
Status: in progress

## Handover
- Current milestone: M1 follow-up; M2 has not started. Exit criteria remain in the [spec](../specs/disksnap.md#acceptance-criteria).
- Done and verified: The user reported `dotnet test DiskSnap.sln -c Release` passing all 24 tests after the follow-up; details are in the session entry below.
- First step next session: Review the M1 follow-up and user-provided benchmark results below; do not start M2 without a new request.
- Read first: `README.md`, `specs/disksnap.md`, `DECISIONS.md`, this Handover.
- Traps: Do not scan a real drive or access `%LOCALAPPDATA%\DiskSnap`; tests and benchmark use temp directories only. Temp-subdirectory scans use an internal test-only overload. Benchmark results are single runs; peak working set covers the test process, including fixture setup.
- Open questions: Decide how to handle cloud placeholder directories before the first real-drive scan (DECISIONS.md 2026-11-23-01); the symlink privilege-missing skip path has not been exercised on an unprivileged machine.

## Sessions (newest first)
### 2026-11-23 · M1 follow-up
- Changed: Recorded the user's benchmark output; no scanner changes in this session.
- Verified: The user reported the opt-in benchmark passed once per version. For 100,000 temp files, scan elapsed time was 0.3330565 s before and 0.1770075 s after; process peak working set was 131,854,336 bytes before and 72,118,272 bytes after. Total benchmark test-run durations were 25.1 s and 30.0 s, respectively, including fixture creation and cleanup.
- Risks / open: Single runs do not establish a stable speed ratio; peak working set is process-wide and may include setup rather than only scanning. Cloud placeholder directory enumeration still needs the user's decision before a real-drive scan.

### 2026-11-23 · M1 follow-up
- Changed: Recorded the user's test and timing results; no scanner changes in this session.
- Verified: The user reported 12/12 tests passing in 33.5 s before and 12/12 in 27.0 s for another run, then 24/24 passing in 30.8 s for the full suite after (`dotnet test DiskSnap.sln -c Release`). These are test-run durations, not scan-only elapsed time or peak working set; the 12-test and 24-test runs do not have the same test count.
- Risks / open: Cloud placeholder directory enumeration still needs the user's decision before a real-drive scan.

### 2026-11-23 · M1 follow-up
- Changed: Added the opt-in `DISKSNAP_BENCH=1` 100,000-file benchmark in its own first commit (`f0269d5`); folded small-file data into directory totals; compiled and prefiltered rules per scan; enforced volume-root scans; adjusted unsupported-enumeration handling; added temp-only sparse, compression, long-path, cancellation, and regression fixtures.
- Verified: `dotnet build DiskSnap.sln -c Release` succeeded. Tests and benchmark were not run per request; the user will run the elevated suite and record benchmark baseline/after measurements. Temp fixtures use the internal test-only subdirectory overload.
- Risks / open: Cloud placeholder directory enumeration needs the user's decision before a real-drive scan; no real-drive scan was performed.

### 2026-11-23 · M1
- Changed: Corrected the junction fixture command and cleanup; made the symlink privilege skip discoverable by xUnit.
- Verified: Elevated `dotnet test DiskSnap.sln -c Release --no-restore --logger 'console;verbosity=normal'` passed all 11 tests, including the junction, symlink, deny ACE, threshold, rules, hard-link, and cloud cases. Visual Studio solution build succeeded.
- Risks / open: The privilege-missing skip path was not exercised; no real-drive scan was performed.

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
