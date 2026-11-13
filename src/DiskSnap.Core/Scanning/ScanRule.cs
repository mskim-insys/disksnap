using System.Text.Json.Serialization;

namespace DiskSnap.Core.Scanning;

public sealed class ScanRule
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("pattern")]
    public required string Pattern { get; init; }

    [JsonPropertyName("action")]
    public required string Action { get; init; }

    [JsonPropertyName("measure")]
    public bool Measure { get; init; }

    [JsonPropertyName("title")]
    public required string Title { get; init; }

    [JsonPropertyName("guidance")]
    public required string Guidance { get; init; }
}
