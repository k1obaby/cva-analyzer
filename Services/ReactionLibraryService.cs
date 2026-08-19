using CvaAnalyzer.Models;
using System.Globalization;
using System.IO;
using System.Linq;

namespace CvaAnalyzer.Services;

public class ReactionLibraryService
{
    public List<ReactionEntry> LoadFromFile(string filePath)
    {
        var lines = File.ReadAllLines(filePath);
        if (lines.Length == 0) return new List<ReactionEntry>();

        var header = SplitLine(lines[0]);
        var columnMap = BuildColumnMap(header);

        var reactions = new List<ReactionEntry>();
        for (int i = 1; i < lines.Length; i++)
        {
            var parts = SplitLine(lines[i]);
            if (parts.Length == 0) continue;

            string reaction = GetString(parts, columnMap.ReactionIndex);
            if (string.IsNullOrWhiteSpace(reaction)) continue;

            double e0 = GetDouble(parts, columnMap.E0Index);
            int n = (int)Math.Round(GetDouble(parts, columnMap.NIndex));
            double kH = GetDouble(parts, columnMap.KHIndex);
            double kOH = GetDouble(parts, columnMap.KOHIndex);

            reactions.Add(new ReactionEntry
            {
                Reaction = reaction,
                E0 = e0,
                N = n,
                KHPlus = kH,
                KOHMinus = kOH
            });
        }

        return reactions;
    }

    private static string[] SplitLine(string line)
    {
        if (line.Contains('\t'))
            return line.Split('\t');
        if (line.Contains(';'))
            return line.Split(';');
        return line.Split(',');
    }

    private static string GetString(string[] parts, int index)
    {
        if (index < 0 || index >= parts.Length) return string.Empty;
        return parts[index].Trim();
    }

    private static double GetDouble(string[] parts, int index)
    {
        if (index < 0 || index >= parts.Length) return 0;
        var text = parts[index].Trim();
        if (string.IsNullOrEmpty(text)) return 0;

        text = text.Replace(',', '.');
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            return value;

        return 0;
    }

    private static ColumnMap BuildColumnMap(string[] header)
    {
        int reactionIndex = 0;
        int e0Index = -1;
        int nIndex = -1;
        int kHIndex = -1;
        int kOHIndex = -1;

        for (int i = 0; i < header.Length; i++)
        {
            string normalized = Normalize(header[i]);
            if (string.IsNullOrEmpty(normalized) && i == 0)
            {
                reactionIndex = 0;
                continue;
            }

            if (normalized.Contains("реак") || normalized.Contains("reaction"))
                reactionIndex = i;
            else if (normalized.Contains("e0") || normalized.Contains("e0ph0"))
                e0Index = i;
            else if (normalized == "n")
                nIndex = i;
            else if (normalized.Contains("kh") || normalized.Contains("k,h"))
                kHIndex = i;
            else if (normalized.Contains("koh") || normalized.Contains("k,oh"))
                kOHIndex = i;
        }

        return new ColumnMap(reactionIndex, e0Index, nIndex, kHIndex, kOHIndex);
    }

    private static string Normalize(string value)
    {
        return new string(value
            .ToLowerInvariant()
            .Where(char.IsLetterOrDigit)
            .ToArray());
    }

    private readonly record struct ColumnMap(int ReactionIndex, int E0Index, int NIndex, int KHIndex, int KOHIndex);
}
