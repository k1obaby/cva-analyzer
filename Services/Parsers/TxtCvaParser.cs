using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using CvaAnalyzer.Models;

namespace CvaAnalyzer.Services.Parsers;

public class TxtCvaParser
{
    // Строка заголовка таблицы: столбцы время, потенциал, ток.
    private static readonly Regex HeaderPattern = new(
        @"Время,\s*с\.?,[\s\t]+Потенциал,\s*В\.?,[\s\t]+Ток,\s*А\.?",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public CyclicVoltammetryData Parse(string filePath)
    {
        var data = new CyclicVoltammetryData();
        // Кодировка файла: UTF-8; при отсутствии распознанных точек — windows-1251.
        var lines = File.ReadAllLines(filePath, Encoding.UTF8);
        var parsed = ParseLines(lines, data);
        if (!parsed && !Equals(Encoding.UTF8, Encoding.GetEncoding(1251)))
        {
            data.Points.Clear();
            data.SampleName = string.Empty;
            lines = File.ReadAllLines(filePath, Encoding.GetEncoding(1251));
            ParseLines(lines, data);
        }
        if (string.IsNullOrEmpty(data.SampleName))
            data.SampleName = Path.GetFileNameWithoutExtension(filePath);
        return data;
    }

    private static bool ParseLines(string[] lines, CyclicVoltammetryData data)
    {
        bool inDataSection = false;
        string? candidateName = null;

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed)) continue;

            // Не данные измерения: префиксы цикла, шага, служебные символы в начале строки.
            if (trimmed.StartsWith("u") && trimmed.Length <= 2 ||
                trimmed.StartsWith("Цикл") || trimmed.StartsWith("Öèêë") ||
                trimmed.StartsWith("Шаг"))
            {
                continue;
            }

            if (HeaderPattern.IsMatch(trimmed))
            {
                inDataSection = true;
                if (!string.IsNullOrEmpty(candidateName))
                    data.SampleName = candidateName.Trim();
                continue;
            }

            if (inDataSection)
            {
                if (!CvaTxtDataLineParser.TryParse(trimmed, out double time, out double potential, out double current))
                    continue;

                data.Points.Add(new VoltammetryPoint
                {
                    Time = time,
                    Potential = potential,
                    Current = current
                });
            }
            else
                candidateName = trimmed;
        }

        return data.Points.Count > 0;
    }
}