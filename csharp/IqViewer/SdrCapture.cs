using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;

namespace IqViewer
{
    public class CaptureParams
    {
        public string Address = "192.168.2.131";
        public bool SetLo;
        public long LoFreq = 351000000;
        public long SampleRate = 30720000;
        public long Bandwidth = 40000000;
        public int RxChannel = 1; // 1 = RX1, 2 = RX2 (needs the SDR in 2R2T mode)
        public string GainMode = "manual";
        public double Gain;
        public long TotalSamples = 3072000;
        public int BufferSize = 65536;
        public string OutputFile = "iqcap.raw";
        public string ToolsDir = "";

        // True if applying 'o' would not change any SDR setting (Configure can be skipped).
        public bool SameHardwareConfig(CaptureParams o)
        {
            return Address == o.Address && ToolsDir == o.ToolsDir && RxChannel == o.RxChannel &&
                   SampleRate == o.SampleRate && Bandwidth == o.Bandwidth &&
                   SetLo == o.SetLo && (!SetLo || LoFreq == o.LoFreq) &&
                   GainMode == o.GainMode && (GainMode != "manual" || Gain == o.Gain);
        }
    }

    public class SdrCapture
    {
        readonly Action<string> _log;
        volatile Process _current;

        public SdrCapture(Action<string> log) { _log = log; }

        public void Cancel()
        {
            try { var p = _current; if (p != null && !p.HasExited) p.Kill(); } catch { }
        }

        static string Tool(CaptureParams c, string name)
        {
            if (!string.IsNullOrWhiteSpace(c.ToolsDir)) return Path.Combine(c.ToolsDir, name + ".exe");
            return name;
        }

        // Runs on a worker thread. Throws on failure; returns false if cancelled (no exception,
        // so the debugger does not break on a first-chance OperationCanceledException).
        public bool Run(CaptureParams c, CancellationToken ct)
        {
            return Configure(c, ct) && ReadOnce(c, ct, true);
        }

        // Applies the AD9361 settings (iio_attr). Needed once; repeated reads reuse them.
        public bool Configure(CaptureParams c, CancellationToken ct)
        {
            string uri = "ip:" + c.Address;
            string attr = Tool(c, "iio_attr");
            string phy = "-u " + uri + " -c ad9361-phy ";

            if (c.RxChannel == 2)
            {
                // RX2 only exists when the AD9361 runs in 2R2T mode; fail early with a clear message.
                if (!Exec(attr, "-u " + uri + " -c cf-ad9361-lpc voltage2 sampling_frequency", ct,
                        "RX2 not found: the SDR is not in 2R2T mode (adi,2rx-2tx-mode-enable=0)")) return false;
            }
            string rxIn = "voltage" + (c.RxChannel - 1); // ad9361-phy input channel of the selected RX

            if (c.SetLo && !Exec(attr, phy + "altvoltage0 frequency " + c.LoFreq, ct)) return false;
            if (!Exec(attr, phy + "voltage0 sampling_frequency " + c.SampleRate, ct)) return false;
            if (!Exec(attr, phy + "voltage0 rf_bandwidth " + c.Bandwidth, ct)) return false;
            // -i: RX2's voltage1 must not resolve to the TX output channel of the same name
            if (!Exec(attr, "-u " + uri + " -i -c ad9361-phy " + rxIn + " gain_control_mode " + c.GainMode, ct)) return false;
            if (c.GainMode == "manual" &&
                !Exec(attr, "-u " + uri + " -i -c ad9361-phy " + rxIn + " hardwaregain " +
                      c.Gain.ToString(CultureInfo.InvariantCulture), ct)) return false;
            return true;
        }

        // Reads TotalSamples with iio_readdev into OutputFile. verbose=false keeps the log quiet
        // (continuous mode) except for stderr output.
        public bool ReadOnce(CaptureParams c, CancellationToken ct, bool verbose)
        {
            string uri = "ip:" + c.Address;
            string exe = Tool(c, "iio_readdev");
            // Always name the channels: with none, iio_readdev enables all of them (4ch in 2R2T mode).
            string args = string.Format("-u {0} -b {1} -s {2} cf-ad9361-lpc {3}", uri, c.BufferSize, c.TotalSamples,
                c.RxChannel == 2 ? "voltage2 voltage3" : "voltage0 voltage1");
            if (verbose) _log(exe + " " + args + " > " + c.OutputFile);
            var psi = Base(exe, args);
            using (var p = Process.Start(psi))
            {
                _current = p;
                var err = new StringBuilder();
                p.ErrorDataReceived += (s, e) => { if (e.Data != null) err.AppendLine(e.Data); };
                p.BeginErrorReadLine();
                using (var fs = File.Create(c.OutputFile))
                    p.StandardOutput.BaseStream.CopyTo(fs);
                p.WaitForExit();
                _current = null;
                if (err.Length > 0) _log(err.ToString().TrimEnd());
                if (ct.IsCancellationRequested) return false;
                if (p.ExitCode != 0) throw new InvalidOperationException("iio_readdev exit code " + p.ExitCode);
            }
            if (verbose)
                _log(string.Format("Saved {0} bytes (expected {1})",
                    new FileInfo(c.OutputFile).Length, c.TotalSamples * 4));
            return true;
        }

        static ProcessStartInfo Base(string exe, string args)
        {
            return new ProcessStartInfo(exe, args)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
        }

        // Returns false if cancelled; throws if the command fails.
        bool Exec(string exe, string args, CancellationToken ct, string failMessage = null)
        {
            if (ct.IsCancellationRequested) return false;
            _log(Path.GetFileName(exe) + " " + args);
            using (var p = Process.Start(Base(exe, args)))
            {
                _current = p;
                var errTask = p.StandardError.ReadToEndAsync();
                string outp = p.StandardOutput.ReadToEnd();
                p.WaitForExit();
                string err = errTask.Result;
                _current = null;
                if (outp.Trim().Length > 0) _log(outp.TrimEnd());
                if (err.Trim().Length > 0) _log(err.TrimEnd());
                if (ct.IsCancellationRequested) return false;
                if (p.ExitCode != 0)
                    throw new InvalidOperationException(failMessage ??
                        Path.GetFileName(exe) + " exit code " + p.ExitCode);
                return true;
            }
        }
    }
}
