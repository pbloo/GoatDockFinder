using System.Globalization;

namespace GoatFinder.Core;

public static class ByteSizeFormatter
{
    private static readonly string[] Units = ["KB", "MB", "GB", "TB"];

    public static string Format(long bytes)
    {
        var culture = CultureInfo.GetCultureInfo("pt-BR");
        if (bytes < 1024) return string.Format(culture, "{0} bytes", bytes);

        double value = bytes;
        int unit = -1;
        while (value >= 1024 && unit < Units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return string.Format(culture, "{0:0.#} {1}", value, Units[unit]);
    }
}
