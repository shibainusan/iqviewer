using System;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace IqViewer
{
    public class MainForm : Form
    {
        readonly TextBox _addr = new TextBox { Text = "192.168.2.131" };
        readonly CheckBox _setLo = new CheckBox { Text = "Set RX LO (MHz)", AutoSize = true };
        readonly TextBox _lo = new TextBox { Text = "351" };
        readonly TextBox _rate = new TextBox { Text = "30.72" };
        readonly TextBox _bw = new TextBox { Text = "40" };
        readonly ComboBox _gainMode = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        readonly TextBox _gain = new TextBox { Text = "0" };
        readonly TextBox _samples = new TextBox { Text = "3072000" };
        readonly TextBox _buf = new TextBox { Text = "65536" };
        readonly TextBox _file = new TextBox { Text = "iqcap.raw" };
        readonly TextBox _tools = new TextBox();
        readonly TextBox _fullScale = new TextBox { Text = "32768" };
        readonly Button _capture = new Button { Text = "Capture", Height = 32 };
        readonly Button _cancel = new Button { Text = "Cancel", Enabled = false };
        readonly Button _load = new Button { Text = "Load file..." };
        readonly CheckBox _dbMode = new CheckBox { Text = "Magnitude (dBFS)", AutoSize = true };
        readonly NumericUpDown _viewStart = new NumericUpDown { Maximum = int.MaxValue };
        readonly NumericUpDown _viewLen = new NumericUpDown { Maximum = int.MaxValue };
        readonly Label _stats = new Label { AutoSize = false, Height = 64, Dock = DockStyle.Top, Font = new Font("Consolas", 10f), Padding = new Padding(4) };
        readonly WaveformControl _plot = new WaveformControl { Dock = DockStyle.Fill };
        readonly TextBox _log = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Bottom, Height = 110 };

        IqData _data;
        CancellationTokenSource _cts;
        SdrCapture _sdr;
        bool _updatingView;

        public MainForm()
        {
            Text = "IQ Viewer";
            ClientSize = new Size(1150, 700);
            _gainMode.Items.AddRange(new object[] { "manual", "slow_attack", "fast_attack", "hybrid" });
            _gainMode.SelectedIndex = 0;
            _file.Text = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "iqcap.raw");

            var left = new TableLayoutPanel { Dock = DockStyle.Left, Width = 270, ColumnCount = 2, Padding = new Padding(6), AutoScroll = true };
            left.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 105));
            left.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            AddRow(left, "Address", _addr);
            AddRow(left, _setLo, _lo);
            AddRow(left, "Sample rate (MHz)", _rate);
            AddRow(left, "RF BW (MHz)", _bw);
            AddRow(left, "Gain mode", _gainMode);
            AddRow(left, "Gain (dB)", _gain);
            AddRow(left, "Total samples", _samples);
            AddRow(left, "Buffer size", _buf);
            AddRow(left, "Output file", _file);
            AddRow(left, "iio tools dir", _tools);
            AddRow(left, "Full scale", _fullScale);
            var btns = new FlowLayoutPanel { AutoSize = true };
            btns.Controls.AddRange(new Control[] { _capture, _cancel, _load });
            left.Controls.Add(btns);
            left.SetColumnSpan(btns, 2);
            AddRow(left, _dbMode, null);
            AddRow(left, "View start", _viewStart);
            AddRow(left, "View length", _viewLen);

            Controls.Add(_plot);
            Controls.Add(_stats);
            Controls.Add(_log);
            Controls.Add(left);

            _sdr = new SdrCapture(Log);
            _capture.Click += (s, e) => StartCapture();
            _cancel.Click += (s, e) => { if (_cts != null) _cts.Cancel(); _sdr.Cancel(); };
            _load.Click += (s, e) => LoadDialog();
            _dbMode.CheckedChanged += (s, e) => _plot.DbMode = _dbMode.Checked;
            _fullScale.Leave += (s, e) => Refresh2();
            _rate.Leave += (s, e) => Refresh2();
            _plot.ViewChanged += (s, e) => SyncViewFields();
            _viewStart.ValueChanged += (s, e) => ApplyViewFields();
            _viewLen.ValueChanged += (s, e) => ApplyViewFields();
        }

        static void AddRow(TableLayoutPanel t, string label, Control c)
        {
            AddRow(t, new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 6, 3, 3) }, c);
        }

        static void AddRow(TableLayoutPanel t, Control a, Control b)
        {
            t.Controls.Add(a);
            if (b == null) { t.SetColumnSpan(a, 2); return; }
            b.Dock = DockStyle.Fill;
            t.Controls.Add(b);
        }

        void Log(string msg)
        {
            if (InvokeRequired) { BeginInvoke(new Action<string>(Log), msg); return; }
            _log.AppendText(msg + Environment.NewLine);
        }

        static long L(TextBox t) { return long.Parse(t.Text.Trim(), CultureInfo.InvariantCulture); }
        static double D(TextBox t) { return double.Parse(t.Text.Trim(), CultureInfo.InvariantCulture); }
        // MHz text (decimals allowed) -> Hz
        static long Hz(TextBox t) { return (long)Math.Round(D(t) * 1e6); }

        async void StartCapture()
        {
            CaptureParams p;
            try
            {
                p = new CaptureParams
                {
                    Address = _addr.Text.Trim(),
                    SetLo = _setLo.Checked,
                    LoFreq = Hz(_lo),
                    SampleRate = Hz(_rate),
                    Bandwidth = Hz(_bw),
                    GainMode = (string)_gainMode.SelectedItem,
                    Gain = D(_gain),
                    TotalSamples = L(_samples),
                    BufferSize = (int)L(_buf),
                    OutputFile = _file.Text.Trim(),
                    ToolsDir = _tools.Text.Trim(),
                };
            }
            catch (FormatException)
            {
                MessageBox.Show(this, "Invalid numeric parameter.", Text);
                return;
            }

            _capture.Enabled = false;
            _cancel.Enabled = true;
            _cts = new CancellationTokenSource();
            var ct = _cts.Token;
            Log("--- Capture start ---");
            try
            {
                await Task.Run(() => _sdr.Run(p, ct));
                LoadFile(p.OutputFile);
            }
            catch (OperationCanceledException) { Log("Cancelled."); }
            catch (Exception ex) { Log("ERROR: " + ex.Message); }
            finally
            {
                _capture.Enabled = true;
                _cancel.Enabled = false;
            }
        }

        void LoadDialog()
        {
            using (var dlg = new OpenFileDialog { Filter = "IQ files (*.raw;*.bin;*.cs16)|*.raw;*.bin;*.cs16|All files|*.*" })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                _file.Text = dlg.FileName;
                LoadFile(dlg.FileName);
            }
        }

        void LoadFile(string path)
        {
            try
            {
                _data = IqData.Load(path);
                Log(string.Format("Loaded {0}: {1} samples", path, _data.Length));
                Refresh2(true);
            }
            catch (Exception ex) { Log("ERROR: " + ex.Message); }
        }

        void Refresh2(bool reset = false)
        {
            double fs, full;
            if (!double.TryParse(_rate.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out fs)) return;
            fs *= 1e6;
            if (!double.TryParse(_fullScale.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out full) || full <= 0) return;
            if (_data == null) return;
            if (reset) _plot.SetData(_data, fs, full); else _plot.SetScale(fs, full);

            var s = _data.ComputeStats(full);
            _stats.Text = string.Format(CultureInfo.InvariantCulture,
                "Samples: {0}   Duration: {1:0.###} ms   (FS = {2})\r\n" +
                "RMS: {3:0.000000} (linear)   Peak: {4:0.00} dBFS   Average: {5:0.00} dBFS   Min: {6:0.00} dBFS",
                s.Count, s.Count / fs * 1e3, full, s.RmsLinear, s.PeakDbfs, s.AverageDbfs, s.MinDbfs);
        }

        void SyncViewFields()
        {
            _updatingView = true;
            try
            {
                int n = _data == null ? 0 : _data.Length;
                _viewStart.Maximum = Math.Max(0, n);
                _viewLen.Maximum = Math.Max(16, n);
                _viewStart.Value = Math.Min(_viewStart.Maximum, _plot.ViewStart);
                _viewLen.Value = Math.Max(_viewLen.Minimum, Math.Min(_viewLen.Maximum, _plot.ViewLength));
            }
            finally { _updatingView = false; }
        }

        void ApplyViewFields()
        {
            if (_updatingView) return;
            _plot.SetView((long)_viewStart.Value, (long)_viewLen.Value);
        }
    }
}
