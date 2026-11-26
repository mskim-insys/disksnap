# Worklog: DiskSnap
Spec: specs/disksnap.md
Status: in progress

## Handover
- Current milestone: M4-C; milestone criteria are in the [spec](../specs/disksnap.md#milestones).
- Done and verified: M4-B GUI interactions were manually verified by the user. M4-C GUI and Core changes compile; Core tests were added but not run.
- Not done / partial: User test and GUI verification remain pending; milestone criteria are unchecked.
- First step next session: Review the user's test and GUI results.
- Read first: `README.md`, `specs/disksnap.md`, `DECISIONS.md`, this Handover.
- Traps: Do not scan a real drive or access `%LOCALAPPDATA%\DiskSnap` in tests. A folder below `dirThreshold` is recorded by name only, so its files never appear. Cloud directory contents may be omitted (2026-11-24-06); file change lower bounds assume folding and are not guaranteed when exclusions or skips apply (2026-11-24-07). Compare selections must share a drive and volume serial.
- How to try, in order: (1) Open Top growers, toggle Shrinkers and Include root, then double-click a row and confirm it jumps in the tree. (2) Open Special folders on X:; expect System Volume Information or similar plus unaccounted space. (3) Change maxSnapshots and thresholds in Settings, restart, and confirm they persisted. (4) For the AC13-style check on X:, create a >=300 MiB file in a new folder, scan, scan again, and confirm Top growers lists it first; choose thresholds that record the folder and file, and compare the scan before creation with the scan after creation. Run the Core tests as appropriate.
- Open questions: User test and GUI verification are pending.
- M4-C regression: fixed read-only Special folders bindings and included exception types in comparison failures; user verification remains pending.

## Sessions (newest first)
### 2026-11-24 · M4-C
- Changed: Added Top growers, Special folders, atomic settings editing, comparison keyboard and sign indicators, ordered scan updates, and temp-only Core tests; refined the small-range notification behavior from 2026-11-24-09.
- Verified: Visual Studio workspace build and `dotnet build DiskSnap.sln -c Release --no-restore` succeeded (0 warnings, 0 errors). Tests and GUI checks were left to the user; no real-drive scans were performed.
- Risks / open: User verification is pending; see the [milestone criteria](../specs/disksnap.md#milestones).

### 2026-11-24 · M4-B
- Changed: Added reusable binary size formatting, the iterative flattened comparison model and range-indexed virtualized Compare list, automatic same-drive/serial A/B selection and async compare, display-only guidance, and Explorer/clipboard actions. Replaced the synchronous refresh and Dispatcher.Invoke progress wrapper; recorded 2026-11-24-09 and -10.
- Verified: `dotnet build DiskSnap.sln -c Release` succeeded. Tests and GUI checks were left to the user; no real-drive scan was performed. AC12 remains unchecked.
- Risks / open: User-run tests and GUI verification are pending; Top growers and Special folders are M4-C work.

### 2026-11-24 · M4-A
- Changed: Added throttled scan progress, a shared Core `ScanService` for GUI and headless scanning, ready-fixed-drive catalog, safe snapshot listing/deletion, and the M4-A WPF shell; added temp-only tests and recorded 2026-11-24-08.
- Verified: `dotnet build DiskSnap.sln -c Release` succeeded. Tests and GUI checks were left to the user; no real-drive scan was performed. AC12 remains unchecked.
- Risks / open: User verification is pending; M4-B comparison work has not started.

### 2026-11-24 · M3 follow-up
- Changed: Updated R5, R17 and R18; recorded 2026-11-24-06 and 2026-11-24-07. Added cloud-directory safeguards, qualifying child attribution, observed-file metadata and candidates, root visibility control, and regression fixtures.
- Verified: `dotnet build DiskSnap.sln -c Release` succeeded with 0 warnings and 0 errors. Tests were not run at the user's request; no real-drive scans were performed.
- Risks / open: The user will run the follow-up tests. An enclosing measured exclusion hides placeholder descendants; folding-based bounds are conditional.

### 2026-11-24 · M3 verification
- Changed: Checked AC10 after reviewing the diff fixtures.
- Verified: The user ran `dotnet test DiskSnap.sln -c Release` elevated: 57 total, 56 passed, 1 skipped (opt-in benchmark), 0 failed.
- Risks / open: Cloud placeholder directory handling remains undecided (2026-11-23-01).
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
