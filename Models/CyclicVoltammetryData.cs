using System.Collections.ObjectModel;

namespace CvaAnalyzer.Models;

public class CyclicVoltammetryData
{
    public string SampleName { get; set; } = string.Empty;
    public ObservableCollection<VoltammetryPoint> Points { get; } = new();
}
public record VoltammetryPoint
{
    public double Time { get; init; }        // время, с
    public double Potential { get; init; }   // потенциал, В
    public double Current { get; init; }     // ток, А
    public double Charge => Current * Time;  // вычитаемый заряд, Кл
}