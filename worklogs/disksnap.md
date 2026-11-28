# Worklog: DiskSnap
Spec: specs/disksnap.md
Status: in progress

## Handover
- Current milestone: M6-A (Core report generator); M0-M5 are complete. M6-B delivery remains for a later scope. [AC15](../specs/disksnap.md#acceptance-criteria) is intentionally unchecked; [AC16](../specs/disksnap.md#acceptance-criteria) is not implemented here.
- Done: implemented the UI-free Core Markdown report generator, Core-only row and UTF-8 byte options, golden fixture and focused report tests, R21 updates, and the report contract decision. The app icon was added by the user via another agent; left unchanged.
- Verified: `dotnet build DiskSnap.sln -c Release` succeeded with 0 warnings/errors. Elevated `dotnet test DiskSnap.sln` passed: 115 passed, 2 skipped, 0 failed. AC15 remains unchecked.
- User-reported AC14 verification (2026-11-28): the user ran the published exe, accepted the admin prompt, and reported normal use working. No other AC14 checks are claimed.
- Publish: `dotnet publish src/DiskSnap.App -c Release -r win-x64 -o artifacts/publish` produces `artifacts/publish/DiskSnap.exe`. The published size is unmeasured; no trimming, ReadyToRun, or signing is configured. Existing `.gitignore` rules already ignore `artifacts/`, `publish/`, `bin/`, and `obj/`.
- First step next session: implement the requested GUI/headless delivery separately under R22 and AC16.
- Read first: `README.md`, `specs/disksnap.md`, `DECISIONS.md`, this Handover.
- Traps:
  - Do not scan a real drive or access `%LOCALAPPDATA%\DiskSnap` in tests.
  - A folder below `dirThreshold` is recorded by name only, so its files never appear.
  - Cloud directory contents may be omitted (2026-11-24-06); file change lower bounds assume folding and are not guaranteed when exclusions or skips apply (2026-11-24-07).
  - Compare selections must share a drive and volume serial; picking another drive replaces the selection.
  - WPF: bindings to get-only properties on Run.Text/TextBox.Text need Mode=OneWay (a missed one broke Special folders once; unit tests cannot catch it).
  - Smart App Control, SmartScreen or WDAC may block freshly built or published unsigned binaries; no runtime behavior beyond the user's stated normal-use report is claimed here.
  - WinExe: from cmd use `start /wait DiskSnap.exe --scan ...` to get the exit code; the GUI relaunches itself elevated with an internal `--gui`.
- M6 notes: report is deterministic, uses invariant formatting and LF, emits UTC header timestamps, keeps paths only, omits skipped sample paths, and caps output at 20,480 UTF-8 bytes by default. Keep the existing GUI wording for observation and conditional folding bounds.
- Not run: publish command, CLI/GUI export flows, and real-drive scans.
- Open questions: none.

## Sessions (newest first)
### 2026-11-28 · M6-A follow-up
- Changed: Preserved path punctuation and backslashes in adaptive inline code spans, preserved guidance commands while flattening newlines, corrected omitted-rule caveats and zero-baseline percentages, extended the fixture and golden report, and refined R21 and decision 2026-11-28-01. The user had another agent add the app icon; it was not changed in this session. AC15 remains unchecked.
- Verified: elevated `dotnet test DiskSnap.sln` passed (115 passed, 2 skipped, 0 failed); `dotnet build DiskSnap.sln -c Release` succeeded (0 warnings, 0 errors). AC15 remains unchecked.
- Not run: publish command, CLI/GUI export paths, or real-drive scan.

### 2026-11-28 · M6-A
- Changed: Added the Core report generator/options and AC10-style golden fixture/tests, set golden Markdown line endings in `.gitattributes`, updated R21 and recorded 2026-11-28-01. Marked AC14 from the user's report only; AC15 remains unchecked as requested.
- Verified: `dotnet build DiskSnap.sln -c Release` succeeded; focused report tests passed 4/4. The user ran the published exe, accepted the admin prompt, and reported normal use working (2026-11-28). No other AC14 checks are claimed.
- Not run: full suite, publish command, CLI/GUI export paths, or real-drive scan.

### 2026-11-26 · M5 follow-up
- Changed: Embedded debug symbols in App and Core, removed publish symbol-copy settings, and restored usage and handover details.
- Verified: Visual Studio workspace build and `dotnet build DiskSnap.sln -c Release --no-restore` succeeded. No publish, executable, tests, or real-drive scans were run; AC14 verification was subsequently reported by the user on 2026-11-28.

### 2026-11-26 · M5
- Changed: Added win-x64 publish settings and product/version metadata, audited startup and data paths, and rewrote standalone usage. No decision entry was needed.
- Single-file source audit: no `Assembly.Location`, `AppContext.BaseDirectory`, or executable-adjacent data files found. Default rules are compiled into `ScanRules`; `SnapshotStore` creates the editable rules and settings under `%LOCALAPPDATA%\DiskSnap` and stores snapshots there. WPF native libraries are configured for self-extraction, not loose deployment beside the exe.
- Startup audit: `--scan` still dispatches from the WinExe and attaches to the parent console as before. GUI elevation still restarts `Environment.ProcessPath` with `--gui`, which dispatches directly to the GUI; these published-exe paths remain untested at runtime.
- Verified: Visual Studio app build and `dotnet build src\DiskSnap.App\DiskSnap.App.csproj -c Release --no-restore` succeeded; evaluated win-x64 publish properties and confirmed ignore rules. An initial Visual Studio build failed on missing generated WPF files with unconditional single-file properties; conditioning those properties on win-x64 restored the normal development build. No publish, executable, tests, or real-drive scans were run.
- Risks / open: [AC14](../specs/disksnap.md#acceptance-criteria) awaits the user's publish and runtime checks; output size and exact contents are not yet measured.

### 2026-11-26 · M4-C
- Changed: Added cross-drive selection replacement, a selection strip, highlighted A/B badges, Clear and per-drive Compare latest two; added UI-free selection tests and updated R15/R19.
- Verified: Visual Studio workspace build succeeded. Tests and GUI were not run at the user's request; no real-drive scans were performed.

### 2026-11-24 · M4-C
- Changed: Added Top growers, Special folders, atomic settings editing, comparison keyboard and sign indicators, ordered scan updates, and temp-only Core tests; refined the small-range notification behavior from 2026-11-24-09.
- Verified: Visual Studio workspace build and `dotnet build DiskSnap.sln -c Release --no-restore` succeeded (0 warnings, 0 errors). Tests and GUI checks were left to the user; no real-drive scans were performed.

### 2026-11-24 · M4-B
- Changed: Added reusable binary size formatting, the iterative flattened comparison model and range-indexed virtualized Compare list, automatic same-drive/serial A/B selection and async compare, display-only guidance, and Explorer/clipboard actions. Replaced the synchronous refresh and Dispatcher.Invoke progress wrapper; recorded 2026-11-24-09 and -10.
- Verified: `dotnet build DiskSnap.sln -c Release` succeeded. Tests and GUI checks were left to the user; no real-drive scan was performed. AC12 remains unchecked.

### 2026-11-24 · M4-A
- Changed: Added throttled scan progress, a shared Core `ScanService` for GUI and headless scanning, ready-fixed-drive catalog, safe snapshot listing/deletion, and the M4-A WPF shell; added temp-only tests and recorded 2026-11-24-08.
- Verified: `dotnet build DiskSnap.sln -c Release` succeeded. Tests and GUI checks were left to the user; no real-drive scan was performed. AC12 remains unchecked.

### 2026-11-24 · M3 follow-up
- Changed: Updated R5, R17 and R18; recorded 2026-11-24-06 and 2026-11-24-07. Added cloud-directory safeguards, qualifying child attribution, observed-file metadata and candidates, root visibility control, and regression fixtures.
- Verified: `dotnet build DiskSnap.sln -c Release` succeeded with 0 warnings and 0 errors. Tests were not run at the user's request; no real-drive scans were performed.

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
