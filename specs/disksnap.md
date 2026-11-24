# Spec: DiskSnap
Status: active
Last updated: 2026-11-24

## Goal
A personal Windows tool that records disk-usage snapshots of one or more drives and compares two snapshots, so that when free space suddenly drops the user can see which folder or file grew. Comparison is the main feature. The scan must be cheap enough to run routinely, and snapshots small enough to keep many.

## Requirements

### Scanning
- R1 Drive selection: list local fixed drives. The user selects one or more. Each drive produces its own snapshot file.
- R2 Admin: the GUI manifest requires administrator. The headless mode warns and continues if not elevated.
- R3 Access errors: any directory or file that cannot be read (access denied, sharing violation, path too long, vanished mid-scan) is skipped. The scan never aborts on one. The snapshot header stores the skipped count and up to 200 sample paths.
- R4 Links: never descend into a directory symlink, junction, or mount point. Record it as a link entry with size 0, except that R5 takes precedence for a directory with cloud attributes. Files that are symlinks count 0. Other reparse tags (for example data deduplication) are not links: count their allocated size normally.
- R5 Cloud placeholders: files with the offline or recall-on-data-access attributes occupy no local space. Count them as 0 bytes and flag them. Never enumerate directories with OFFLINE (0x1000), RECALL_ON_OPEN (0x40000), or RECALL_ON_DATA_ACCESS (0x400000), including when measuring an exclusion. Record each visible such directory as a collapsed entry with size 0, file count 0, and the `cloud` flag.
- R6 Hard links: best-effort de-duplication. For files at or above 1 MiB (configurable), check the link count and count each (volume, file ID) once.
- R7 Size metric: allocated size on disk (see DECISIONS.md 2026-11-12-03). Totals are recursive subtree totals in bytes.
- R8 Thresholds: the tree is walked except for links, cloud placeholder directories and unmeasured exclusions (DECISIONS.md 2026-11-12-04). What is recorded:
  - A directory whose subtree total is at or above `dirThreshold` (default 256 MiB) is expanded: its children are recorded.
  - A directory below the threshold is collapsed: record name, total size, and file count only.
  - Inside an expanded directory, files at or above `fileThreshold` (default 64 MiB) get their own entry. All smaller files are folded into one aggregate entry with count and total bytes.
  - Both thresholds are stored in the header and configurable in settings.
- R9 Rules (exclusions and hints): a JSON rules file (defaults embedded, user-editable copy in the settings folder). Each rule has `id`, `pattern` (path relative to the drive root, wildcards allowed, case-insensitive), `action` (`exclude` or `track`), `measure` (bool, only meaningful for `exclude`), `title`, and `guidance` (display text).
  - `exclude`: do not descend. Record the entry as excluded with the rule id. If `measure` is true, still walk it to compute its size and record that size, except for cloud placeholder directories under R5. A placeholder matching the rule itself is recorded under R5 with the rule id; an enclosing exclusion still hides its descendants.
  - `track`: scan normally. The rule only attaches guidance to the node.
  - The UI shows guidance next to any matching node in the compare views and in a separate "Special folders" panel listing every rule that matched on the selected snapshot, with its size when known.
  - Default rules are in the table below.
- R10 Unaccounted space: the header stores the volume's total and free bytes. The UI shows `used - recorded root total` as "unaccounted" (skipped folders, shadow copies, filesystem metadata, unmeasured exclusions).

### Snapshot storage
- R11 Location: `%LOCALAPPDATA%\DiskSnap\snapshots\<drive letter>\<yyyyMMdd-HHmmss>.jsonl.gz` (local time in the name, UTC inside the header). Settings in `%LOCALAPPDATA%\DiskSnap\settings.json`.
- R12 Write path: stream through GZipStream into a temp file in the same folder, then rename on success. A cancelled or failed scan leaves no snapshot and does not delete existing snapshots. At the start of a store write, delete only files named `.<32 hex>.tmp` older than 24 hours in that drive's snapshot folder; warn if cleanup fails.
- R13 Retention: keep the newest `maxSnapshots` valid snapshots per drive (default 30, configurable). After a successful new snapshot, delete the oldest beyond the limit. Unreadable, foreign-drive, or invalid-header files are skipped, never deleted, and reported as warnings without failing the write. The user can also delete snapshots manually in the UI.
- R14 Format: JSON Lines, UTF-8. Line 1 is the header. All later lines are nodes in depth-first order.
  - Header: `{"t":"hdr","v":1,"tool":"DiskSnap x.y.z","drive":"C:","volSerial":"...","fs":"NTFS","utc":"...","host":"...","elevated":true,"volTotal":0,"volFree":0,"metric":"allocated","dirThreshold":0,"fileThreshold":0,"scanSeconds":0,"skipped":{"count":0,"sample":[]}}`
  - Node: `{"p":"Users\\me\\AppData","k":"d","s":123456,"n":4567}`
    - `p`: path relative to the drive root, `""` for the root.
    - `k`: `d` expanded directory, `c` collapsed directory, `f` file, `g` aggregate of small files in the parent (its `p` is the parent path plus `\\*`), `l` link, `x` excluded.
    - `s`: bytes. `n`: recursive file count (omit for `f`).
    - Optional: `r` rule id (for `x`, or a matched `track` rule), `fl` flags array (`cloud`, `hardlink`, `sys`).
  - The agent may refine field names in M0. The final schema must be written back into this section and `v` bumped on any later change.

