using System;
using System.IO;

namespace IqViewer
{
    public struct IqStats
    {
        public long Count;
        public double RmsLinear;   // sqrt(mean(m^2)), linear (FS = 1.0)
        public double PeakDbfs;    // 20*log10(max m)
        public double AverageDbfs; // 20*log10(mean m)
        public double MinDbfs;     // 20*log10(min m)
    }

    public class IqData
    {
        public const double FloorDb = -200.0;

        public short[] I;
        public short[] Q;
        public int Length { get { return I == null ? 0 : I.Length; } }

        // File is interleaved int16 I,Q,I,Q,...
        public static IqData Load(string path)
        {
            byte[] raw = File.ReadAllBytes(path);
            int n = raw.Length / 4;
            var d = new IqData { I = new short[n], Q = new short[n] };
            for (int k = 0; k < n; k++)
            {
                d.I[k] = BitConverter.ToInt16(raw, 4 * k);
                d.Q[k] = BitConverter.ToInt16(raw, 4 * k + 2);
            }
            return d;
        }

        public static double ToDb(double linear)
        {
            return linear <= 1e-10 ? FloorDb : Math.Max(FloorDb, 20.0 * Math.Log10(linear));
        }

        public IqStats ComputeStats(double fullScale)
        {
            var s = new IqStats { Count = Length };
            if (Length == 0) return s;
            double sumSq = 0, sumMag = 0, max = 0, min = double.MaxValue;
            for (int k = 0; k < Length; k++)
            {
                double i = I[k] / fullScale, q = Q[k] / fullScale;
                double p = i * i + q * q;
                double m = Math.Sqrt(p);
                sumSq += p;
                sumMag += m;
                if (m > max) max = m;
                if (m < min) min = m;
            }
            s.RmsLinear = Math.Sqrt(sumSq / Length);
            s.PeakDbfs = ToDb(max);
            s.AverageDbfs = ToDb(sumMag / Length);
            s.MinDbfs = ToDb(min);
            return s;
        }
    }
}
