using System.Drawing;
using System.Windows.Forms;

namespace IqViewer
{
    public class SpectrumControl : Control
    {
        double[] _db;
        double _fs = 30720000;
        string _info = "";

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
            _fs = sampleRate;
            _info = info;
            Invalidate();
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
                double fmhz = _fs / 2e6;
                for (int k = 0; k <= 8; k++)
                {
                    float px = r.Left + r.Width * k / 8f;
                    g.DrawLine(gridPen, px, r.Top, px, r.Bottom);
                    g.DrawString((-fmhz + 2 * fmhz * k / 8).ToString("0.##"), font, brush, px - 14, r.Bottom + 2);
                }
                g.DrawString("MHz (baseband)", font, brush, r.Right - 90, r.Bottom + 13);
                g.DrawString(_info, font, brush, r.Left + 4, r.Top + 1);

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
