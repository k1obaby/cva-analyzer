namespace CvaAnalyzer.Models;

public class ReactionEntry
{
    public string Reaction { get; set; } = string.Empty;
    public double E0 { get; set; }
    public int N { get; set; }
    public double KHPlus { get; set; }
    public double KOHMinus { get; set; }
}