### Comparison
- R15 Selection: compare any two snapshots of the same volume serial (refuse otherwise). Default is newest against the previous one.
- R16 Matching: match nodes by normalized path (case-insensitive). Delta = newer minus older.
- R17 Presence rules:
  - In both: delta is exact for recorded sizes. Ancestor deltas use recorded subtree totals; skipped and unmeasured content remains outside those totals.
  - Only in one snapshot: a directory or link whose parent was expanded in the other is exactly new or removed, with size 0 on the missing side. Descendants of an exactly absent directory are likewise absent. A file with an expanded parent in the other snapshot has unknown presence and size: it may have been folded into the small-files aggregate below that snapshot's `fileThreshold` (unless that threshold is 0). Use each snapshot's own threshold. Otherwise the missing size and presence are unknown; the nearest comparable ancestor carries the recorded delta.
  - Files with unknown presence are described as newly observed when present only in the newer snapshot, or no longer observed when present only in the older snapshot, never as new or removed. Their delta remains unknown.
  - The file threshold is a conditional bound when an ordinary file was folded, not an unconditional bound: exclusions and skipped paths can also hide entries, and excluded files can be folded regardless of their measured size. Expose recorded size minus the missing side's threshold as a conditional lower bound on growth (or shrinkage magnitude) only when that threshold is available; any "at least" wording must state the folding assumption.
  - A missing small-files aggregate under an expanded parent has recorded size 0. An aggregate is not an individual new or removed file. Aggregate deltas can include files crossing the threshold and must not be interpreted as exact changes to the same set of files.
  - An excluded node with no recorded file count has unknown measured size; its stored zero may represent an unmeasured exclusion. An exclusion may hide descendants. Do not infer a size for an excluded node solely from current rules, which may have changed since scanning.
- R18 Top growers: a flat list ranked by delta. A node is listed when its delta is at least `minDelta` (default 50 MiB) and no single child with a qualifying delta accounts for 80% or more of its delta (otherwise the child is listed instead of the parent). A newly observed file with unknown delta may instead be a candidate when its recorded size is at least `minDelta` and at least 80% of its parent's positive delta; its delta remains unknown and its conditional lower bound is shown with the folding assumption when available. Rank observed-file candidates by recorded size. Omit the root by default, with an option to include it; it remains in the tree. A toggle shows shrinkers instead. Each row shows path, older size, newer size, delta, percent change, and any matching rule guidance.
- R21 Report export: produce a Markdown report from a comparison, meant to be pasted to an LLM or read as text.
  - Contents, in this order: (1) header: drive, volume serial, both snapshot timestamps, thresholds, volume used/free for each, "unaccounted" for each; (2) summary: total delta, count of new, removed and unknown-presence nodes; (3) top growers table, default 30 rows (configurable): rank, path, older, newer, delta, delta %, flags; (4) for each of the top 10 growers: its ancestor chain with deltas and any matching rule guidance; (5) caveats: skipped-folder counts, excluded rules that were not measured, snapshot-scope notes.
  - Sizes are shown as human-readable text with exact bytes alongside. Paths are drive-relative with backslashes.
  - Output is deterministic (stable ordering, no timestamps of the export itself) and capped at a configurable length (default about 20 KB); when the cap truncates, the report says what was omitted.
  - Reports contain paths only, never file contents. Paths may include user names, so the UI warns before copying.
- R22 Delivery: the GUI has an "Export report" action (save as .md, or copy to clipboard). Headless: `DiskSnap.exe --compare C: [--a <file> --b <file>] --out report.md`. With no `--a`/`--b`, it compares the newest snapshot with the previous one.

### Interfaces
- R19 GUI (WPF), plain and usable:
  - Left pane: drive checkboxes with a Scan button, scan progress (current path, items scanned, skipped count) with Cancel, and the snapshot list per drive (date, size, delete, choose as A or B).
  - Main pane tabs: Compare (tree), Top growers (flat), Special folders.
  - Compare tree columns: Name, Older, Newer, Delta, Delta %, a delta bar. Siblings sorted by absolute delta descending. A "growth only" filter. Context menu: open in Explorer, copy path. A "Jump to biggest grower" action expands the path to the largest culprit.
  - The tree must stay responsive with hundreds of thousands of nodes: virtualized, nodes built lazily.
- R20 Headless: `DiskSnap.exe --scan C: D: [--quiet]` runs without a window, writes snapshots, applies retention, and exits with 0 on success and non-zero if any drive failed. Ctrl+C cancels the scan. Unless quiet, print a final count of saved and failed drives. Intended for Task Scheduler with highest privileges.

### Default rules
Guidance text below is a draft for display only. Verify each command against current Microsoft documentation before shipping and never execute anything automatically.

