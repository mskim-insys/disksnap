using System.Globalization;
using DiskSnap.Core.Formatting;

namespace DiskSnap.Core.Tests.Formatting;

public sealed class SizeFormatterTests
{
    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(1024, "1 KiB")]
    [InlineData(1536, "1.5 KiB")]
    [InlineData(1048576, "1 MiB")]
    [InlineData(1073741824, "1 GiB")]
    public void FormatsBinarySizes(long bytes, string expected)
    {
        Assert.Equal(expected, SizeFormatter.Format(bytes, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void UnknownSizeAndExactBytesAreExplicit()
    {
        Assert.Equal("-", SizeFormatter.Format(null, CultureInfo.InvariantCulture));
        Assert.Equal("1,234,567 bytes", SizeFormatter.FormatExactBytes(1_234_567, CultureInfo.InvariantCulture));
        Assert.Equal("-", SizeFormatter.FormatExactBytes(null, CultureInfo.InvariantCulture));
    }
}
