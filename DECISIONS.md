# Decisions

## 2026-11-12-01 Stack: C# on .NET with a WPF UI
- Context: Windows-only personal tool. It needs low-level Win32 file enumeration (P/Invoke), an admin manifest, and a tree-style data grid. It will be built and maintained mostly by AI agents.
- Decision: C# on current .NET LTS (net10.0-windows; if the installed SDK is older, use net8.0-windows and log it here). WPF for the UI. All scan, snapshot, and diff logic lives in a UI-free class library (`DiskSnap.Core`) so the engine or the UI can be swapped later.
- Alternatives considered and rejected:
  - Rust (egui or Tauri): fastest scan and good NTFS crates, but the UI and iteration loop are slower to build. Revisit only as a scan engine behind the same Core interface (see 2026-11-12-05).
  - Python: scan speed and packaging are weak for multi-million-file volumes.
  - Electron or Tauri web UI: file access would need a native sidecar anyway.
  - WinUI 3: packaging friction, and its tree-grid controls are less mature than WPF's ecosystem.
  - Avalonia or MAUI: cross-platform is not needed now. Avalonia is the migration path if that changes, because Core has no UI references.
- Impact / follow-ups: Core must not reference WPF. Elevated debugging is required (admin manifest).

## 2026-11-12-02 Snapshot format: JSON Lines compressed with gzip
- Context: snapshots are write-once, read-whole, and compared by loading two into memory. The user suggested 7z or any practical compressed format.
- Decision: one JSON object per line (header line, then node lines), compressed with built-in GZipStream, extension `.jsonl.gz`. The schema is in specs/disksnap.md.
- Alternatives considered and rejected:
  - 7z: needs a bundled 7z binary or a native wrapper, which is a dependency for little gain on text data.
  - SQLite: random access is not needed.
  - Zstandard or Brotli: slightly better ratio, but common tools cannot open the files. Revisit if typical snapshots exceed about 50 MB.
  - Custom binary: unreadable for debugging.
- Impact / follow-ups: `v` field in the header versions the schema. Measure real snapshot sizes in M0/M2 and record them in the worklog.

## 2026-11-12-03 Size metric: allocated size on disk
- Context: logical file size misreports sparse files, compressed files, VHDX images, and cloud placeholders, which are exactly the files that surprise a user.
- Decision: record allocated size. Enumerate with bulk directory queries (GetFileInformationByHandleEx with FileIdBothDirectoryInfo or FileIdExtdDirectoryInfo), which return allocation size without a per-file call.
- Alternatives considered and rejected: logical size only (misleading, see above); GetCompressedFileSize per file (one extra syscall per file, slow).
- Impact / follow-ups: M1 verified the bulk query on a temporary directory with 1,000 4 KiB files: it returned the 4,096,000-byte allocation total in 0.5 ms (the query also returned `.` and `..`). The bulk query is feasible, so no fallback was needed. Full-drive timing remains unmeasured pending separate user approval.

## 2026-11-12-04 Scan walks everything; thresholds only prune what is recorded
- Context: deciding whether a folder is "large enough to expand" needs its subtree total.
- Decision: every directory is enumerated once. Thresholds decide what goes into the snapshot, not what is walked.
- Alternatives considered and rejected: stop descending below a threshold (the total would be unknown without walking).
- Impact / follow-ups: scan time does not depend on thresholds. Thresholds only control snapshot size.

## 2026-11-12-05 Deferred: direct MFT reading for faster scans
- Context: tools like WizTree read the NTFS master file table directly. It is faster but needs raw volume access, works on NTFS only, and is much more code.
- Decision: not in v1. Use normal directory enumeration.
- Impact / follow-ups: revisit if a full scan of the user's C: takes longer than the user tolerates. Record the measured scan time in the worklog first.

## 2026-11-12-06 Deferred: scheduled scans from inside the app
- Decision: v1 only provides the headless `--scan` command. The user registers a Task Scheduler task manually (run with highest privileges).
- Impact / follow-ups: a UI for registering the task can be added later.

## 2026-11-23-01 Open: enumerate cloud placeholder directories
- Context: directory entries carrying `RECALL_ON_DATA_ACCESS` may represent cloud placeholders; opening them for enumeration may fetch placeholder contents over the network.
- Decision: open. Keep current behavior unchanged until the user decides whether and how to enumerate these directories.
- Alternatives considered: enumerate normally and risk network hydration; skip such directories and report them as skipped, which may omit local data.
- Impact / follow-ups: decide before scanning a drive that may contain cloud placeholder directories. No real-drive scan is authorized by this entry.

