namespace DiskSnap.Core.Reporting;

public sealed record ReportOptions
{
    public int TopGrowerRows { get; init; } = 30;
    public int MaxUtf8Bytes { get; init; } = 20_480;
}
