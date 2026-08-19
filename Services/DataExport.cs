using CvaAnalyzer.Models;
using System.IO;
using System.Text;

namespace CvaAnalyzer.Services.Export;

public static class DataExporter
{
    public static void ExportToCsv(CyclicVoltammetryData data, string outputPath)
    {
        using var writer = new StreamWriter(outputPath, false, Encoding.UTF8);
        writer.WriteLine("Время, с\tПотенциал, В\tТок, А\tЗаряд, Кл");

        foreach (var p in data.Points)
        {
            writer.WriteLine($"{p.Time:F9}\t{p.Potential:F12}\t{p.Current:E}\t{p.Charge:E}");
        }
    }

    public static void ExportToTxt(CyclicVoltammetryData data, string outputPath)
    {
        using var writer = new StreamWriter(outputPath, false, Encoding.UTF8);
        writer.WriteLine($"{"Время, с",-20}  {"Потенциал, В",-20}  {"Ток, А",-20}  {"Заряд, Кл",-20}");

        foreach (var p in data.Points)
        {
            writer.WriteLine($"{p.Time,20:F9}  {p.Potential,20:F12}  {p.Current,20:E}  {p.Charge,20:E}");
        }
    }
}