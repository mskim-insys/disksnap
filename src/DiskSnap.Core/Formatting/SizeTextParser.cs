using System.Globalization;
using System.Text.RegularExpressions;

namespace DiskSnap.Core.Formatting;

public static partial class SizeTextParser
{
    public static bool TryParse(string? text, out long bytes)
    {
        bytes = 0;
        var match = SizePattern().Match(text?.Trim() ?? string.Empty);
        if (!match.Success || !decimal.TryParse(match.Groups[1].Value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value))
            return false;

        var unit = match.Groups[2].Value.ToUpperInvariant();
        decimal multiplier = unit switch
        {
            "" => 1024m * 1024,
            "B" => 1,
            "KB" => 1000m,
            "MB" => 1000m * 1000,
            "GB" => 1000m * 1000 * 1000,
            "TB" => 1000m * 1000 * 1000 * 1000,
            "KIB" => 1024m,
            "MIB" => 1024m * 1024,
            "GIB" => 1024m * 1024 * 1024,
            "TIB" => 1024m * 1024 * 1024 * 1024,
            _ => 0
        };
        try
        {
            var result = value * multiplier;
            if (multiplier == 0 || result > long.MaxValue || result != decimal.Truncate(result))
                return false;
            bytes = (long)result;
            return true;
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    [GeneratedRegex(@"^(\d+(?:\.\d+)?)\s*([a-zA-Z]*)$")]
    private static partial Regex SizePattern();
}
