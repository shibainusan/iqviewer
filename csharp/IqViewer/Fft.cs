using System;

namespace IqViewer
{
    public static class Fft
    {
        // In-place radix-2 complex FFT. n must be a power of two.
        public static void Transform(double[] re, double[] im)
        {
            int n = re.Length;
            for (int i = 1, j = 0; i < n; i++)
            {
                int bit = n >> 1;
                for (; (j & bit) != 0; bit >>= 1) j ^= bit;
                j ^= bit;
                if (i < j)
                {
                    double t = re[i]; re[i] = re[j]; re[j] = t;
                    t = im[i]; im[i] = im[j]; im[j] = t;
                }
            }
            for (int len = 2; len <= n; len <<= 1)
            {
                double ang = -2 * Math.PI / len;
                double wr = Math.Cos(ang), wi = Math.Sin(ang);
                for (int i = 0; i < n; i += len)
                {
                    double cr = 1, ci = 0;
                    for (int k = 0; k < len / 2; k++)
                    {
                        int a = i + k, b = i + k + len / 2;
                        double xr = re[b] * cr - im[b] * ci;
                        double xi = re[b] * ci + im[b] * cr;
                        re[b] = re[a] - xr; im[b] = im[a] - xi;
                        re[a] += xr; im[a] += xi;
                        double t = cr * wr - ci * wi;
                        ci = cr * wi + ci * wr;
                        cr = t;
                    }
                }
            }
        }

        // Single (non-averaged) Hann-windowed FFT of n samples starting at 'start'.
        // Returns dBFS per bin, fftshifted (index 0 = -fs/2, index n/2 = DC).
        // A full-scale complex tone reads 0 dBFS.
        public static double[] SpectrumDbfs(IqData d, long start, int n, double fullScale)
        {
            var re = new double[n];
            var im = new double[n];
            double wsum = 0;
            for (int k = 0; k < n; k++)
            {
                double w = 0.5 - 0.5 * Math.Cos(2 * Math.PI * k / n);
                wsum += w;
                long idx = start + k;
                if (idx >= d.Length) break;
                re[k] = d.I[idx] / fullScale * w;
                im[k] = d.Q[idx] / fullScale * w;
            }
            Transform(re, im);
            var db = new double[n];
            for (int k = 0; k < n; k++)
            {
                int src = (k + n / 2) % n;
                db[k] = IqData.ToDb(Math.Sqrt(re[src] * re[src] + im[src] * im[src]) / wsum);
            }
            return db;
        }
    }
}
