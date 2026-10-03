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
        readonly ComboBox _mode = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        readonly TextBox _spaStart = new TextBox { Text = "2400" };
        readonly TextBox _spaStop = new TextBox { Text = "2600" };
        readonly TextBox _spaStep = new TextBox { Text = "40" };
        readonly CheckBox _maxHold = new CheckBox { Text = "Max hold", AutoSize = true };
        readonly TextBox _addr = new TextBox { Text = "192.168.2.131" };
        readonly CheckBox _setLo = new CheckBox { Text = "Set RX LO (MHz)", AutoSize = true };
        readonly TextBox _lo = new TextBox { Text = "2450" };
        readonly TextBox _rate = new TextBox { Text = "61.44" };
        readonly TextBox _bw = new TextBox { Text = "56" };
        readonly ComboBox _rxChannel = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        readonly ComboBox _gainMode = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        readonly TextBox _gain = new TextBox { Text = "50" };
        readonly TextBox _duration = new TextBox { Text = "1" };
        readonly TextBox _buf = new TextBox { Text = "65536" };
        readonly TextBox _file = new TextBox { Text = "iqcap.raw" };
        readonly TextBox _tools = new TextBox();
        readonly TextBox _fullScale = new TextBox { Text = "2048" };
        readonly Button _capture = new Button { Text = "Capture", Height = 32 };
        readonly Button _cancel = new Button { Text = "Cancel", Enabled = false };
        readonly Button _load = new Button { Text = "Load file..." };
        readonly NumericUpDown _viewStart = new NumericUpDown { Maximum = int.MaxValue };
        readonly NumericUpDown _viewLen = new NumericUpDown { Maximum = int.MaxValue };
        readonly Label _stats = new Label { AutoSize = false, Height = 64, Dock = DockStyle.Top, Font = new Font("Consolas", 10f), Padding = new Padding(4) };
        readonly CheckBox _continuous = new CheckBox { Text = "Continuous capture", AutoSize = true };
        readonly ComboBox _fftSize = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        readonly WaveformControl _plot = new WaveformControl { Dock = DockStyle.Fill };
        readonly SpectrumControl _spectrum = new SpectrumControl { Dock = DockStyle.Fill };
        readonly TextBox _log = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Bottom, Height = 110 };

        IqData _data;
        CancellationTokenSource _cts;
        SdrCapture _sdr;
        bool _updatingView;
        int _row;                      // next row of the parameter table
        Control[] _vsaOnly, _spaOnly;  // shown only in that mode
        double[] _holdDb;                // VSA max-hold spectrum (null = nothing held)
        long _holdStart;
        double _holdFs, _holdFull;
        long _swStart, _swStop, _swStep; // last valid SPA sweep (Hz), set by TryBuildParams

        public MainForm()
        {
            Text = "IQ Viewer";
            ClientSize = new Size(1150, 700);
            _mode.Items.AddRange(new object[] { "VSA", "SPA" });
            _mode.SelectedIndex = 0;
            _gainMode.Items.AddRange(new object[] { "manual", "slow_attack", "fast_attack", "hybrid" });
            _gainMode.SelectedIndex = 0;
            _rxChannel.Items.AddRange(new object[] { "RX1", "RX2" });
            _rxChannel.SelectedIndex = 0;
            foreach (int n in new[] { 32, 64, 128, 256, 512, 1024, 2048, 4096 }) _fftSize.Items.Add(n);
            _fftSize.SelectedItem = 4096;
            _file.Text = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "iqcap.raw");

            var left = new TableLayoutPanel { Dock = DockStyle.Left, Width = 300, ColumnCount = 2, Padding = new Padding(6), AutoScroll = true };
            left.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140));
            left.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            AddRow(left, "Mode", _mode);
            AddRow(left, "IIO Address", _addr);
            AddRow(left, _setLo, _lo);
            var spaLo = new[] { AddRow(left, "Start LO (MHz)", _spaStart), AddRow(left, "Stop LO (MHz)", _spaStop), AddRow(left, "Step (MHz)", _spaStep) };
            AddRow(left, "Sample rate (MHz)", _rate);
            AddRow(left, "RF BW (MHz)", _bw);
            AddRow(left, "RX channel", _rxChannel);
            AddRow(left, "Gain mode", _gainMode);
            AddRow(left, "Gain (dB)", _gain);
            var durLbl = AddRow(left, "Sampling duration (ms)", _duration);
            AddRow(left, "Buffer size", _buf);
            AddRow(left, "Output file", _file);
            AddRow(left, "iio tools dir", _tools);
            AddRow(left, "Full scale", _fullScale);
            AddRow(left, "FFT size (bins)", _fftSize);
            var btns = new FlowLayoutPanel { AutoSize = true };
            btns.Controls.AddRange(new Control[] { _capture, _cancel, _load });
            left.Controls.Add(btns, 0, _row++);
            left.SetColumnSpan(btns, 2);
            AddRow(left, _continuous, null);
            AddRow(left, _maxHold, null);
            AddRow(left, "View start", _viewStart);
            AddRow(left, "View length", _viewLen);

            _vsaOnly = new Control[] { _setLo, _lo, durLbl, _duration };
            _spaOnly = new Control[] { spaLo[0], _spaStart, spaLo[1], _spaStop, spaLo[2], _spaStep };
            ApplyMode();
            _maxHold.CheckedChanged += (s, e) => { _holdDb = null; UpdateFft(); };
            _mode.SelectedIndexChanged += (s, e) => { ApplyMode(); if (!IsSpa) UpdateFft(); };

            var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal };
            split.Panel1.Controls.Add(_plot);
            split.Panel2.Controls.Add(_spectrum);
            Controls.Add(split);
            Controls.Add(_stats);
            Controls.Add(_log);
            Controls.Add(left);

            _sdr = new SdrCapture(Log);
            _capture.Click += (s, e) => StartCapture();
            _cancel.Click += (s, e) => { if (_cts != null) _cts.Cancel(); _sdr.Cancel(); };
            _load.Click += (s, e) => LoadDialog();
            _fullScale.Leave += (s, e) => Refresh2();
            _rate.Leave += (s, e) => Refresh2();
            _plot.MarkerChanged += (s, e) => UpdateFft();
            _fftSize.SelectedIndexChanged += (s, e) => UpdateFft();
            Shown += (s, e) => split.SplitterDistance = split.Height / 2;
            _plot.ViewChanged += (s, e) => SyncViewFields();
            _viewStart.ValueChanged += (s, e) => ApplyViewFields();
            _viewLen.ValueChanged += (s, e) => ApplyViewFields();
        }

        // Cells are placed explicitly so hiding a row's controls (mode switch) cannot shift the others.
        Label AddRow(TableLayoutPanel t, string label, Control c)
        {
            var l = new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 6, 3, 3) };
            AddRow(t, l, c);
            return l;
        }

        void AddRow(TableLayoutPanel t, Control a, Control b)
        {
            t.Controls.Add(a, 0, _row);
            if (b == null) t.SetColumnSpan(a, 2);
            else
            {
                b.Dock = DockStyle.Fill;
                t.Controls.Add(b, 1, _row);
            }
            _row++;
        }

        bool IsSpa { get { return _mode.SelectedIndex == 1; } }

        void ApplyMode()
        {
            foreach (var c in _vsaOnly) c.Visible = !IsSpa;
            foreach (var c in _spaOnly) c.Visible = IsSpa;
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

        const double MinLoMHz = 50, MaxLoMHz = 6000;
        string _paramError = "Invalid numeric parameter.";

        // Reads the parameter fields; null (with _paramError set) if any field is not (yet) valid.
        CaptureParams TryBuildParams()
        {
            try
            {
                bool spa = IsSpa;
                if (!spa && _setLo.Checked && (D(_lo) < MinLoMHz || D(_lo) > MaxLoMHz))
                {
                    _paramError = "RX LO must be between " + MinLoMHz + " and " + MaxLoMHz + " MHz.";
                    return null;
                }
                long rate = Hz(_rate), swStart = 0, swStop = 0, swStep = 0;
                if (spa)
                {
                    swStart = Hz(_spaStart); swStop = Hz(_spaStop); swStep = Hz(_spaStep);
                    string err = SweepLayout.Validate(swStart, swStop, swStep, rate, MinLoMHz * 1e6, MaxLoMHz * 1e6);
                    if (err != null) { _paramError = err; return null; }
                }
                _paramError = "Invalid numeric parameter.";
                var cp = new CaptureParams
                {
                    Address = _addr.Text.Trim(),
                    SetLo = !spa && _setLo.Checked,
                    LoFreq = spa ? 0 : Hz(_lo),
                    SampleRate = rate,
                    Bandwidth = Hz(_bw),
                    RxChannel = _rxChannel.SelectedIndex + 1,
                    GainMode = (string)_gainMode.SelectedItem,
                    Gain = D(_gain),
                    // SPA only needs one FFT's worth of samples per LO step.
                    TotalSamples = spa ? (int)_fftSize.SelectedItem : (long)Math.Round(D(_duration) * 1e-3 * rate),
                    BufferSize = (int)L(_buf),
                    OutputFile = _file.Text.Trim(),
                    ToolsDir = _tools.Text.Trim(),
                };
                if (spa) { _swStart = swStart; _swStop = swStop; _swStep = swStep; }
                return cp;
            }
            catch (FormatException) { return null; }
            catch (OverflowException) { return null; }
        }

        async void StartCapture()
        {
            CaptureParams p = TryBuildParams();
            if (p == null)
            {
                MessageBox.Show(this, _paramError, Text);
                return;
            }

            _holdDb = null; // max hold applies within one run
            _capture.Enabled = false;
            _cancel.Enabled = true;
            _cts = new CancellationTokenSource();
            var ct = _cts.Token;
            bool cont = _continuous.Checked;
            _continuous.Enabled = false;
            _mode.Enabled = false;
            Log((IsSpa ? "SPA " : "") + (cont ? "--- Continuous capture start (Cancel to stop)" : "--- Capture start") + ", RX" + p.RxChannel + " ---");
            try
            {
                if (IsSpa) await RunSweeps(p, cont, ct);
                else if (!cont)
                {
                    if (await Task.Run(() => _sdr.Run(p, ct))) LoadFile(p.OutputFile);
                    else Log("Cancelled.");
                }
                else
                {
                    // Configure, then read + redraw back to back until cancelled or an error occurs.
                    // Parameters are re-read before every read: hardware settings are re-applied only
                    // when they changed; duration/buffer/file take effect on the next read. Fields that
                    // are mid-edit (invalid) are ignored and the last valid values are kept.
                    int n = 0;
                    CaptureParams applied = p;
                    bool ok = await Task.Run(() => _sdr.Configure(applied, ct));
                    while (ok)
                    {
                        var np = TryBuildParams();
                        if (np != null)
                        {
                            if (!np.SameHardwareConfig(applied))
                            {
                                Log("Parameters changed, reconfiguring...");
                                var cfg = np;
                                if (!await Task.Run(() => _sdr.Configure(cfg, ct))) break;
                                applied = np;
                                _holdDb = null; // held peaks are not comparable after a hardware change
                            }
                            p = np;
                        }
                        var cur = p;
                        if (!await Task.Run(() => _sdr.ReadOnce(cur, ct, false))) break;
                        LoadFile(cur.OutputFile, true, true);
                        _stats.Text += string.Format("   #{0}", ++n);
                    }
                    Log("Stopped.");
                }
            }
            catch (Exception ex) { Log("ERROR: " + ex.Message); }
            finally
            {
                _capture.Enabled = true;
                _cancel.Enabled = false;
                _continuous.Enabled = true;
                _mode.Enabled = true;
            }
        }

        // SPA: step the RX LO from Start to Stop, one non-averaged FFT per step, stitched into one trace.
        // Continuous repeats the sweep; parameters are re-read at the start of each sweep.
        async Task RunSweeps(CaptureParams p, bool cont, CancellationToken ct)
        {
            CaptureParams applied = p;
            if (!await Task.Run(() => _sdr.Configure(applied, ct))) { Log("Cancelled."); return; }
            SweepLayout layout = null;
            double[] trace = null;
            bool prevHold = false;
            for (int sweep = 1; ; sweep++)
            {
                if (sweep > 1)
                {
                    var np = TryBuildParams(); // invalid (mid-edit) fields keep the last valid values
                    if (np != null)
                    {
                        if (!np.SameHardwareConfig(applied))
                        {
                            Log("Parameters changed, reconfiguring...");
                            if (!await Task.Run(() => _sdr.Configure(np, ct))) { Log("Stopped."); return; }
                            applied = np;
                            layout = null; // gain/rate/etc. changed: held peaks are no longer comparable
                        }
                        p = np;
                    }
                }
                bool hold = _maxHold.Checked;
                if (hold && !prevHold) layout = null; // turning max hold on starts from an empty trace
                prevHold = hold;
                double full;
                if (!double.TryParse(_fullScale.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out full) || full <= 0) full = 2048;
                int n = (int)_fftSize.SelectedItem;
                var lay = SweepLayout.Build(_swStart, _swStop, _swStep, p.SampleRate, n);
                if (!lay.SameAs(layout))
                {
                    // New layout: start from an empty trace. An unchanged layout keeps the previous
                    // sweep visible until each step overwrites its own slice.
                    layout = lay;
                    trace = new double[lay.Total];
                    for (int k = 0; k < trace.Length; k++) trace[k] = double.NaN;
                }
                var cur = p;
                for (int i = 0; i < layout.StepCount; i++)
                {
                    long lo = layout.LoHz[i];
                    IqData got = null;
                    double[] db = await Task.Run(() =>
                    {
                        if (!_sdr.SetLo(cur, lo, ct) || !_sdr.ReadOnce(cur, ct, false)) return null;
                        got = IqData.Load(cur.OutputFile);
                        return Fft.SpectrumDbfs(got, 0, n, full);
                    });
                    if (db == null) { Log(cont ? "Stopped." : "Cancelled."); return; }
                    layout.CopySlice(i, db, trace, hold);
                    _spectrum.SetTrace(layout.FreqMHz, trace, layout.XMinMHz, layout.XMaxMHz,
                        string.Format(CultureInfo.InvariantCulture, "SPA sweep {0}  step {1}/{2}  LO {3:0.###} MHz  RBW {4:0.###} kHz{5}",
                            sweep, i + 1, layout.StepCount, lo / 1e6, p.SampleRate / (double)n / 1e3,
                            hold ? "  MAX HOLD" : ""));
                    _data = got; // time-domain panel shows the latest step
                    Refresh2(true, true);
                }
                if (!cont) { Log("Sweep done."); return; }
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

        void LoadFile(string path, bool keepView = false, bool quiet = false)
        {
            try
            {
                _data = IqData.Load(path);
                if (!quiet) Log(string.Format("Loaded {0}: {1} samples", path, _data.Length));
                Refresh2(true, keepView);
            }
            catch (Exception ex) { Log("ERROR: " + ex.Message); }
        }

        void Refresh2(bool reset = false, bool keepView = false)
        {
            double fs, full;
            if (!double.TryParse(_rate.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out fs)) return;
            fs *= 1e6;
            if (!double.TryParse(_fullScale.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out full) || full <= 0) return;
            if (_data == null) return;
            if (reset) _plot.SetData(_data, fs, full, keepView); else _plot.SetScale(fs, full);
            UpdateFft();

            var s = _data.ComputeStats(full);
            _stats.Text = string.Format(CultureInfo.InvariantCulture,
                "Samples: {0}   Duration: {1:0.###} ms   (FS = {2})\r\n" +
                "RMS: {3:0.000000} (linear)   Peak: {4:0.00} dBFS   Average: {5:0.00} dBFS   Min: {6:0.00} dBFS",
                s.Count, s.Count / fs * 1e3, full, s.RmsLinear, s.PeakDbfs, s.AverageDbfs, s.MinDbfs);
        }

        // One non-averaged FFT starting at the time-domain marker.
        void UpdateFft()
        {
            double fs, full;
            if (IsSpa) return; // the spectrum panel belongs to the sweep trace
            if (_data == null || _data.Length == 0) return;
            if (!double.TryParse(_rate.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out fs)) return;
            if (!double.TryParse(_fullScale.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out full) || full <= 0) return;
            fs *= 1e6;
            int n = (int)_fftSize.SelectedItem;
            long start = Math.Max(0, Math.Min(_plot.MarkerSample, _data.Length - n));
            _plot.MarkerSpan = n;
            var db = Fft.SpectrumDbfs(_data, start, n, full);
            bool hold = _maxHold.Checked;
            if (hold && _holdDb != null && _holdDb.Length == n && _holdStart == start && _holdFs == fs && _holdFull == full)
            {
                for (int k = 0; k < n; k++) if (db[k] > _holdDb[k]) _holdDb[k] = db[k];
            }
            else
            {
                // Marker, FFT size, rate or full scale changed (or hold is off): start over.
                _holdDb = hold ? db : null;
                _holdStart = start; _holdFs = fs; _holdFull = full;
            }
            _spectrum.SetSpectrum(hold ? _holdDb : db, fs, string.Format(CultureInfo.InvariantCulture,
                "FFT {0} pts, start sample {1} ({2:0.###} us), RBW {3:0.###} kHz{4}",
                n, start, start / fs * 1e6, fs / n / 1e3, hold ? "  MAX HOLD" : ""));
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
