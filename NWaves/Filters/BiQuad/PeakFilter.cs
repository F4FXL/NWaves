using System;
using NWaves.Filters.Base;

namespace NWaves.Filters.BiQuad
{
    /// <summary>
    /// BiQuad peaking EQ filter.
    /// The coefficients are calculated automatically according to 
    /// audio-eq-cookbook by R.Bristow-Johnson and WebAudio API.
    /// </summary>
    public class PeakFilter : IirFilter
    {
        /// <summary>
        /// Constructor computes the filter coefficients.
        /// </summary>
        /// <param name="freq"></param>
        /// <param name="q"></param>
        /// <param name="gain"></param>
        public PeakFilter(double freq, double q = 1, double gain = 1.0)
        {
            var a = Math.Pow(10, gain / 40);
            var omega = 2 * Math.PI * freq;
            var alpha = Math.Sin(omega) / (2 * q);
            var cosw = Math.Cos(omega);

            var b0 = 1 + alpha * a;
            var b1 = -2 * cosw;
            var b2 = 1 - alpha * a;

            var a0 = 1 + alpha / a;
            var a1 = -2 * cosw;
            var a2 = 1 - alpha / a;

            B = new[] { b0, b1, b2 };
            A = new[] { a0, a1, a2 };

            Normalize();
        }
    }
}