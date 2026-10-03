using System;

namespace IqViewer
{
    public static class Fft
    {
        // Per-size tables computed once: bit-reversal permutation, twiddle factors, Hann window.
        sealed class Plan
        {
            public int N;
            public int[] Rev;
            public double[] CosT, SinT; // exp(-2*pi*i*k/N), k = 0..N/2-1
            public double[] Window;
            public double WindowSum;
            public double[] Re, Im;     // reusable work buffers
        }

        static readonly System.Collections.Generic.Dictionary<int, Plan> Plans =
            new System.Collections.Generic.Dictionary<int, Plan>();

        static Plan GetPlan(int n)
        {
            lock (Plans)
            {
                Plan p;
                if (Plans.TryGetValue(n, out p)) return p;
                if (n < 2 || (n & (n - 1)) != 0) throw new ArgumentException("FFT size must be a power of two");
                p = new Plan { N = n, Rev = new int[n], CosT = new double[n / 2], SinT = new double[n / 2],
                               Window = new double[n], Re = new double[n], Im = new double[n] };
                int bits = 0;
                while ((1 << bits) < n) bits++;
                for (int i = 0; i < n; i++)
                {
                    int r = 0;
                    for (int b = 0; b < bits; b++) if ((i & (1 << b)) != 0) r |= 1 << (bits - 1 - b);
                    p.Rev[i] = r;
                }
                for (int k = 0; k < n / 2; k++)
                {
                    double ang = -2 * Math.PI * k / n;
                    p.CosT[k] = Math.Cos(ang);
                    p.SinT[k] = Math.Sin(ang);
                }
                for (int k = 0; k < n; k++)
                {
                    p.Window[k] = 0.5 - 0.5 * Math.Cos(2 * Math.PI * k / n);
                    p.WindowSum += p.Window[k];
                }
                Plans[n] = p;
                return p;
            }
        }

        // In-place radix-2 FFT using the precomputed plan. Input must already be in natural order.
        static void Transform(Plan p, double[] re, double[] im)
        {
            int n = p.N;
            var rev = p.Rev;
            for (int i = 0; i < n; i++)
            {
                int j = rev[i];
                if (i < j)
                {
                    double t = re[i]; re[i] = re[j]; re[j] = t;
                    t = im[i]; im[i] = im[j]; im[j] = t;
                }
            }
            for (int len = 2; len <= n; len <<= 1)
            {
                int half = len >> 1, step = n / len;
                for (int i = 0; i < n; i += len)
                {
                    for (int k = 0, tw = 0; k < half; k++, tw += step)
                    {
                        double cr = p.CosT[tw], ci = p.SinT[tw];
                        int a = i + k, b = a + half;
                        double xr = re[b] * cr - im[b] * ci;
                        double xi = re[b] * ci + im[b] * cr;
                        re[b] = re[a] - xr; im[b] = im[a] - xi;
                        re[a] += xr; im[a] += xi;
                    }
                }
            }
        }

        // In-place radix-2 complex FFT. n must be a power of two.
        public static void Transform(double[] re, double[] im)
        {
            Transform(GetPlan(re.Length), re, im);
        }

        // Single (non-averaged) Hann-windowed FFT of n samples starting at 'start'.
        // Returns dBFS per bin, fftshifted (index 0 = -fs/2, index n/2 = DC).
        // A full-scale complex tone reads 0 dBFS.
        public static double[] SpectrumDbfs(IqData d, long start, int n, double fullScale)
        {
            var p = GetPlan(n);
            double scale = 1.0 / fullScale;
            double wsum = p.WindowSum;
            var db = new double[n];
            lock (p) // work buffers are shared per plan
            {
                var re = p.Re;
                var im = p.Im;
                Array.Clear(re, 0, n);
                Array.Clear(im, 0, n);
                int count = (int)Math.Max(0, Math.Min(n, d.Length - start));
                for (int k = 0; k < count; k++)
                {
                    double w = p.Window[k] * scale;
                    re[k] = d.I[start + k] * w;
                    im[k] = d.Q[start + k] * w;
                }
                Transform(p, re, im);
                int h = n / 2;
                for (int k = 0; k < n; k++)
                {
                    int src = k < h ? k + h : k - h;
                    db[k] = IqData.ToDb(Math.Sqrt(re[src] * re[src] + im[src] * im[src]) / wsum);
                }
            }
            return db;
        }
    }
}
