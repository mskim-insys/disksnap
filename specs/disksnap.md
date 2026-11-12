# Spec: DiskSnap
Status: active
Last updated: 2026-11-12

## Goal
A personal Windows tool that records disk-usage snapshots of one or more drives and compares two snapshots, so that when free space suddenly drops the user can see which folder or file grew. Comparison is the main feature. The scan must be cheap enough to run routinely, and snapshots small enough to keep many.

## Requirements

### Scanning
- R1 Drive selection: list local fixed drives. The user selects one or more. Each drive produces its own snapshot file.
- R2 Admin: the GUI manifest requires administrator. The headless mode warns and continues if not elevated.
- R3 Access errors: any directory or file that cannot be read (access denied, sharing violation, path too long, vanished mid-scan) is skipped. The scan never aborts on one. The snapshot header stores the skipped count and up to 200 sample paths.
- R4 Links: never descend into a directory symlink, junction, or mount point. Record it as a link entry with size 0. Files that are symlinks count 0. Other reparse tags (for example data deduplication) are not links: count their allocated size normally.
- R5 Cloud placeholders: files with the offline or recall-on-data-access attributes occupy no local space. Count them as 0 bytes and flag them.
- R6 Hard links: best-effort de-duplication. For files at or above 1 MiB (configurable), check the link count and count each (volume, file ID) once.
- R7 Size metric: allocated size on disk (see DECISIONS.md 2026-11-12-03). Totals are recursive subtree totals in bytes.
- R8 Thresholds: the whole tree is walked (DECISIONS.md 2026-11-12-04). What is recorded:
  - A directory whose subtree total is at or above `dirThreshold` (default 256 MiB) is expanded: its children are recorded.
  - A directory below the threshold is collapsed: record name, total size, and file count only.
  - Inside an expanded directory, files at or above `fileThreshold` (default 64 MiB) get their own entry. All smaller files are folded into one aggregate entry with count and total bytes.
  - Both thresholds are stored in the header and configurable in settings.
- R9 Rules (exclusions and hints): a JSON rules file (defaults embedded, user-editable copy in the settings folder). Each rule has `id`, `pattern` (path relative to the drive root, wildcards allowed, case-insensitive), `action` (`exclude` or `track`), `measure` (bool, only meaningful for `exclude`), `title`, and `guidance` (display text).
  - `exclude`: do not descend. Record the entry as excluded with the rule id. If `measure` is true, still walk it to compute its size and record that size.
  - `track`: scan normally. The rule only attaches guidance to the node.
  - The UI shows guidance next to any matching node in the compare views and in a separate "Special folders" panel listing every rule that matched on the selected snapshot, with its size when known.
  - Default rules are in the table below.
- R10 Unaccounted space: the header stores the volume's total and free bytes. The UI shows `used - recorded root total` as "unaccounted" (skipped folders, shadow copies, filesystem metadata, unmeasured exclusions).

### Snapshot storage
- R11 Location: `%LOCALAPPDATA%\DiskSnap\snapshots\<drive letter>\<yyyyMMdd-HHmmss>.jsonl.gz` (local time in the name, UTC inside the header). Settings in `%LOCALAPPDATA%\DiskSnap\settings.json`.
- R12 Write path: stream through GZipStream into a temp file in the same folder, then rename on success. A cancelled or failed scan leaves no snapshot and deletes nothing.
- R13 Retention: keep the newest `maxSnapshots` per drive (default 30, configurable). After a successful new snapshot, delete the oldest beyond the limit. The user can also delete snapshots manually in the UI.
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
  - In both: delta is exact. Ancestor deltas are exact because sizes are subtree totals.
  - Only in the newer snapshot: if the parent was expanded in the older one, the node is `new` with older size 0. Otherwise its older size is unknown and shown as "-"; the nearest ancestor present in both carries the delta.
  - Only in the older snapshot: symmetric (`removed` or unknown).
- R18 Top growers: a flat list ranked by delta. A node is listed when its delta is at least `minDelta` (default 50 MiB) and no single child accounts for 80% or more of its delta (otherwise the child is listed instead of the parent). A toggle shows shrinkers instead. Each row shows path, older size, newer size, delta, percent change, and any matching rule guidance.

### Interfaces
- R19 GUI (WPF), plain and usable:
  - Left pane: drive checkboxes with a Scan button, scan progress (current path, items scanned, skipped count) with Cancel, and the snapshot list per drive (date, size, delete, choose as A or B).
  - Main pane tabs: Compare (tree), Top growers (flat), Special folders.
  - Compare tree columns: Name, Older, Newer, Delta, Delta %, a delta bar. Siblings sorted by absolute delta descending. A "growth only" filter. Context menu: open in Explorer, copy path. A "Jump to biggest grower" action expands the path to the largest culprit.
  - The tree must stay responsive with hundreds of thousands of nodes: virtualized, nodes built lazily.
- R20 Headless: `DiskSnap.exe --scan C: D: [--quiet]` runs without a window, writes snapshots, applies retention, and exits with 0 on success and non-zero if any drive failed. Intended for Task Scheduler with highest privileges.

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
- [ ] AC1 Solution builds and `dotnet test` passes from an elevated terminal.
- [ ] AC2 Snapshot write/read round trip preserves every field. Header and node schema match R14.
- [ ] AC3 Scanner fixtures: a junction (made with `mklink /J`) and a directory symlink (test skipped with a clear message if the privilege is missing) are recorded as links and their targets are not double counted.
- [ ] AC4 A directory with a deny ACE is skipped, counted in the header, and the scan completes.
- [ ] AC5 Threshold pruning: fixtures show expanded versus collapsed directories and the small-files aggregate exactly as R8 describes.
- [ ] AC6 Rules: an `exclude` rule prevents descent and is recorded with its rule id; `measure` true records a size, false does not; a `track` rule only attaches guidance.
- [ ] AC7 Two hard links to one large file are counted once.
- [ ] AC8 Cloud-placeholder attribute classification is unit tested (count 0, flagged).
- [ ] AC9 Retention and write path: with `maxSnapshots` = N, after N+1 scans exactly N files remain and the oldest is gone; a cancelled or failed scan leaves no partial file and deletes nothing.
- [ ] AC10 Diff fixtures cover: new, removed, unknown-presence, rolled-up ancestor delta, and the 80% culprit attribution rule.
- [ ] AC11 `DiskSnap.exe --scan` creates snapshots for the listed drives with no window and returns correct exit codes.
- [ ] AC12 GUI: multi-drive select and scan with progress and Cancel; snapshot list with delete; Compare tree, Top growers, and Special folders panel work as in R19.
- [ ] AC13 Real-machine check (user-assisted): scan C:, create a new folder with a dummy 2 GB file, scan again; Top growers lists that folder or file first.
- [ ] AC14 Published single-file win-x64 build runs; README.md describes current usage.

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
