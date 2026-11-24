# Worklog: DiskSnap
Spec: specs/disksnap.md
Status: in progress

## Handover
- Current milestone: M3; exit criteria remain unchecked in the [spec](../specs/disksnap.md#acceptance-criteria).
- Done and verified: M2 closed on user-reported tests and a headless scan; M3 Core comparison and in-memory fixtures compile in Release.
- Not done / partial: M3 tests await the user; no M3 acceptance criteria were checked.
- First step next session: Review the user's elevated Release test results, including the large in-memory comparison, and resolve failures before closing M3.
- Read first: `README.md`, `specs/disksnap.md`, `DECISIONS.md`, this Handover.
- Traps: Do not scan a real drive or access `%LOCALAPPDATA%\DiskSnap` in tests. File thresholds are conditional folding evidence, not guaranteed bounds; exclusions can be ambiguous (2026-11-24-05). GUI elevation remains future work (2026-11-24-02).
- Open questions: Cloud placeholder directory handling remains undecided (2026-11-23-01); the symlink privilege-missing skip path remains untested on an unprivileged machine.

## Sessions (newest first)
### 2026-11-24 · M3
- Changed: Updated R17 and recorded 2026-11-24-05; added the Core comparison tree, ranked growers/shrinkers, rule guidance and in-memory fixtures, including a 200,000-node pair.
- Verified: `dotnet build DiskSnap.sln -c Release` succeeded with 0 warnings and 0 errors; tests were not run at the user's request. No real-drive scans were performed.
- Risks / open: [AC10](../specs/disksnap.md#acceptance-criteria) awaits the user's test run; threshold crossings affect aggregate deltas and unmeasured exclusions have ambiguous sizes.

### 2026-11-24 · M2 verification
- Changed: Checked AC9 and AC11 based on the user's verification.
- Verified: The user ran `dotnet test DiskSnap.sln -c Release` elevated: 45 total, 44 passed, 1 skipped (opt-in benchmark), 0 failed. The user successfully ran `start /wait DiskSnap.exe --scan X:` and checked its exit code. The resulting snapshot was 6 KB compressed and 27 KB raw; X: is a real 100 GB partition with about 10 GB used (a 5 GB page file and about 5 GB of copied temp files). It contains few files, so the snapshot is not representative of C:.
- Risks / open: Cloud placeholder directory handling remains open (2026-11-23-01); C: scan time and representative snapshot size remain unmeasured.

### 2026-11-24 · M2 follow-up
- Changed: Made retention skip unsafe candidates with warnings, added stale-temp cleanup, wired Ctrl+C, added headless summaries and drive-root normalization, updated temp-store tests and usage docs; recorded 2026-11-24-04.
- Verified: `dotnet build DiskSnap.sln -c Release` succeeded; tests were not run at the user's request. No real-drive scans were performed.
- Risks / open: [M2 criteria](../specs/disksnap.md#acceptance-criteria) await user verification; compressed size and full-drive timing remain unmeasured.

### 2026-11-24 · M2
- Changed: Added the injectable snapshot store, atomic gzip writes, UTC-ordered per-drive retention, settings and rules loading, and a Core scan runner with a windowless executable entry point. Added temp-only regression fixtures.
- Verified: `dotnet build DiskSnap.sln -c Release` succeeded; tests were not run at the user's request. No real-drive scans were performed.
- Risks / open: [AC9 and AC11](../specs/disksnap.md#acceptance-criteria) await user verification; compressed size and full-drive timing remain unmeasured.

### 2026-11-24 · M1 follow-up
- Changed: Made every root enumeration exception fatal, made cancellation deterministic, corrected the milestone range, and recorded 2026-11-24-01. Committed separately as `828e4a9`.
- Verified: `dotnet build DiskSnap.sln -c Release` succeeded; tests await the user.
- Risks / open: No real-drive scan was performed.

### 2026-11-23 · M1 follow-up
- Changed: Added the opt-in `DISKSNAP_BENCH=1` 100,000-file benchmark (`f0269d5`); folded small-file data into directory totals, compiled and prefiltered rules, enforced volume-root scans, adjusted enumeration error handling, and added temp-only regression fixtures.
- Verified: Release build succeeded. The user reported 12/12 tests in 33.5 s and 12/12 in 27.0 s before, then 24/24 in 30.8 s after. The opt-in benchmark passed once per version: scan elapsed 0.3330565 s before and 0.1770075 s after; process peak working set 131,854,336 and 72,118,272 bytes; total benchmark runs 25.1 s and 30.0 s including setup and cleanup.
- Risks / open: Single benchmark runs do not establish a stable speed ratio; peak working set includes fixture setup. Cloud placeholder directory handling remains open (2026-11-23-01).

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
