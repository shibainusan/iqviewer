using System;

namespace IqViewer
{
    // Frequency layout of an SPA sweep. LO steps from Start to Stop; each step contributes the
    // central Step-wide slice of its FFT, so slices tile the span without the DC spike / roll-off edges.
    public class SweepLayout
    {
        public const int MaxSteps = 5000;

        public long[] LoHz;
        public int N;            // FFT size
        public int Bins;         // bins taken from each FFT
        public double[] FreqMHz; // absolute frequency of every trace point
        public double XMinMHz, XMaxMHz;

        public int StepCount { get { return LoHz.Length; } }
        public int Total { get { return FreqMHz.Length; } }

        // Returns an error message, or null if the sweep is acceptable.
        public static string Validate(long startHz, long stopHz, long stepHz, long rateHz, double minHz, double maxHz)
        {
            if (stepHz <= 0) return "SPA: Step must be greater than 0.";
            if (startHz < minHz || stopHz > maxHz)
                return "SPA: Start/Stop must be between " + minHz / 1e6 + " and " + maxHz / 1e6 + " MHz.";
            if (startHz > stopHz) return "SPA: Start must not exceed Stop.";
            if (stepHz > rateHz) return "SPA: Step must not exceed the sample rate (it would leave gaps).";
            if ((stopHz - startHz) / stepHz + 1 > MaxSteps) return "SPA: too many steps (max " + MaxSteps + ").";
            return null;
        }

        public static SweepLayout Build(long startHz, long stopHz, long stepHz, double sampleRate, int n)
        {
            int steps = (int)((stopHz - startHz) / stepHz) + 1;
            double df = sampleRate / n;
            int bins = (int)Math.Max(1, Math.Min(n, Math.Round(stepHz / df)));
            int k0 = n / 2 - bins / 2; // fftshifted spectrum: index n/2 is DC
            var l = new SweepLayout
            {
                N = n,
                Bins = bins,
                LoHz = new long[steps],
                FreqMHz = new double[steps * bins],
            };
            for (int i = 0; i < steps; i++)
            {
                l.LoHz[i] = startHz + (long)i * stepHz;
                for (int j = 0; j < bins; j++)
                    l.FreqMHz[i * bins + j] = (l.LoHz[i] + (k0 + j - n / 2) * df) / 1e6;
            }
            l.XMinMHz = (startHz - stepHz / 2.0) / 1e6;
            l.XMaxMHz = (l.LoHz[steps - 1] + stepHz / 2.0) / 1e6;
            return l;
        }

        // Copies the central slice of one step's fftshifted spectrum into the trace.
        // maxHold keeps the larger of the old and new value per point (NaN = nothing held yet).
        public void CopySlice(int step, double[] spectrumDb, double[] trace, bool maxHold)
        {
            int k0 = N / 2 - Bins / 2, o = step * Bins;
            if (!maxHold)
            {
                Array.Copy(spectrumDb, k0, trace, o, Bins);
                return;
            }
            for (int j = 0; j < Bins; j++)
            {
                double v = spectrumDb[k0 + j];
                if (double.IsNaN(trace[o + j]) || v > trace[o + j]) trace[o + j] = v;
            }
        }

        public bool SameAs(SweepLayout o)
        {
            return o != null && N == o.N && Bins == o.Bins && StepCount == o.StepCount &&
                   FreqMHz[0] == o.FreqMHz[0] && FreqMHz[Total - 1] == o.FreqMHz[o.Total - 1];
        }
    }
}
