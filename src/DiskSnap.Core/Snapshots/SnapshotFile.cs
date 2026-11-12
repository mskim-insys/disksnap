using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DiskSnap.Core.Snapshots;

public static class SnapshotFile
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly UTF8Encoding Utf8WithoutBom = new(encoderShouldEmitUTF8Identifier: false);

    public static async Task WriteAsync(
        string path,
        Snapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(snapshot);

        await using var file = new FileStream(
            path,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 64 * 1024,
            FileOptions.Asynchronous);
        await using var gzip = new GZipStream(file, CompressionLevel.Optimal, leaveOpen: true);
        await using var writer = new StreamWriter(gzip, Utf8WithoutBom, leaveOpen: true);

        await writer.WriteLineAsync(JsonSerializer.Serialize(snapshot.Header, JsonOptions).AsMemory(), cancellationToken);
        foreach (var node in snapshot.Nodes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await writer.WriteLineAsync(JsonSerializer.Serialize(node, JsonOptions).AsMemory(), cancellationToken);
        }
    }

    public static async Task<Snapshot> ReadAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        await using var file = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var gzip = new GZipStream(file, CompressionMode.Decompress, leaveOpen: true);
        using var reader = new StreamReader(gzip, Utf8WithoutBom, detectEncodingFromByteOrderMarks: false);

        var headerLine = await reader.ReadLineAsync(cancellationToken)
            ?? throw new InvalidDataException("The snapshot does not contain a header.");
        var header = JsonSerializer.Deserialize<SnapshotHeader>(headerLine, JsonOptions)
            ?? throw new InvalidDataException("The snapshot header is empty.");
        if (header.Type != "hdr")
        {
            throw new InvalidDataException("The first snapshot record is not a header.");
        }

        var nodes = new List<SnapshotNode>();
        string? line;
        while ((line = await reader.ReadLineAsync(cancellationToken)) is not null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var node = JsonSerializer.Deserialize<SnapshotNode>(line, JsonOptions)
                ?? throw new InvalidDataException("A snapshot node is empty.");
            nodes.Add(node);
        }

        return new Snapshot
        {
            Header = header,
            Nodes = nodes
        };
    }
}
