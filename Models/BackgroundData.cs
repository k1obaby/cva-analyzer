namespace CvaAnalyzer.Models;

public class BackgroundData : CyclicVoltammetryData
{
    public BackgroundMetadata Metadata { get; set; } = new();
}