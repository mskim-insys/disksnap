namespace DiskSnap.Core.Scanning;

public static class CloudPlaceholderClassifier
{
    private const uint FileAttributeOffline = 0x00001000;
    private const uint FileAttributeRecallOnOpen = 0x00040000;
    private const uint FileAttributeRecallOnDataAccess = 0x00400000;
    private const uint IoReparseTagSymlink = 0xA000000C;
    private const uint IoReparseTagMountPoint = 0xA0000003;

    public static bool IsCloudPlaceholder(uint attributes) =>
        (attributes & (FileAttributeOffline | FileAttributeRecallOnDataAccess)) != 0;

    public static bool IsCloudPlaceholderDirectory(uint attributes) =>
        (attributes & (FileAttributeOffline | FileAttributeRecallOnOpen | FileAttributeRecallOnDataAccess)) != 0;

    public static CloudFileClassification Classify(uint attributes, long allocatedSize)
    {
        if (IsCloudPlaceholder(attributes))
        {
            return new CloudFileClassification(0, ["cloud"]);
        }

        return new CloudFileClassification(allocatedSize, Array.Empty<string>());
    }

    public static bool IsLinkTag(uint reparseTag) =>
        reparseTag is IoReparseTagSymlink or IoReparseTagMountPoint;
}

public sealed record CloudFileClassification(long CountedSize, IReadOnlyList<string> Flags);
