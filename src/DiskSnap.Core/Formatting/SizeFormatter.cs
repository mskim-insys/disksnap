using System.Globalization;

namespace DiskSnap.Core.Formatting;

public static class SizeFormatter
{
    private static readonly string[] Units = ["B", "KiB", "MiB", "GiB", "TiB", "PiB", "EiB"];

    public static string Format(long? bytes, IFormatProvider? formatProvider = null)
    {
        if (!bytes.HasValue)
        {
            return "-";
        }

        formatProvider ??= CultureInfo.CurrentCulture;
        decimal value = bytes.Value;
        var unit = 0;
        while (Math.Abs(value) >= 1024 && unit < Units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return $"{value.ToString(unit == 0 ? "N0" : "0.##", formatProvider)} {Units[unit]}";
    }

    public static string FormatExactBytes(long? bytes, IFormatProvider? formatProvider = null) =>
        bytes.HasValue ? $"{bytes.Value.ToString("N0", formatProvider ?? CultureInfo.CurrentCulture)} bytes" : "-";
}
