using System.Text.Json.Serialization;

namespace DiskSnap.Core.Snapshots;

public sealed class SnapshotHeader
{
    [JsonPropertyName("t")]
    [JsonRequired]
    public string Type { get; init; } = "hdr";

    [JsonPropertyName("v")]
    public required int Version { get; init; }

    [JsonPropertyName("tool")]
    public required string Tool { get; init; }

    [JsonPropertyName("drive")]
    public required string Drive { get; init; }

    [JsonPropertyName("volSerial")]
    public required string VolumeSerial { get; init; }

    [JsonPropertyName("fs")]
    public required string FileSystem { get; init; }

    [JsonPropertyName("utc")]
    public required string Utc { get; init; }

    [JsonPropertyName("host")]
    public required string Host { get; init; }

    [JsonPropertyName("elevated")]
    public required bool Elevated { get; init; }

    [JsonPropertyName("volTotal")]
    public required long VolumeTotal { get; init; }

    [JsonPropertyName("volFree")]
    public required long VolumeFree { get; init; }

    [JsonPropertyName("metric")]
    public required string Metric { get; init; }

    [JsonPropertyName("dirThreshold")]
    public required long DirectoryThreshold { get; init; }

    [JsonPropertyName("fileThreshold")]
    public required long FileThreshold { get; init; }

    [JsonPropertyName("scanSeconds")]
    public required double ScanSeconds { get; init; }

    [JsonPropertyName("skipped")]
    public required SkippedSummary Skipped { get; init; }
}
