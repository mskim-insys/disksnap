using System.Text.Json.Serialization;

namespace DiskSnap.Core.Storage;

public sealed class DiskSnapSettings
{
    [JsonPropertyName("maxSnapshots")]
    public int MaxSnapshots { get; init; } = 30;

    [JsonPropertyName("dirThreshold")]
    public long DirectoryThreshold { get; init; } = 256L * 1024 * 1024;

    [JsonPropertyName("fileThreshold")]
    public long FileThreshold { get; init; } = 64L * 1024 * 1024;

    internal void Validate()
    {
        if (MaxSnapshots < 1 || DirectoryThreshold < 0 || FileThreshold < 0)
        {
            throw new InvalidDataException("Settings require maxSnapshots >= 1 and non-negative thresholds.");
        }
    }
}
