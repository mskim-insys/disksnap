# DiskSnap
Personal Windows tool that records disk-usage snapshots and compares two of them to show which folder or file grew.

## Usage
`DiskSnap.exe --scan C: D: [--quiet]` scans each listed drive without opening a window. It returns zero only if every drive succeeds. `--quiet` suppresses progress and the final summary, not errors or warnings. Ctrl+C cancels a headless scan. The GUI is not yet implemented.

DiskSnap.exe is a windowless program; in cmd, use `start /wait DiskSnap.exe --scan X:` and then `echo %ERRORLEVEL%` to see its exit code. Task Scheduler is unaffected; run scheduled scans with highest privileges.

Snapshots are stored under `%LOCALAPPDATA%\DiskSnap\snapshots\<drive letter>`; `settings.json` and editable `rules.json` are in `%LOCALAPPDATA%\DiskSnap`. Defaults are created on first headless use. Settings are `maxSnapshots` (default 30), `dirThreshold` (default 268435456 bytes), and `fileThreshold` (default 67108864 bytes). Retention runs only after a successful write; unreadable, invalid, or foreign-drive candidates are left in place with a warning. A same-second filename collision fails rather than overwriting a snapshot. At the start of a write, stale `.<32 hex>.tmp` files older than 24 hours are cleaned from that drive's snapshot folder.

## Build and run
See Commands in AGENTS.md.

## Current behavior and limits
The scanner uses bulk allocated-size directory queries, handles reparse points, cloud placeholders and hard links, supports thresholds and rules, and records skipped paths. Public scans require a volume root (`C:` is normalized to `C:\`) because rule paths are drive-relative. Cloud placeholder directories are not enumerated, including during measured exclusions; their local contents may be omitted.

The Core library can compare in-memory snapshots from the same volume, including threshold-aware unknowns and ranked growers or shrinkers. Growers omit the root by default and can include newly observed files with unknown deltas; any folding-based lower bound is conditional, since exclusions or skips can also hide files. Comparison is not yet exposed in the GUI or command line.
