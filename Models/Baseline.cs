using System;

namespace CvaAnalyzer.Models
{
    public class Baseline
    {
        public double Intercept { get; set; } // А, свободный член линейной базы I(U)
        public double Slope { get; set; } // А/В, наклон линейной базы I(U)
        public double[] Coefficients { get; set; } = Array.Empty<double>();
    }

}
