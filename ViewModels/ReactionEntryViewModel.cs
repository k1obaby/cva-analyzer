using CommunityToolkit.Mvvm.ComponentModel;
using System.Globalization;

namespace CvaAnalyzer.ViewModels;

public partial class ReactionEntryViewModel : ObservableObject
{
    public string Reaction { get; }
    public double E0 { get; }
    public int N { get; }
    public double KHPlus { get; }
    public double KOHMinus { get; }

    [ObservableProperty]
    private double? adjustedPotential;

    [ObservableProperty]
    private string adjustedMode = string.Empty;

    public string AdjustedPotentialDisplay =>
        AdjustedPotential.HasValue
            ? AdjustedPotential.Value.ToString("F3", CultureInfo.InvariantCulture)
            : string.Empty;

    public ReactionEntryViewModel(string reaction, double e0, int n, double kHPlus, double kOHMinus)
    {
        Reaction = reaction;
        E0 = e0;
        N = n;
        KHPlus = kHPlus;
        KOHMinus = kOHMinus;
    }

    public void UpdatePotential(double? pH)
    {
        if (!pH.HasValue)
        {
            AdjustedPotential = null;
            AdjustedMode = string.Empty;
            OnPropertyChanged(nameof(AdjustedPotentialDisplay));
            return;
        }

        const double factor = 0.05916;
        if (Math.Abs(KHPlus) > 0)
        {
            AdjustedPotential = E0 - (factor * KHPlus / Math.Max(1, N)) * pH.Value;
            AdjustedMode = "H+";
        }
        else if (Math.Abs(KOHMinus) > 0)
        {
            double pOH = 14.0 - pH.Value;
            AdjustedPotential = E0 - (factor * KOHMinus / Math.Max(1, N)) * pOH;
            AdjustedMode = "OH-";
        }
        else
        {
            AdjustedPotential = E0;
            AdjustedMode = "E0";
        }

        OnPropertyChanged(nameof(AdjustedPotentialDisplay));
    }
}
