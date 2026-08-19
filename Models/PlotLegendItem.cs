using System.Windows.Media;

namespace CvaAnalyzer.Models;

public class PlotLegendItem
{
    public string Title { get; set; } = string.Empty;
    public Brush Color { get; set; } = Brushes.Black;
}
