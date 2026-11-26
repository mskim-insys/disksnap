# DiskSnap
Personal Windows tool that records disk-usage snapshots and compares two of them to show which folder or file grew.

## Usage
Launch `DiskSnap.exe` to open the GUI. If not already elevated, it prompts to restart as administrator; if declined, it continues with a warning. Select one or more ready fixed drives and choose **Scan**. You can monitor progress or cancel, and delete snapshots from the drive list with confirmation.

Choose snapshots with their **A** and **B** buttons, or use **Compare latest two** for a drive; **Clear** clears the selection. Comparisons require the same drive and volume serial. The **Compare** tab shows a tree with growth filtering and path actions. **Top growers** ranks changes and can show shrinkers; double-click a row to find it in the tree. **Special folders** shows exclusions, cloud placeholders, rule matches and display-only guidance. **Settings** changes apply to future scans; retention runs after a successful snapshot write.

Snapshots are stored under `%LOCALAPPDATA%\DiskSnap\snapshots\<drive letter>`; `settings.json` and editable `rules.json` live under `%LOCALAPPDATA%\DiskSnap`. Defaults are created when needed. The default retention is 30 snapshots per drive, and the default directory and file thresholds are 256 MiB and 64 MiB. A same-second filename collision fails rather than overwriting a snapshot.

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
