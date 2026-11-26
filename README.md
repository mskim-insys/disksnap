# DiskSnap
Personal Windows tool that records disk-usage snapshots and compares two of them to show which folder or file grew.

## Usage
Run `dotnet run --project src/DiskSnap.App` or launch `DiskSnap.exe` without arguments to open the GUI. A non-elevated GUI launch prompts to restart with administrator privileges; if declined, it continues with a visible warning. The left pane lists ready fixed drives, including external USB hard disks Windows reports as Fixed. Select one or more drives to scan sequentially, monitor progress, cancel, browse snapshots, choose A/B, or request deletion with confirmation. With at least two snapshots, the GUI selects the newest pair by default. The selection strip shows A and B and can clear them; selected rows are highlighted and badged. Picking a snapshot on a different drive clears the opposite slot, and each drive's Compare latest two button replaces the selection with its newest pair. Pairs with different volume serials are refused. The Compare tab shows an expandable, virtualized tree with growth filtering and path actions. Top growers ranks the current comparison, with shrinker, root and minimum-delta controls; double-click a row to show it in the tree. Special folders shows recorded exclusions, collapsed folders, cloud placeholders and rule matches, with selectable display-only guidance and unaccounted space. Settings changes apply to future scans; retention runs after the next successful snapshot write.

`DiskSnap.exe --scan C: D: [--quiet]` scans each listed drive without opening a window. It returns zero only if every drive succeeds. `--quiet` suppresses progress and the final summary, not errors or warnings. Ctrl+C cancels a headless scan, which remains asInvoker and never prompts for elevation.

DiskSnap.exe is a windowless program; in cmd, use `start /wait DiskSnap.exe --scan X:` and then `echo %ERRORLEVEL%` to see its exit code. Task Scheduler is unaffected; run scheduled scans with highest privileges.

Snapshots are stored under `%LOCALAPPDATA%\DiskSnap\snapshots\<drive letter>`; `settings.json` and editable `rules.json` are in `%LOCALAPPDATA%\DiskSnap`. Defaults are created on first scan. Settings are `maxSnapshots` (default 30), `dirThreshold` (default 268435456 bytes), and `fileThreshold` (default 67108864 bytes). Size inputs without a unit are MiB; `B`, `KB`, `MB`, `GB`, `TB`, and binary `KiB` through `TiB` are accepted (for example `50MB` or `1GiB`). Retention runs only after a successful write; unreadable, invalid, or foreign-drive candidates are left in place with a warning. A same-second filename collision fails rather than overwriting a snapshot. At the start of a write, stale `.<32 hex>.tmp` files older than 24 hours are cleaned from that drive's snapshot folder.

## Build and run
See Commands in AGENTS.md.

## Current behavior and limits
The scanner uses bulk allocated-size directory queries, handles reparse points, cloud placeholders and hard links, supports thresholds and rules, and records skipped paths. Public scans require a volume root (`C:` is normalized to `C:\`) because rule paths are drive-relative. Cloud placeholder directories are not enumerated, including during measured exclusions; their local contents may be omitted.

The Core library and GUI can compare snapshots from the same drive and volume serial, including threshold-aware unknowns and ranked growers. A folder below `dirThreshold` is recorded by name and total only; its files do not appear in the snapshot. Exact new/removed presence is distinguished from newly observed files; any folding-based lower bound is conditional, since exclusions or skips can also hide files. Comparison is not exposed in the headless command line yet.
