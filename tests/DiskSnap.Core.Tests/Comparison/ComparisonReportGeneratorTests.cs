using System.Globalization;
using System.Text;
using DiskSnap.Core.Comparison;
using DiskSnap.Core.Reporting;
using DiskSnap.Core.Scanning;
using DiskSnap.Core.Snapshots;

namespace DiskSnap.Core.Tests.Comparison;

public sealed class ComparisonReportGeneratorTests
{
    [Fact]
    public void Ac10StyleComparisonMatchesGoldenReportAndIsRepeatable()
    {
        var comparison = CreateComparison();

        var first = ComparisonReportGenerator.Generate(comparison);
        var second = ComparisonReportGenerator.Generate(comparison);
        var golden = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "ComparisonReport.golden.md"))
            .Replace("\r\n", "\n", StringComparison.Ordinal);

        Assert.Equal(golden, first);
        Assert.Equal(first, second);
        Assert.DoesNotContain("\r", first, StringComparison.Ordinal);
        Assert.Equal(Encoding.UTF8.GetByteCount(first), Encoding.UTF8.GetByteCount(second));
        Assert.DoesNotContain("host", first, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret", first, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("skipped-sample", first, StringComparison.Ordinal);
    }

    [Fact]
    public void ReportHasStableSectionOrderOrdinalTieOrderAndHonorsRows()
    {
        var comparison = CreateComparison();
        var report = ComparisonReportGenerator.Generate(comparison, new ReportOptions { TopGrowerRows = 1 });

        Assert.True(report.IndexOf("## Comparison", StringComparison.Ordinal) < report.IndexOf("## Summary", StringComparison.Ordinal));
        Assert.True(report.IndexOf("## Summary", StringComparison.Ordinal) < report.IndexOf("## Top growers", StringComparison.Ordinal));
        Assert.True(report.IndexOf("## Top growers", StringComparison.Ordinal) < report.IndexOf("### Ancestors:", StringComparison.Ordinal));
        Assert.True(report.IndexOf("### Ancestors:", StringComparison.Ordinal) < report.IndexOf("## Caveats", StringComparison.Ordinal));
        Assert.Contains("4 top-grower rows omitted by the configured row limit", report, StringComparison.Ordinal);
        Assert.Contains("| 1 | unknown\\.bin", report, StringComparison.Ordinal);
        Assert.DoesNotContain("| 2 |", report, StringComparison.Ordinal);
    }

    [Fact]
    public void OutputAndTruncationNotesStayWithinUtf8ByteLimitAndKeepRequiredSections()
    {
        var comparison = CreateComparison();
        const int cap = 1_800;

        var report = ComparisonReportGenerator.Generate(comparison, new ReportOptions { MaxUtf8Bytes = cap });

        Assert.True(Encoding.UTF8.GetByteCount(report) <= cap);
        Assert.Contains("## Comparison", report, StringComparison.Ordinal);
        Assert.Contains("## Summary", report, StringComparison.Ordinal);
        Assert.Contains("## Caveats", report, StringComparison.Ordinal);
        Assert.Contains("## Truncation", report, StringComparison.Ordinal);
        Assert.Contains("omitted to fit", report, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void OutputIsInvariantUnderNonEnglishCulture()
    {
        var comparison = CreateComparison();
        var originalCulture = CultureInfo.CurrentCulture;
        var originalUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            var invariantReport = ComparisonReportGenerator.Generate(comparison);
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("fr-FR");

            Assert.Equal(invariantReport, ComparisonReportGenerator.Generate(comparison));
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
        }
    }

    private static SnapshotComparison CreateComparison()
    {
        const long m = 1024 * 1024;
        var oldHeader = Header("2026-11-25T01:02:03+09:00", 1_000 * m, 300 * m, 20 * m);
        var newHeader = Header("2026-11-26T01:02:03+09:00", 1_200 * m, 350 * m, 10 * m);
        var cacheRule = new ScanRule { Id = "cache", Pattern = "cache", Action = "exclude", Title = "Cache", Guidance = "Not measured." };
        var hintRule = new ScanRule { Id = "hint", Pattern = "alpha", Action = "track", Title = "Alpha", Guidance = "Tracked folder." };
        var older = new Snapshot
        {
            Header = oldHeader,
            Nodes =
            [
                Node("", "d", 500 * m),
                Node("alpha", "c", 100 * m),
                Node("b|eta", "c", 100 * m),
                Node("gone", "c", 10 * m),
                Node("cache", "x", 0, ruleId: "cache"),
                Node("excluded", "x", 0),
                Node("*", "g", 10 * m)
            ]
        };
        var newer = new Snapshot
        {
            Header = newHeader,
            Nodes =
            [
                Node("", "d", 700 * m),
                Node("alpha", "c", 170 * m, ruleId: "hint"),
                Node("b|eta", "c", 160 * m),
                Node("fresh", "c", 60 * m, flags: ["cloud", "hardlink"]),
                Node("unknown.bin", "f", 170 * m),
                Node("cache", "x", 0, ruleId: "cache"),
                Node("excluded", "x", 0),
                Node("*", "g", 70 * m)
            ]
        };
        return SnapshotComparer.Compare(older, newer, [cacheRule, hintRule]);
    }

    private static SnapshotHeader Header(string utc, long total, long free, long fileThreshold) => new()
    {
        Version = 1,
        Tool = "DiskSnap test",
        Drive = "X:",
        VolumeSerial = "ABCD-1234",
        FileSystem = "NTFS",
        Utc = utc,
        Host = "test-host-must-not-appear",
        Elevated = true,
        VolumeTotal = total,
        VolumeFree = free,
        Metric = "allocated",
        DirectoryThreshold = 4 * 1024 * 1024,
        FileThreshold = fileThreshold,
        ScanSeconds = 1,
        Skipped = new SkippedSummary { Count = 2, Sample = ["skipped-sample-secret-path"] }
    };

    private static SnapshotNode Node(string path, string kind, long size, string? ruleId = null, long? count = null, IReadOnlyList<string>? flags = null) => new()
    {
        Path = path,
        Kind = kind,
        Size = size,
        RuleId = ruleId,
        FileCount = count,
        Flags = flags
    };
}
