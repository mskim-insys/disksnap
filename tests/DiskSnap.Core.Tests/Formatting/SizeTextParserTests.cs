using DiskSnap.Core.Formatting;

namespace DiskSnap.Core.Tests.Formatting;

public sealed class SizeTextParserTests
{
    [Theory]
    [InlineData("50", 52428800L)]
    [InlineData("50MB", 50000000L)]
    [InlineData("1GiB", 1073741824L)]
    [InlineData(" 1.5 MiB ", 1572864L)]
    [InlineData("0B", 0L)]
    [InlineData("9223372036854775807B", long.MaxValue)]
    public void ParsesSizes(string text, long expected)
    {
        Assert.True(SizeTextParser.TryParse(text, out var actual));
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("")]
    [InlineData("-1")]
    [InlineData("1XB")]
    [InlineData("0.1B")]
    [InlineData("9223372036854775808B")]
    [InlineData("9999999999999999999999999999TiB")]
    public void RejectsInvalidSizes(string text) => Assert.False(SizeTextParser.TryParse(text, out _));
}
