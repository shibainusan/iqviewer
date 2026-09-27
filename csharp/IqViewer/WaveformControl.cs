using System;
using System.Drawing;
using System.Windows.Forms;

namespace IqViewer
{
    public class WaveformControl : Control
    {
        IqData _data;
        double _fs = 30720000;
        double _fullScale = 32768;
        bool _dbMode;
        long _start;
        long _length;
        bool _dragging;
        int _dragX;
        long _dragStart;

        const int MarginL = 60, MarginR = 12, MarginT = 10, MarginB = 28;

        public event EventHandler ViewChanged;

        public WaveformControl()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            BackColor = Color.Black;
            ForeColor = Color.Gainsboro;
        }

        public long ViewStart { get { return _start; } }
        public long ViewLength { get { return _length; } }

        public void SetData(IqData data, double sampleRate, double fullScale)
        {
            _data = data;
            _fs = sampleRate;
            _fullScale = fullScale;
            _start = 0;
            _length = data == null ? 0 : data.Length;
            RaiseChanged();
        }

        public void SetScale(double sampleRate, double fullScale)
        {
            _fs = sampleRate;
            _fullScale = fullScale;
            Invalidate();
        }

        public bool DbMode
        {
            get { return _dbMode; }
            set { _dbMode = value; Invalidate(); }
        }

        public void SetView(long start, long length)
        {
            if (_data == null) return;
            long n = _data.Length;
            length = Math.Max(16, Math.Min(length, n));
            start = Math.Max(0, Math.Min(start, n - length));
            _start = start;
            _length = length;
            RaiseChanged();
        }

        void RaiseChanged()
        {
            Invalidate();
            var h = ViewChanged;
            if (h != null) h(this, EventArgs.Empty);
        }

        Rectangle PlotRect
        {
            get { return new Rectangle(MarginL, MarginT, Math.Max(10, Width - MarginL - MarginR), Math.Max(10, Height - MarginT - MarginB)); }
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if (_data == null) return;
            var r = PlotRect;
            double frac = Math.Max(0, Math.Min(1, (e.X - r.Left) / (double)r.Width));
            double factor = e.Delta > 0 ? 0.7 : 1 / 0.7;
            long newLen = (long)Math.Max(16, Math.Min(_data.Length, _length * factor));
            long anchor = _start + (long)(frac * _length);
            SetView(anchor - (long)(frac * newLen), newLen);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();
            _dragging = true;
            _dragX = e.X;
            _dragStart = _start;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!_dragging || _data == null) return;
            double perPx = _length / (double)PlotRect.Width;
            SetView(_dragStart - (long)((e.X - _dragX) * perPx), _length);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            _dragging = false;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            var r = PlotRect;
            using (var gridPen = new Pen(Color.FromArgb(50, 50, 50)))
            using (var axisPen = new Pen(Color.Gray))
            using (var font = new Font("Segoe UI", 8f))
            using (var brush = new SolidBrush(ForeColor))
            {
                double yMin = _dbMode ? -100 : -1.0, yMax = _dbMode ? 0 : 1.0;
                double yStep = _dbMode ? 20 : 0.25;
                for (double y = yMin; y <= yMax + 1e-9; y += yStep)
                {
                    float py = YToPx(y, yMin, yMax, r);
                    g.DrawLine(gridPen, r.Left, py, r.Right, py);
                    g.DrawString(_dbMode ? y.ToString("0") : y.ToString("0.00"), font, brush, 2, py - 7);
                }
                g.DrawString(_dbMode ? "dBFS" : "amp", font, brush, 2, 0);

                if (_data == null || _length <= 0)
                {
                    g.DrawRectangle(axisPen, r);
                    return;
                }

                // Time axis
                double t0 = _start / _fs, t1 = (_start + _length) / _fs;
                double span = t1 - t0;
                string unit; double mul;
                if (span < 1e-3) { unit = "us"; mul = 1e6; }
                else if (span < 1.0) { unit = "ms"; mul = 1e3; }
                else { unit = "s"; mul = 1; }
                for (int k = 0; k <= 8; k++)
                {
                    float px = r.Left + r.Width * k / 8f;
                    g.DrawLine(gridPen, px, r.Top, px, r.Bottom);
                    double t = (t0 + span * k / 8) * mul;
                    g.DrawString(t.ToString("0.###"), font, brush, px - 14, r.Bottom + 2);
                }
                g.DrawString(unit, font, brush, r.Right - 20, r.Bottom + 13);

                g.SetClip(r);
                if (_dbMode)
                    DrawEnvelope(g, r, Color.Yellow, yMin, yMax, 2);
                else
                {
                    DrawEnvelope(g, r, Color.DeepSkyBlue, yMin, yMax, 0);
                    DrawEnvelope(g, r, Color.OrangeRed, yMin, yMax, 1);
                }
                g.ResetClip();
                g.DrawRectangle(axisPen, r);

                if (!_dbMode)
                {
                    g.FillRectangle(Brushes.DeepSkyBlue, r.Right - 60, r.Top + 4, 8, 8);
                    g.DrawString("I", font, brush, r.Right - 50, r.Top + 1);
                    g.FillRectangle(Brushes.OrangeRed, r.Right - 30, r.Top + 4, 8, 8);
                    g.DrawString("Q", font, brush, r.Right - 20, r.Top + 1);
                }
            }
        }

        static float YToPx(double y, double yMin, double yMax, Rectangle r)
        {
            return (float)(r.Bottom - (y - yMin) / (yMax - yMin) * r.Height);
        }

        // ch: 0 = I, 1 = Q, 2 = magnitude in dBFS. Draws min/max envelope per pixel column.
        void DrawEnvelope(Graphics g, Rectangle r, Color color, double yMin, double yMax, int ch)
        {
            using (var pen = new Pen(color))
            {
                double perPx = _length / (double)r.Width;
                float prevMid = float.NaN;
                for (int x = 0; x < r.Width; x++)
                {
                    long a = _start + (long)(x * perPx);
                    long b = Math.Max(a + 1, _start + (long)((x + 1) * perPx));
                    b = Math.Min(b, _data.Length);
                    if (a >= b) break;
                    double lo = double.MaxValue, hi = double.MinValue;
                    for (long k = a; k < b; k++)
                    {
                        double v;
                        if (ch == 0) v = _data.I[k] / _fullScale;
                        else if (ch == 1) v = _data.Q[k] / _fullScale;
                        else
                        {
                            double i = _data.I[k] / _fullScale, q = _data.Q[k] / _fullScale;
                            v = IqData.ToDb(Math.Sqrt(i * i + q * q));
                        }
                        if (v < lo) lo = v;
                        if (v > hi) hi = v;
                    }
                    float pxX = r.Left + x;
                    float y1 = YToPx(hi, yMin, yMax, r), y2 = YToPx(lo, yMin, yMax, r);
                    float mid = (y1 + y2) / 2;
                    if (!float.IsNaN(prevMid) && perPx < 1.5)
                        g.DrawLine(pen, pxX - 1, prevMid, pxX, mid);
                    else
                        g.DrawLine(pen, pxX, y1, pxX, Math.Max(y2, y1 + 1));
                    prevMid = mid;
                }
            }
        }
    }
}
