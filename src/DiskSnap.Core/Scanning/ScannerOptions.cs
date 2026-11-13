namespace DiskSnap.Core.Scanning;

public sealed class ScannerOptions
{
    public long DirectoryThreshold { get; init; } = 256L * 1024 * 1024;
    public long FileThreshold { get; init; } = 64L * 1024 * 1024;
    public long HardLinkDeduplicationThreshold { get; init; } = 1024L * 1024;
    public int SkippedPathSampleLimit { get; init; } = 200;
    public IReadOnlyList<ScanRule> Rules { get; init; } = ScanRules.Defaults;

    internal void Validate()
    {
        if (DirectoryThreshold < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(DirectoryThreshold));
        }

        if (FileThreshold < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(FileThreshold));
        }

        if (HardLinkDeduplicationThreshold < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(HardLinkDeduplicationThreshold));
        }

        if (SkippedPathSampleLimit is < 0 or > 200)
        {
            throw new ArgumentOutOfRangeException(nameof(SkippedPathSampleLimit));
        }

        ScanRules.Validate(Rules);
    }
}
