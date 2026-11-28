# DiskSnap comparison report

## Comparison
- Drive: X:
- Volume serial: ABCD-1234
- Older snapshot (UTC): 2026-11-24 16:02:03 UTC
- Newer snapshot (UTC): 2026-11-25 16:02:03 UTC
- Directory threshold (older / newer): 4 MiB (4,194,304 bytes) / 4 MiB (4,194,304 bytes)
- File threshold (older / newer): 20 MiB (20,971,520 bytes) / 10 MiB (10,485,760 bytes)
- Older volume: used 700 MiB (734,003,200 bytes), free 300 MiB (314,572,800 bytes), unaccounted 200 MiB (209,715,200 bytes)
- Newer volume: used 850 MiB (891,289,600 bytes), free 350 MiB (367,001,600 bytes), unaccounted 150 MiB (157,286,400 bytes)

## Summary
- Total delta: +200 MiB (+209,715,200 bytes)
- New nodes: 1
- Removed nodes: 1
- Unknown-presence nodes: 1

## Top growers

| Rank | Path | Older | Newer | Delta | Delta % | Flags |
| ---: | --- | --- | --- | --- | ---: | --- |
| 1 | `unknown.bin` | unknown | 170 MiB (178,257,920 bytes) | at least +150 MiB (+157,286,400 bytes) (assuming ordinary-file folding) | unknown | newly observed |
| 2 | `alpha.dir` | 100 MiB (104,857,600 bytes) | 180 MiB (188,743,680 bytes) | +80 MiB (+83,886,080 bytes) | +80% | rule:path-hint |
| 3 | `alpha` | 100 MiB (104,857,600 bytes) | 170 MiB (178,257,920 bytes) | +70 MiB (+73,400,320 bytes) | +70% | rule:hint |
| 4 | `*` | 10 MiB (10,485,760 bytes) | 70 MiB (73,400,320 bytes) | +60 MiB (+62,914,560 bytes) | +600% | small files |
| 5 | `alpha.dir\-child_name&more` | 10 MiB (10,485,760 bytes) | 70 MiB (73,400,320 bytes) | +60 MiB (+62,914,560 bytes) | +600% | rule:path-hint |
| 6 | `b\|eta` | 100 MiB (104,857,600 bytes) | 160 MiB (167,772,160 bytes) | +60 MiB (+62,914,560 bytes) | +60% |  |
| 7 | `fresh` | 0 B (0 bytes) | 60 MiB (62,914,560 bytes) | +60 MiB (+62,914,560 bytes) | n/a | new, cloud, hardlink |

### Ancestors: `unknown.bin`
- (drive root): +200 MiB (+209,715,200 bytes)
- `unknown.bin`: at least +150 MiB (+157,286,400 bytes) (assuming ordinary-file folding)

### Ancestors: `alpha.dir`
- (drive root): +200 MiB (+209,715,200 bytes)
- `alpha.dir`: +80 MiB (+83,886,080 bytes); guidance: Path hint (path-hint): Run `tool.exe /open C:\folder`. Keep it.

### Ancestors: `alpha`
- (drive root): +200 MiB (+209,715,200 bytes)
- `alpha`: +70 MiB (+73,400,320 bytes); guidance: Alpha (hint): Tracked folder.

### Ancestors: `*`
- (drive root): +200 MiB (+209,715,200 bytes)
- `*`: +60 MiB (+62,914,560 bytes)

### Ancestors: `alpha.dir\-child_name&more`
- (drive root): +200 MiB (+209,715,200 bytes)
- `alpha.dir`: +80 MiB (+83,886,080 bytes); guidance: Path hint (path-hint): Run `tool.exe /open C:\folder`. Keep it.
- `alpha.dir\-child_name&more`: +60 MiB (+62,914,560 bytes); guidance: Path hint (path-hint): Run `tool.exe /open C:\folder`. Keep it.

### Ancestors: `b|eta`
- (drive root): +200 MiB (+209,715,200 bytes)
- `b|eta`: +60 MiB (+62,914,560 bytes)

### Ancestors: `fresh`
- (drive root): +200 MiB (+209,715,200 bytes)
- `fresh`: +60 MiB (+62,914,560 bytes)

## Caveats
- Skipped folders: older 2; newer 2. Sample paths are not included.
- Unmeasured excluded rule ids: cache.
- Sizes are allocated bytes; the small-files aggregate can combine files and threshold crossings.
- Unknown file observations may reflect folding, exclusions, or skipped paths; any lower bound assumes ordinary-file folding.
- Cloud-placeholder directories are not enumerated.
