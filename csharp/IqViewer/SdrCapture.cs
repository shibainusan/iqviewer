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
        public string GainMode = "manual";
        public double Gain;
        public long TotalSamples = 3072000;
        public int BufferSize = 65536;
        public string OutputFile = "iqcap.raw";
        public string ToolsDir = "";
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

            if (c.SetLo && !Exec(attr, phy + "altvoltage0 frequency " + c.LoFreq, ct)) return false;
            if (!Exec(attr, phy + "voltage0 sampling_frequency " + c.SampleRate, ct)) return false;
            if (!Exec(attr, phy + "voltage0 rf_bandwidth " + c.Bandwidth, ct)) return false;
            if (!Exec(attr, phy + "voltage0 gain_control_mode " + c.GainMode, ct)) return false;
            if (c.GainMode == "manual" &&
                !Exec(attr, "-u " + uri + " -i -c ad9361-phy voltage0 hardwaregain " +
                      c.Gain.ToString(CultureInfo.InvariantCulture), ct)) return false;
            return true;
        }

        // Reads TotalSamples with iio_readdev into OutputFile. verbose=false keeps the log quiet
        // (continuous mode) except for stderr output.
        public bool ReadOnce(CaptureParams c, CancellationToken ct, bool verbose)
        {
            string uri = "ip:" + c.Address;
            string exe = Tool(c, "iio_readdev");
            string args = string.Format("-u {0} -b {1} -s {2} cf-ad9361-lpc", uri, c.BufferSize, c.TotalSamples);
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
        bool Exec(string exe, string args, CancellationToken ct)
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
                    throw new InvalidOperationException(Path.GetFileName(exe) + " exit code " + p.ExitCode);
                return true;
            }
        }
    }
}
