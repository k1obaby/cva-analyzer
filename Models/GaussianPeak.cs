namespace CvaAnalyzer.Models
{
    public enum PeakShape
    {
        Gaussian,
        Lorentzian,
        PseudoVoigt
    }

    public class GaussianPeak
    {
        public double Amplitude { get; set; } // А; знак: анод >0 / катод <0
        public double Center { get; set; }    // В, положение максимума
        public double Sigma { get; set; }     // В, ширина
        public PeakShape Shape { get; set; } = PeakShape.Gaussian;
        public double Eta { get; set; } = 0.5; // доля Лоренца при PseudoVoigt
        public bool IsUserDefined { get; set; }
        public double FixedCurrent { get; set; }
    }
}
