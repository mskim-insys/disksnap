using System.Diagnostics;
using DiskSnap.Core.Scanning;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace DiskSnap.Core.Tests.Scanning;

public sealed class OptInBenchmarkFactAttribute : FactAttribute
{
    public OptInBenchmarkFactAttribute()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("DISKSNAP_BENCH"), "1", StringComparison.Ordinal))
        {
            Skip = "Set DISKSNAP_BENCH=1 to run this opt-in benchmark.";
        }
    }
}

public sealed class DiskScannerBenchmarkTests(ITestOutputHelper output)
{
    [OptInBenchmarkFact]
    public async Task Scan_AboutOneHundredThousandTinyFiles()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw SkipException.ForSkip("Scanner benchmarks require Windows.");
        }

        using var fixture = new BenchmarkDirectory();
        const int directoryCount = 100;
        const int filesPerDirectory = 1000;
        var payload = new byte[] { 1 };
        for (var directoryIndex = 0; directoryIndex < directoryCount; directoryIndex++)
        {
            var directory = Directory.CreateDirectory(Path.Combine(fixture.Path, $"nested-{directoryIndex:D3}"));
            for (var fileIndex = 0; fileIndex < filesPerDirectory; fileIndex++)
            {
                File.WriteAllBytes(Path.Combine(directory.FullName, $"file-{fileIndex:D4}.bin"), payload);
            }
        }

        var stopwatch = Stopwatch.StartNew();
        var snapshot = await new DiskScanner().ScanAsyncForTests(fixture.Path);
        stopwatch.Stop();
        var peakWorkingSet = Process.GetCurrentProcess().PeakWorkingSet64;

        output.WriteLine($"Scanned {directoryCount * filesPerDirectory:N0} files in {stopwatch.Elapsed}.");
        output.WriteLine($"Peak working set: {peakWorkingSet:N0} bytes.");
        Assert.NotEmpty(snapshot.Nodes);
    }

    private sealed class BenchmarkDirectory : IDisposable
    {
        public BenchmarkDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "DiskSnap.Benchmark." + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