## 2026-11-23-02 Error handling: unsupported enumeration errors depend on scan depth
- Context: Win32 errors 1, 50, and 87 from bulk directory enumeration may indicate that the API is unsupported on the volume, but can also arise on an individual inner directory.
- Decision: on the scan root, fail with a `NotSupportedException` naming the drive and filesystem; on an inner directory, skip and count it like access denied. Error 995 remains non-skippable.
- Alternatives considered: treating these errors as fatal everywhere would abort scans for isolated directories; skipping them at the root would produce a misleading partial snapshot when the enumeration API is unavailable for the volume.
- Impact / follow-ups: user-facing scan failures identify the volume and filesystem; skipped inner paths appear in the existing summary.

## 2026-11-24-01 Error handling: all root enumeration failures are fatal
- Context: 2026-11-23-02 handled unsupported APIs at the root, but skipping other root errors could produce a successful empty snapshot for an unavailable drive.
- Decision: any exception enumerating the scan root fails the scan. Unsupported API errors retain their drive and filesystem diagnostic; skippable inner-directory errors still count as skipped.
- Alternatives considered and rejected: skipping root access or I/O errors would misrepresent a failed drive scan as success.
- Impact / follow-ups: no snapshot or retention should follow a failed root enumeration.

## 2026-11-24-02 Entry point: one windowless-capable WPF executable
- Context: scheduled scans must run without a window and must warn rather than require elevation, while the future GUI needs elevation.
- Decision: use a custom entry point in the WPF WinExe, named `DiskSnap.exe`, that dispatches `--scan` to Core without starting WPF. The executable runs as the invoking user for headless mode; GUI elevation remains to be implemented with the GUI.
- Alternatives considered and rejected: a separate console executable would not provide the requested single executable; an executable-wide require-administrator manifest would prompt or fail instead of allowing an unelevated scheduled scan.
- Impact / follow-ups: M4 must implement the GUI elevation requirement without forcing elevation on headless scans.

## 2026-11-24-03 Snapshot naming collisions and retention ordering
- Context: local filenames have only second precision and can collide, including during a daylight-saving transition; local filename order can differ from actual chronological order.
- Decision: never overwrite an existing filename; report the collision as a failed write and do not run retention. Sort retention by the UTC header, with filename as a deterministic tie-breaker, and validate candidate headers before deleting.
- Alternatives considered and rejected: overwriting would lose an existing snapshot; inventing a timestamp or suffix would violate the specified filename or misstate the scan time.
- Impact / follow-ups: a scan completed in the same local second as an existing snapshot must be retried after that second; candidate error handling is refined by 2026-11-24-04.

## 2026-11-24-04 Retention skips unsafe candidates
- Context: 2026-11-24-03 made an invalid candidate block retention after the new snapshot was already saved, making a successful write appear to fail.
- Decision: warn on stderr and skip unreadable, foreign-drive, or invalid-header candidates without deleting them; retain valid candidates using the existing UTC ordering. The saved path and successful exit remain available.
- Alternatives considered and rejected: failing the write after saving misreports success; deleting an unvalidated candidate risks losing unrelated or unreadable data.
- Impact / follow-ups: the folder may contain more than `maxSnapshots` files because skipped candidates do not count toward retention.

## 2026-11-24-05 Comparison of threshold-hidden files
- Context: an individually recorded file can have been folded into the other snapshot's small-files aggregate, even when its parent was expanded; thresholds can differ between scans. Unmeasured exclusions also store zero.
- Decision: use the missing side's header threshold and report such file sizes and presence as unknown rather than inventing zero; preserve exact recorded directory deltas. Treat missing aggregates under expanded parents as zero recorded aggregate size, and unmeasured or ambiguous excluded sizes as unknown. See R17 for the complete rules.
- Alternatives considered and rejected: treating every child missing under an expanded parent as new or removed fabricates file deltas and double-counts threshold crossings. Applying today's rules or thresholds to old snapshots cannot recover historical measurement choices.
- Impact / follow-ups: consumers must support unknown sizes and presence and distinguish aggregate changes from individual file changes. The existing snapshot schema does not record enough information to disambiguate every exclusion.