| id | pattern | action | measure | guidance |
|---|---|---|---|---|
| winsxs | `Windows\WinSxS` | exclude | false | Hard-link heavy, so totals are misleading. Do not delete files by hand. Use `Dism.exe /Online /Cleanup-Image /StartComponentCleanup` from an elevated prompt. |
| sysvolinfo | `System Volume Information` | exclude | false | Restore points and shadow copies. Check with `vssadmin list shadowstorage`; limit via System Protection settings. Usually unreadable even as admin; shows up as unaccounted space. |
| winupdate | `Windows\SoftwareDistribution\Download` | track | - | Windows Update download cache. Settings > Storage > Temporary files, or Disk Cleanup > Windows Update Cleanup. |
| windows-old | `Windows.old` | track | - | Previous Windows installation. Remove through Disk Cleanup > Previous Windows installation(s). |
| hiberfil | `hiberfil.sys` | track | - | Hibernation file. `powercfg /h off` disables hibernation and removes it (also disables Fast Startup). |
| pagefile | `pagefile.sys`, `swapfile.sys` | track | - | Virtual memory. Adjust in System Properties > Advanced > Performance > Virtual memory. |
| recycle | `$Recycle.Bin` | track | - | Empty the Recycle Bin. |
| temp | `Windows\Temp`, `Users\*\AppData\Local\Temp` | track | - | Temporary files. Settings > Storage > Temporary files. |
| vhdx | `*.vhdx` | track | - | WSL or Docker virtual disk. Grows but does not shrink by itself. For WSL, `wsl --shutdown` then `wsl --manage <distro> --set-sparse true` (verify availability), or compact the disk image. |

## Constraints
- Windows 10/11 x64. .NET per DECISIONS.md 2026-11-12-01. Single-file, self-contained publish for win-x64.
- Layout: `DiskSnap.sln`, `src/DiskSnap.Core` (scanner, snapshot IO, store, diff, rules; no UI references), `src/DiskSnap.App` (WPF, MVVM), `tests/DiskSnap.Core.Tests`.
- Dependencies: BCL only, plus the test framework and optionally CommunityToolkit.Mvvm. No network access.
- The scanner is read-only on scanned drives.
- Scanning runs on background threads with a bounded degree of parallelism (configurable; default suited to SSD, with a single-thread option for HDDs). Cancellation must be responsive.
- Paths longer than 260 characters must work (use extended-length path prefixes).
- Targets (measure and record in the worklog, not hard gates): a typical snapshot at default thresholds stays under about 10 MB compressed; scan time for the user's C: is recorded after M1.

## Acceptance criteria
- [x] AC1 Solution builds and `dotnet test` passes from an elevated terminal.
- [x] AC2 Snapshot write/read round trip preserves every field. Header and node schema match R14.
- [x] AC3 Scanner fixtures: a junction (made with `mklink /J`) and a directory symlink (test skipped with a clear message if the privilege is missing) are recorded as links and their targets are not double counted.
- [x] AC4 A directory with a deny ACE is skipped, counted in the header, and the scan completes.
- [x] AC5 Threshold pruning: fixtures show expanded versus collapsed directories and the small-files aggregate exactly as R8 describes.
- [x] AC6 Rules: an `exclude` rule prevents descent and is recorded with its rule id; `measure` true records a size, false does not; a `track` rule only attaches guidance.
- [x] AC7 Two hard links to one large file are counted once.
- [x] AC8 Cloud-placeholder attribute classification is unit tested (count 0, flagged).
- [x] AC9 Retention and write path: with `maxSnapshots` = N, after N+1 scans exactly N files remain and the oldest is gone; a cancelled or failed scan leaves no partial file and deletes nothing.
- [x] AC10 Diff fixtures cover: new, removed, unknown-presence, rolled-up ancestor delta, and the 80% culprit attribution rule.
- [x] AC11 `DiskSnap.exe --scan` creates snapshots for the listed drives with no window and returns correct exit codes.
- [ ] AC12 GUI: multi-drive select and scan with progress and Cancel; snapshot list with delete; Compare tree, Top growers, and Special folders panel work as in R19.
- [ ] AC13 Real-machine check (user-assisted): scan C:, create a new folder with a dummy 2 GB file, scan again; Top growers lists that folder or file first.
- [ ] AC14 Published single-file win-x64 build runs; README.md describes current usage.
- [ ] AC15 Golden-file test: the AC10 diff fixtures produce a report with the sections in R21 order, rows sorted by delta, the row limit honored, truncation noted, and byte-identical output on a second run.
- [ ] AC16 `--compare` writes the report, returns 0 on success and non-zero when fewer than two snapshots exist or the volume serials differ; GUI export saves/copies the same text.

## Milestones
### M0 Scaffold and snapshot IO
- Exit criteria: AC1, AC2
### M1 Scanner core
- Exit criteria: AC3, AC4, AC5, AC6, AC7, AC8
### M2 Store, retention, headless mode
- Exit criteria: AC9, AC11
### M3 Diff engine
- Exit criteria: AC10
### M4 WPF UI
- Exit criteria: AC12, AC13
### M5 Publish
- Exit criteria: AC14
### M6 LLM report export
- Exit criteria: AC15, AC16 (depends on M3; the GUI action needs M4)
