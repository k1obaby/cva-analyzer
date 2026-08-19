namespace CvaAnalyzer.Models;

public class BackgroundMetadata
{
    public int Id { get; set; } 
    public double ScanRate { get; set; } // мВ/с
    public string Electrolyte { get; set; } = string.Empty;
    public string WorkingElectrode { get; set; } = string.Empty;
    public string ReferenceElectrode { get; set; } = string.Empty;
    public string Atmosphere { get; set; } = string.Empty;
    public string CellType { get; set; } = string.Empty;
    public string DepositionMethod { get; set; } = string.Empty;
    public string Illumination { get; set; } = string.Empty;
}