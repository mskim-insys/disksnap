# DiskSnap
Personal Windows tool that records disk-usage snapshots and compares two of them to show which folder or file grew.

## Usage
The Windows-only Core library now provides directory scanning and produces schema-v1 snapshots. The GUI and headless scan command are not wired yet; target behavior is in [specs/disksnap.md](specs/disksnap.md).

## Build and run
See Commands in AGENTS.md.

## Current behavior and limits
The scanner uses bulk allocated-size directory queries, handles reparse points, cloud placeholders and hard links, supports thresholds and rules, and records skipped paths. It has only been exercised against temporary fixtures; no real-drive scan has been performed.
