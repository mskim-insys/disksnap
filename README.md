# DiskSnap
Personal Windows tool that records disk-usage snapshots and compares two of them to show which folder or file grew.

## Usage
`DiskSnap.exe --scan C: D: [--quiet]` scans each listed drive without opening a window. It returns zero only if every drive succeeds. `--quiet` suppresses progress, not errors or the warning when not elevated. For Task Scheduler, run with highest privileges. The GUI is not yet implemented.

Snapshots are stored under `%LOCALAPPDATA%\DiskSnap\snapshots\<drive letter>`; `settings.json` and editable `rules.json` are in `%LOCALAPPDATA%\DiskSnap`. Defaults are created on first headless use. Settings are `maxSnapshots` (default 30), `dirThreshold` (default 268435456 bytes), and `fileThreshold` (default 67108864 bytes). Retention runs only after a successful write. A same-second filename collision fails rather than overwriting a snapshot.

## Build and run
See Commands in AGENTS.md.

## Current behavior and limits
The scanner uses bulk allocated-size directory queries, handles reparse points, cloud placeholders and hard links, supports thresholds and rules, and records skipped paths. Public scans require a volume root (`C:` is normalized to `C:\`) because rule paths are drive-relative. Real-drive scanning has not been performed; cloud placeholder directory handling remains an open decision in DECISIONS.md.
