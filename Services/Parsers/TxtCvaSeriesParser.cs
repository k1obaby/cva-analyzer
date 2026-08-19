using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using CvaAnalyzer.Models;

namespace CvaAnalyzer.Services.Parsers;

public class TxtCvaSeriesParser
{
    // Строка заголовка таблицы: столбцы время, потенциал, ток.
    private static readonly Regex HeaderPattern = new(
        @"Время,\s*с\.?,[\s\t]+Потенциал,\s*В\.?,[\s\t]+Ток,\s*А\.?",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public (string sampleName, List<CyclicVoltammetryData> cycles) Parse(string filePath)
    {
        var lines = File.ReadAllLines(filePath, Encoding.GetEncoding(1251));
        var cycles = new List<CyclicVoltammetryData>();

        bool inDataSection = false;
        string? candidateName = null;
        string sampleName = string.Empty;

        CyclicVoltammetryData? currentCycle = null;

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed)) continue;

            if (trimmed.StartsWith("Цикл", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("Cycle", StringComparison.OrdinalIgnoreCase))
            {
                if (currentCycle != null && currentCycle.Points.Count > 0)
                    cycles.Add(currentCycle);
                currentCycle = new CyclicVoltammetryData();
                inDataSection = false;
                continue;
            }

            if (trimmed.StartsWith("u") ||
                trimmed.StartsWith("Шаг"))
            {
                continue;
            }

            if (HeaderPattern.IsMatch(trimmed))
            {
                inDataSection = true;
                if (!string.IsNullOrEmpty(candidateName) && string.IsNullOrEmpty(sampleName))
                    sampleName = candidateName.Trim();
                if (currentCycle == null)
                {
                    currentCycle = new CyclicVoltammetryData();
                }
                else if (currentCycle.Points.Count > 0)
                {
                    cycles.Add(currentCycle);
                    currentCycle = new CyclicVoltammetryData();
                }
                continue;
            }

            if (inDataSection)
            {
                if (!CvaTxtDataLineParser.TryParse(trimmed, out double time, out double potential, out double current))
                    continue;

                currentCycle ??= new CyclicVoltammetryData();
                currentCycle.Points.Add(new()
                {
                    Time = time,
                    Potential = potential,
                    Current = current
                });
            }
            else
            {
                candidateName = trimmed;
            }
        }

        if (currentCycle != null && currentCycle.Points.Count > 0)
            cycles.Add(currentCycle);

        if (string.IsNullOrEmpty(sampleName))
            sampleName = Path.GetFileNameWithoutExtension(filePath);

        foreach (var cycle in cycles)
            cycle.SampleName = sampleName;

        return (sampleName, cycles);
    }
}
