using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace CvaAnalyzer.Services.Parsers;

internal static class CvaTxtDataLineParser
{
    public static bool TryParse(string trimmed, out double time, out double potential, out double current)
    {
        time = potential = current = 0;
        var parts = SplitColumns(trimmed);
        if (parts.Length < 3)
            return false;

        return double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out time)
            && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out potential)
            && double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out current);
    }

    private static string[] SplitColumns(string trimmed)
    {
        var parts = Regex.Split(trimmed, @"\s{2,}|\t")
            .Where(s => !string.IsNullOrEmpty(s))
            .ToArray();
        if (parts.Length >= 3)
            return parts;

        return Regex.Split(trimmed, @"\s+")
            .Where(s => !string.IsNullOrEmpty(s))
            .ToArray();
    }
}
