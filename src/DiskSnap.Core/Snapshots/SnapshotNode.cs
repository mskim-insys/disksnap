using System.Text.Json.Serialization;

namespace DiskSnap.Core.Snapshots;

public sealed class SnapshotNode
{
    [JsonPropertyName("p")]
    public required string Path { get; init; }

    [JsonPropertyName("k")]
    public required string Kind { get; init; }

    [JsonPropertyName("s")]
    public required long Size { get; init; }

    [JsonPropertyName("n")]
    public long? FileCount { get; init; }

    [JsonPropertyName("r")]
    public string? RuleId { get; init; }

    [JsonPropertyName("fl")]
    public IReadOnlyList<string>? Flags { get; init; }
}
