# DiskSnap
Personal Windows tool that records disk-usage snapshots and compares two of them to show which folder or file grew.

## Usage
Launch `DiskSnap.exe` to open the GUI. If not already elevated, it prompts to restart as administrator; if declined, it continues with a warning. Select one or more ready fixed drives and choose **Scan**; the list includes external USB disks reported by Windows as Fixed. You can monitor progress or cancel, and delete snapshots from the drive list with confirmation.

With at least two snapshots, the newest pair is selected automatically. Choose snapshots with their **A** and **B** buttons, or use **Compare latest two** for a drive; **Clear** clears the selection. Comparisons require the same drive and volume serial. The **Compare** tab shows a tree with growth filtering and path actions. **Top growers** ranks changes and can show shrinkers; double-click a row to find it in the tree. **Special folders** shows exclusions, cloud placeholders, rule matches and display-only guidance. **Settings** changes apply to future scans; retention runs after a successful snapshot write.

Snapshots are stored under `%LOCALAPPDATA%\DiskSnap\snapshots\<drive letter>`; `settings.json` and editable `rules.json` live under `%LOCALAPPDATA%\DiskSnap`. Defaults are created when needed. The settings keys are `maxSnapshots` (default 30), `dirThreshold` (default 268435456 bytes) and `fileThreshold` (default 67108864 bytes). Thresholds in JSON are bytes; size inputs in Settings default to MiB without a unit and accept `B`, `KB`, `MB`, `GB`, `TB`, and `KiB` through `TiB` (for example `50MB` or `1GiB`).

Retention skips unreadable, invalid or foreign-drive snapshots with a warning. At the start of a write, stale `.<32 hex>.tmp` files older than 24 hours are cleaned from that drive's snapshot folder. A same-second filename collision fails rather than overwriting a snapshot.

### Headless scans
Run `DiskSnap.exe --scan C: D: [--quiet]` to scan without a window. Replace the drives with the desired volume roots; omit the brackets around `--quiet`. Quiet mode suppresses progress and the final summary, not errors or warnings. Headless mode does not request elevation; it warns if unelevated. Ctrl+C cancels. The exit code is 0 only if every drive succeeds. In Command Prompt, wait for the windowless executable before reading the exit code:

```cmd
start /wait DiskSnap.exe --scan X:
echo %ERRORLEVEL%
```

For scheduled scans, create a Windows Task Scheduler task pointing to `DiskSnap.exe`, pass `--scan C: D:` as arguments, and select **Run with highest privileges**.

## Build and publish
See the build commands in AGENTS.md. To publish for Windows x64, run `dotnet publish src/DiskSnap.App -c Release -r win-x64 -o artifacts/publish`; the executable is `artifacts/publish/DiskSnap.exe`.

## Current behavior and limits
Sizes are allocated bytes. A folder below the directory threshold is recorded by name and total, not its children; small files inside expanded folders are aggregated. A file missing from one snapshot may have been folded, excluded or skipped, so it can be described as **newly observed** rather than definitely new; any folding-based lower bound is conditional. Cloud placeholder directories are not enumerated, even within measured exclusions, so their local contents may be omitted. Report export and headless `--compare` are not available.
