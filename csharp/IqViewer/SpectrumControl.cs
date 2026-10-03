using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace IqViewer
{
    public class SpectrumControl : Control
    {
        double[] _db;
        double _fs = 30720000;
        string _info = "";
        // Absolute-frequency sweep trace (SPA); NaN points are not drawn.
        double[] _trF, _trDb;
        double _xMin, _xMax;

        const int MarginL = 60, MarginR = 12, MarginT = 10, MarginB = 28;
        const double YMin = -120, YMax = 0;

        public SpectrumControl()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.ResizeRedraw, true);
            BackColor = Color.Black;
            ForeColor = Color.Gainsboro;
        }

        public void SetSpectrum(double[] db, double sampleRate, string info)
        {
            _db = db;
            _trF = null;
            _fs = sampleRate;
            _info = info;
            Invalidate();
        }

        public void SetTrace(double[] fMHz, double[] db, double xMinMHz, double xMaxMHz, string info)
        {
            _db = null;
            _trF = fMHz;
            _trDb = db;
            _xMin = xMinMHz;
            _xMax = xMaxMHz;
            _info = info;
            Invalidate();
        }

        // Max per pixel column, so hundreds of thousands of points stay cheap to draw.
        void DrawTrace(Graphics g, Rectangle r, Pen pen)
        {
            int w = r.Width;
            var col = new double[w];
            for (int i = 0; i < w; i++) col[i] = double.NaN;
            double span = _xMax - _xMin;
            if (span <= 0) return;
            for (int k = 0; k < _trF.Length; k++)
            {
                double v = _trDb[k];
                if (double.IsNaN(v)) continue;
                int x = (int)((_trF[k] - _xMin) / span * (w - 1));
                if (x < 0 || x >= w) continue;
                if (double.IsNaN(col[x]) || v > col[x]) col[x] = v;
            }
            var run = new List<PointF>();
            g.SetClip(r);
            for (int x = 0; x <= w; x++)
            {
                if (x < w && !double.IsNaN(col[x]))
                {
                    double v = Math.Max(YMin, Math.Min(YMax, col[x]));
                    run.Add(new PointF(r.Left + x, (float)(r.Bottom - (v - YMin) / (YMax - YMin) * r.Height)));
                }
                else
                {
                    if (run.Count > 1) g.DrawLines(pen, run.ToArray());
                    else if (run.Count == 1) g.DrawLine(pen, run[0].X, run[0].Y, run[0].X + 1, run[0].Y);
                    run.Clear();
                }
            }
            g.ResetClip();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            var r = new Rectangle(MarginL, MarginT, System.Math.Max(10, Width - MarginL - MarginR), System.Math.Max(10, Height - MarginT - MarginB));
            using (var gridPen = new Pen(Color.FromArgb(50, 50, 50)))
            using (var axisPen = new Pen(Color.Gray))
            using (var font = new Font("Segoe UI", 8f))
            using (var brush = new SolidBrush(ForeColor))
            using (var pen = new Pen(Color.LimeGreen))
            {
                for (double y = YMin; y <= YMax + 1e-9; y += 20)
                {
                    float py = (float)(r.Bottom - (y - YMin) / (YMax - YMin) * r.Height);
                    g.DrawLine(gridPen, r.Left, py, r.Right, py);
                    g.DrawString(y.ToString("0"), font, brush, 2, py - 7);
                }
                g.DrawString("dBFS", font, brush, 2, 0);
                bool abs = _trF != null;
                double fmhz = _fs / 2e6;
                for (int k = 0; k <= 8; k++)
                {
                    float px = r.Left + r.Width * k / 8f;
                    g.DrawLine(gridPen, px, r.Top, px, r.Bottom);
                    double f = abs ? _xMin + (_xMax - _xMin) * k / 8 : -fmhz + 2 * fmhz * k / 8;
                    g.DrawString(f.ToString("0.##"), font, brush, px - 14, r.Bottom + 2);
                }
                g.DrawString(abs ? "MHz" : "MHz (baseband)", font, brush, r.Right - 90, r.Bottom + 13);
                g.DrawString(_info, font, brush, r.Left + 4, r.Top + 1);

                if (abs) DrawTrace(g, r, pen);

                if (_db != null && _db.Length > 1)
                {
                    int n = _db.Length;
                    var pts = new PointF[n];
                    for (int k = 0; k < n; k++)
                    {
                        double v = System.Math.Max(YMin, System.Math.Min(YMax, _db[k]));
                        pts[k] = new PointF(r.Left + r.Width * k / (float)(n - 1),
                                            (float)(r.Bottom - (v - YMin) / (YMax - YMin) * r.Height));
                    }
                    g.SetClip(r);
                    g.DrawLines(pen, pts);
                    g.ResetClip();
                }
                g.DrawRectangle(axisPen, r);
            }
        }
    }
}
