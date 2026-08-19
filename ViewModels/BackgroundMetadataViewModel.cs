using CvaAnalyzer.Models;

namespace CvaAnalyzer.ViewModels;

public partial class BackgroundMetadataViewModel : CommunityToolkit.Mvvm.ComponentModel.ObservableObject
{
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    private string sampleName = string.Empty;

    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    private double scanRate;

    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    private string electrolyte = string.Empty;

    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    private string workingElectrode = string.Empty;

    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    private string referenceElectrode = string.Empty;

    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    private string atmosphere = string.Empty;

    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    private string cellType = string.Empty;

    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    private string depositionMethod = string.Empty;

    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    private string illumination = string.Empty;

    public BackgroundMetadata ToModel()
    {
        return new BackgroundMetadata
        {
            ScanRate = ScanRate,
            Electrolyte = Electrolyte,
            WorkingElectrode = WorkingElectrode,
            ReferenceElectrode = ReferenceElectrode,
            Atmosphere = Atmosphere,
            CellType = CellType,
            DepositionMethod = DepositionMethod,
            Illumination = Illumination
        };
    }

    public void SetFromBackground(BackgroundData data)
    {
        if (data == null) return;
        SampleName = data.SampleName ?? string.Empty;
        var m = data.Metadata;
        if (m == null) return;
        ScanRate = m.ScanRate;
        Electrolyte = m.Electrolyte ?? string.Empty;
        WorkingElectrode = m.WorkingElectrode ?? string.Empty;
        ReferenceElectrode = m.ReferenceElectrode ?? string.Empty;
        Atmosphere = m.Atmosphere ?? string.Empty;
        CellType = m.CellType ?? string.Empty;
        DepositionMethod = m.DepositionMethod ?? string.Empty;
        Illumination = m.Illumination ?? string.Empty;
    }
}