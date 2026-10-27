using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using LibreHardwareMonitor.Hardware;

namespace HwTemp
{
    internal static class Program
    {
        [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr ShellExecute(IntPtr hwnd, string lpOperation, string lpFile,
            string lpParameters, string lpDirectory, int nShowCmd);

        [STAThread]
        private static void Main(string[] args)
        {
            bool skipElevate = false;
            foreach (string a in args)
            {
                if (string.Equals(a, "/noelevate", StringComparison.OrdinalIgnoreCase)) skipElevate = true;
            }

            bool admin = false;
            try
            {
                var id = System.Security.Principal.WindowsIdentity.GetCurrent();
                var pr = new System.Security.Principal.WindowsPrincipal(id);
                admin = pr.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
            }
            catch { }

            if (!admin && !skipElevate)
            {
                try
                {
                    string exe = Application.ExecutablePath;
                    IntPtr r = ShellExecute(IntPtr.Zero, "runas", exe, "/elevated",
                        Path.GetDirectoryName(exe), 1);
                    if (r.ToInt64() > 32) return;   // elevated instance launched
                }
                catch { }
            }

            bool created = false;
            var mtx = new Mutex(true, "Local\\HardwareTempMonitorSingle", out created);
            if (!created) return;

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (s, e) => Program.Log("UI exception: " + e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (s, e) => Program.Log("app exception: " + e.ExceptionObject);
            try
            {
                Application.Run(new MainForm(admin));
            }
            catch (Exception ex)
            {
                Log("fatal: " + ex);
            }
            GC.KeepAlive(mtx);
        }

        public static readonly string AppDir = Path.GetDirectoryName(Application.ExecutablePath);
        public static readonly string LogPath = Path.Combine(AppDir, "app.log");

        public static void Log(string msg)
        {
            try
            {
                File.AppendAllText(LogPath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + msg + "\r\n");
            }
            catch { }
        }
    }

    internal sealed class SensorHub : IDisposable
    {
        private Computer _computer;
        public bool Ready { get; private set; }
        public bool IsAdmin { get; private set; }
        public string Error = "";
        public string CpuName = "";
        public string GpuName = "";
        public double? CpuTemp, CpuLoad, CpuClock, GpuTemp, GpuHot, GpuLoad, GpuMem;

        public SensorHub(bool admin)
        {
            IsAdmin = admin;
            try
            {
                _computer = new Computer();
                _computer.IsCpuEnabled = true;
                _computer.IsGpuEnabled = true;
                _computer.Open();
                Ready = true;
                foreach (var hw in _computer.Hardware)
                {
                    if (hw.HardwareType == HardwareType.Cpu) CpuName = hw.Name;
                    else if (IsGpu(hw.HardwareType) && GpuName == "") GpuName = hw.Name;
                }
                Program.Log("sensors ready, admin=" + admin + ", cpu=" + CpuName + ", gpu=" + GpuName);
            }
            catch (Exception ex)
            {
                Error = ex.Message;
                Ready = false;
                Program.Log("sensor init error: " + ex.Message);
            }
        }

        private static bool IsGpu(HardwareType t)
        {
            return t == HardwareType.GpuNvidia || t == HardwareType.GpuAmd || t == HardwareType.GpuIntel;
        }

        public void Refresh()
        {
            CpuTemp = CpuLoad = CpuClock = null;
            GpuTemp = GpuHot = GpuLoad = GpuMem = null;
            if (!Ready) return;
            try
            {
                foreach (var hw in _computer.Hardware)
                {
                    try { hw.Update(); }
                    catch { continue; }

                    if (hw.HardwareType == HardwareType.Cpu)
                    {
                        double? pkg = null, coreMax = null, coreAvg = null;
                        foreach (var s in hw.Sensors)
                        {
                            if (!s.Value.HasValue) continue;
                            float raw = s.Value.Value;
                            if (float.IsNaN(raw) || float.IsInfinity(raw)) continue;
                            double v = raw;

                            if (s.SensorType == SensorType.Temperature)
                            {
                                if (s.Name == "CPU Package") pkg = v;
                                else if (s.Name == "Core Max") coreMax = v;
                                else if (s.Name == "Core Average") coreAvg = v;
                            }
                            else if (s.SensorType == SensorType.Load)
                            {
                                if (s.Name == "CPU Total") CpuLoad = v;
                            }
                            else if (s.SensorType == SensorType.Clock)
                            {
                                if (!CpuClock.HasValue || v > CpuClock.Value) CpuClock = v;
                            }
                        }
                        CpuTemp = pkg ?? coreMax ?? coreAvg;
                    }
                    else if (IsGpu(hw.HardwareType))
                    {
                        foreach (var s in hw.Sensors)
                        {
                            if (!s.Value.HasValue) continue;
                            float raw = s.Value.Value;
                            if (float.IsNaN(raw) || float.IsInfinity(raw)) continue;
                            double v = raw;

                            if (s.SensorType == SensorType.Temperature)
                            {
                                if (s.Name == "GPU Core") GpuTemp = v;
                                else if (s.Name == "GPU Hot Spot") GpuHot = v;
                            }
                            else if (s.SensorType == SensorType.Load)
                            {
                                if (s.Name == "GPU Core") GpuLoad = v;
                                else if (s.Name == "GPU Memory") GpuMem = v;
                            }
                        }
                    }
                }
            }
            catch { }
        }

        public void Dispose()
        {
            try { if (_computer != null) _computer.Close(); } catch { }
        }
    }

    internal sealed class MainForm : Form
    {
        // 布局
        private const int W = 424;
        private const int H = 542;
        private const int HistMax = 60;

        // 配色
        private static readonly Color ColBg = Color.FromArgb(15, 17, 21);
        private static readonly Color ColCard = Color.FromArgb(25, 28, 35);
        private static readonly Color ColCardHi = Color.FromArgb(34, 38, 48);
        private static readonly Color ColLine = Color.FromArgb(40, 45, 55);
        private static readonly Color ColTxt = Color.FromArgb(235, 237, 241);
        private static readonly Color ColDim = Color.FromArgb(132, 138, 148);
        private static readonly Color ColDim2 = Color.FromArgb(92, 98, 108);
        private static readonly Color ColCpu = Color.FromArgb(74, 158, 255);
        private static readonly Color ColGpu = Color.FromArgb(177, 140, 255);
        private static readonly Color ColOk = Color.FromArgb(56, 209, 130);
        private static readonly Color ColWarn = Color.FromArgb(255, 176, 32);
        private static readonly Color ColHot = Color.FromArgb(255, 92, 92);

        // 字体
        private readonly Font _fTitle = new Font("Microsoft YaHei UI", 10.5f, FontStyle.Bold);
        private readonly Font _fTag = new Font("Segoe UI", 9f, FontStyle.Bold);
        private readonly Font _fName = new Font("Microsoft YaHei UI", 8f);
        private readonly Font _fBig = new Font("Segoe UI Light", 50f);
        private readonly Font _fUnit = new Font("Segoe UI", 12f);
        private readonly Font _fInfo = new Font("Microsoft YaHei UI", 8.5f);
        private readonly Font _fSmall = new Font("Microsoft YaHei UI", 8f);
        private readonly Font _fLeg = new Font("Microsoft YaHei UI", 7.5f);
        private readonly Font _fIcon = new Font("Segoe UI", 11f);
        private readonly Font _fAxis = new Font("Segoe UI", 7f);

        private readonly SensorHub _hub;
        private readonly System.Windows.Forms.Timer _timer;
        private readonly List<double?> _histCpu = new List<double?>();
        private readonly List<double?> _histGpu = new List<double?>();

        private string _cpuName = "", _gpuName = "";
        private string _status = "正在读取...";
        private Color _statusColor = ColDim2;
        private Color _dotColor = ColOk;
        private Color? _cpuColor, _gpuColor;

        private bool _pinned, _dragging, _hoverClose, _hoverTop;
        private int _pressed;                  // 0=无 1=关闭 2=置顶
        private Point _dragStart, _winStart;

        // 标题栏按钮命中区：绘制与点击共用同一组坐标，避免"看得见点不着"
        private static readonly Rectangle RcClose = new Rectangle(374, 8, 36, 32);
        private static readonly Rectangle RcTop = new Rectangle(324, 8, 44, 32);

        private readonly string _cfgPath = Path.Combine(Program.AppDir, "settings.txt");

        public MainForm(bool admin)
        {
            _hub = new SensorHub(admin);
            _cpuName = ShortCpu(_hub.CpuName);
            _gpuName = ShortGpu(_hub.GpuName);

            Text = "硬件温度监控";
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(W, H);
            BackColor = ColBg;
            ShowInTaskbar = true;
            KeyPreview = true;
            DoubleBuffered = true;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);

            LoadLayout();

            _timer = new System.Windows.Forms.Timer { Interval = 1000 };
            _timer.Tick += (s, e) => { try { Tick(); } catch (Exception ex) { Program.Log("tick: " + ex.Message); } };
            _timer.Start();

            Tick();
            Program.Log("form constructed, size=" + ClientSize.Width + "x" + ClientSize.Height +
                " cpuTemp=" + (_hub.CpuTemp.HasValue ? _hub.CpuTemp.Value.ToString("0.0") : "null") +
                " gpuTemp=" + (_hub.GpuTemp.HasValue ? _hub.GpuTemp.Value.ToString("0.0") : "null"));
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ClassStyle |= 0x00020000;   // CS_DROPSHADOW
                return cp;
            }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Program.Log("form shown at " + Location.X + "," + Location.Y + " visible=" + Visible + " topmost=" + TopMost);
        }

        private static string ShortCpu(string n)
        {
            if (string.IsNullOrEmpty(n)) return "未识别";
            n = n.Replace("(R)", "").Replace("(TM)", "").Replace("  ", " ").Trim();
            var m = System.Text.RegularExpressions.Regex.Match(n, "(i[3579][-\\s]?\\d{4,5}\\w*)");
            return m.Success ? m.Groups[1].Value : n;
        }

        private static string ShortGpu(string n)
        {
            if (string.IsNullOrEmpty(n)) return "未识别";
            return n.Replace("NVIDIA", "").Replace("GeForce", "").Replace("AMD", "").Trim();
        }

        private void LoadLayout()
        {
            try
            {
                if (File.Exists(_cfgPath))
                {
                    string[] p = File.ReadAllText(_cfgPath).Trim().Split(',');
                    int x, y;
                    if (p.Length >= 2 && int.TryParse(p[0], out x) && int.TryParse(p[1], out y))
                    {
                        var sc = Screen.PrimaryScreen.Bounds;
                        if (x > -20 && y > -20 && x < sc.Width - 100 && y < sc.Height - 100)
                        {
                            StartPosition = FormStartPosition.Manual;
                            Location = new Point(x, y);
                        }
                    }
                }
            }
            catch { }
        }

        private void SaveLayout()
        {
            try { File.WriteAllText(_cfgPath, Location.X + "," + Location.Y); }
            catch { }
        }

        // ---------------- 数据 ----------------
        private void Tick()
        {
            _hub.Refresh();

            _cpuColor = _hub.CpuTemp.HasValue ? TempColor(_hub.CpuTemp) : (Color?)null;
            _gpuColor = _hub.GpuTemp.HasValue ? TempColor(_hub.GpuTemp) : (Color?)null;

            _histCpu.Add(_hub.CpuTemp);
            _histGpu.Add(_hub.GpuTemp);
            while (_histCpu.Count > HistMax) _histCpu.RemoveAt(0);
            while (_histGpu.Count > HistMax) _histGpu.RemoveAt(0);

            UpdateStatus();
            Invalidate();
        }

        private static Color TempColor(double? t)
        {
            if (!t.HasValue) return ColDim2;
            if (t.Value >= 85) return ColHot;
            if (t.Value >= 70) return ColWarn;
            return ColOk;
        }

        private static string TempWord(double? t)
        {
            if (!t.HasValue) return "无数据";
            if (t.Value >= 90) return "过热";
            if (t.Value >= 80) return "很热";
            if (t.Value >= 70) return "偏热";
            if (t.Value >= 50) return "正常";
            return "凉爽";
        }

        private void UpdateStatus()
        {
            double? tmax = _hub.CpuTemp;
            if (_hub.GpuTemp.HasValue && (!tmax.HasValue || _hub.GpuTemp.Value > tmax.Value)) tmax = _hub.GpuTemp;

            if (!_hub.Ready)
            {
                _status = "传感器库加载失败";
                _statusColor = ColHot;
                _dotColor = ColHot;
            }
            else if (!_hub.CpuTemp.HasValue && !_hub.IsAdmin)
            {
                _status = "CPU 温度需管理员权限  ·  显卡温度可正常查看";
                _statusColor = ColWarn;
                _dotColor = ColWarn;
            }
            else if (!_hub.CpuTemp.HasValue)
            {
                _status = "未读到 CPU 温度传感器  ·  管理员模式";
                _statusColor = ColWarn;
                _dotColor = ColWarn;
            }
            else if (tmax.HasValue && tmax.Value >= 85)
            {
                _status = "温度偏高，注意散热";
                _statusColor = ColHot;
                _dotColor = ColHot;
            }
            else if (tmax.HasValue && tmax.Value >= 70)
            {
                _status = "温度偏高";
                _statusColor = ColWarn;
                _dotColor = ColWarn;
            }
            else
            {
                _status = "运行正常  ·  " + (_hub.IsAdmin ? "管理员模式" : "普通模式");
                _statusColor = ColDim2;
                _dotColor = ColOk;
            }
        }

        // ---------------- 绘制 ----------------
        private static GraphicsPath RoundRect(RectangleF r, float rad)
        {
            var p = new GraphicsPath();
            float d = rad * 2;
            p.AddArc(r.Left, r.Top, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Top, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        private static StringFormat SF(ContentAlignment align)
        {
            var f = new StringFormat(StringFormat.GenericTypographic);
            f.FormatFlags |= StringFormatFlags.NoWrap;
            switch (align)
            {
                case ContentAlignment.MiddleRight:
                case ContentAlignment.TopRight:
                    f.Alignment = StringAlignment.Far; break;
                case ContentAlignment.MiddleCenter:
                case ContentAlignment.TopCenter:
                    f.Alignment = StringAlignment.Center; break;
                default:
                    f.Alignment = StringAlignment.Near; break;
            }
            switch (align)
            {
                case ContentAlignment.MiddleLeft:
                case ContentAlignment.MiddleCenter:
                case ContentAlignment.MiddleRight:
                    f.LineAlignment = StringAlignment.Center; break;
                default:
                    f.LineAlignment = StringAlignment.Near; break;
            }
            return f;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;

            using (var b = new SolidBrush(ColBg)) g.FillRectangle(b, ClientRectangle);
            using (var p = new Pen(ColLine)) g.DrawRectangle(p, 0, 0, Width - 1, Height - 1);

            // header
            using (var b = new SolidBrush(_dotColor)) g.FillEllipse(b, 10, 21, 8, 8);
            using (var b = new SolidBrush(ColTxt))
                g.DrawString("硬件温度监控", _fTitle, b, new RectangleF(24, 12, 220, 26), SF(ContentAlignment.MiddleLeft));

            // 置顶按钮
            if (_hoverTop || _pressed == 2)
                using (var path = RoundRect(new RectangleF(RcTop.X, RcTop.Y, RcTop.Width, RcTop.Height), 6))
                using (var b = new SolidBrush(_pressed == 2 ? Color.FromArgb(28, 74, 158, 255) : ColCardHi))
                    g.FillPath(b, path);
            using (var b = new SolidBrush(_hoverTop ? ColTxt : (_pinned ? ColCpu : ColDim2)))
                g.DrawString("置顶", _fSmall, b,
                    new RectangleF(RcTop.X, RcTop.Y, RcTop.Width, RcTop.Height), SF(ContentAlignment.MiddleCenter));

            // 关闭按钮
            if (_hoverClose || _pressed == 1)
                using (var path = RoundRect(new RectangleF(RcClose.X, RcClose.Y, RcClose.Width, RcClose.Height), 6))
                using (var b = new SolidBrush(_pressed == 1 ? Color.FromArgb(70, 255, 92, 92) : ColCardHi))
                    g.FillPath(b, path);
            using (var b = new SolidBrush(_hoverClose ? ColHot : ColDim))
                g.DrawString("✕", _fIcon, b,
                    new RectangleF(RcClose.X, RcClose.Y, RcClose.Width, RcClose.Height), SF(ContentAlignment.MiddleCenter));

            // cards
            DrawTempCard(g, 58, "CPU", ColCpu, _cpuName, _hub.CpuTemp, _cpuColor, BuildCpuInfo());
            DrawTempCard(g, 218, "GPU", ColGpu, _gpuName, _hub.GpuTemp, _gpuColor, BuildGpuInfo());

            DrawTrend(g);

            using (var b = new SolidBrush(_statusColor))
                g.DrawString(_status, _fSmall, b, new RectangleF(20, 508, 280, 20), SF(ContentAlignment.MiddleLeft));

            using (var b = new SolidBrush(ColDim2))
                g.DrawString("更新于 " + DateTime.Now.ToString("HH:mm:ss"), _fSmall, b,
                    new RectangleF(W - 170, 508, 150, 20), SF(ContentAlignment.MiddleRight));
        }

        private string BuildCpuInfo()
        {
            var parts = new List<string>();
            if (_hub.CpuLoad.HasValue) parts.Add("负载 " + Math.Round(_hub.CpuLoad.Value).ToString("0") + "%");
            if (_hub.CpuClock.HasValue && _hub.CpuClock.Value > 0)
                parts.Add((_hub.CpuClock.Value / 1000.0).ToString("0.00") + " GHz");
            if (parts.Count == 0) parts.Add(_hub.CpuTemp.HasValue ? "—" : "传感器未就绪");
            return string.Join("   ·   ", parts.ToArray());
        }

        private string BuildGpuInfo()
        {
            var parts = new List<string>();
            if (_hub.GpuLoad.HasValue) parts.Add("负载 " + Math.Round(_hub.GpuLoad.Value).ToString("0") + "%");
            if (_hub.GpuHot.HasValue) parts.Add("热点 " + Math.Round(_hub.GpuHot.Value).ToString("0") + "°C");
            if (_hub.GpuMem.HasValue) parts.Add("显存 " + Math.Round(_hub.GpuMem.Value).ToString("0") + "%");
            if (parts.Count == 0) parts.Add(_hub.GpuTemp.HasValue ? "—" : "传感器未就绪");
            return string.Join("   ·   ", parts.ToArray());
        }

        private void DrawTempCard(Graphics g, int y, string tag, Color accent, string devName,
            double? temp, Color? tempColor, string info)
        {
            var rect = new RectangleF(18, y, W - 36, 152);
            using (var path = RoundRect(rect, 10))
            using (var b = new SolidBrush(ColCard))
                g.FillPath(b, path);

            // 左侧色条
            using (var p = new Pen(accent, 3f))
            {
                p.StartCap = LineCap.Round;
                p.EndCap = LineCap.Round;
                g.DrawLine(p, 18.5f, y + 16, 18.5f, y + 136);
            }

            using (var b = new SolidBrush(accent))
                g.DrawString(tag, _fTag, b, new RectangleF(34, y + 10, 60, 20), SF(ContentAlignment.MiddleLeft));

            using (var b = new SolidBrush(ColDim))
                g.DrawString(devName, _fName, b, new RectangleF(34, y + 10, W - 36 - 50, 20), SF(ContentAlignment.MiddleRight));

            // 大温度
            string txt = temp.HasValue ? Math.Round(temp.Value).ToString("0") : "--";
            Color c = tempColor ?? ColDim2;
            SizeF sz;
            using (var b = new SolidBrush(c))
            {
                sz = g.MeasureString(txt, _fBig, PointF.Empty, StringFormat.GenericTypographic);
                g.DrawString(txt, _fBig, b, new PointF(34, y + 26), StringFormat.GenericTypographic);
            }
            using (var b = new SolidBrush(ColDim2))
                g.DrawString("°C", _fUnit, b, new PointF(34 + sz.Width - 2, y + 62), StringFormat.GenericTypographic);

            using (var b = new SolidBrush(temp.HasValue ? c : ColWarn))
                g.DrawString(TempWord(temp), _fInfo, b,
                    new RectangleF(34, y + 56, W - 36 - 50, 22), SF(ContentAlignment.MiddleRight));

            using (var b = new SolidBrush(ColDim))
                g.DrawString(info, _fInfo, b, new RectangleF(34, y + 118, W - 36 - 52, 22), SF(ContentAlignment.MiddleLeft));
        }

        private void DrawTrend(Graphics g)
        {
            var cardRect = new RectangleF(18, 378, W - 36, 122);
            using (var path = RoundRect(cardRect, 10))
            using (var b = new SolidBrush(ColCard))
                g.FillPath(b, path);

            using (var b = new SolidBrush(ColDim2))
                g.DrawString("温度趋势（60 秒）", _fLeg, b, new RectangleF(34, 386, 160, 16), SF(ContentAlignment.MiddleLeft));

            using (var b = new SolidBrush(ColCpu))
                g.DrawString("● CPU", _fLeg, b, new RectangleF(W - 36 - 96, 386, 48, 16), SF(ContentAlignment.MiddleRight));
            using (var b = new SolidBrush(ColGpu))
                g.DrawString("● GPU", _fLeg, b, new RectangleF(W - 36 - 46, 386, 48, 16), SF(ContentAlignment.MiddleRight));

            var plot = new RectangleF(34, 408, W - 36 - 44, 78);
            DrawPlot(g, plot);
        }

        private void DrawPlot(Graphics g, RectangleF plot)
        {
            var vals = new List<double>();
            foreach (var v in _histCpu) if (v.HasValue) vals.Add(v.Value);
            foreach (var v in _histGpu) if (v.HasValue) vals.Add(v.Value);

            double lo = 30, hi = 90;
            if (vals.Count > 0)
            {
                double mn = vals.Min(), mx = vals.Max();
                lo = Math.Floor((mn - 4) / 10.0) * 10;
                hi = Math.Ceiling((mx + 4) / 10.0) * 10;
                if (hi - lo < 20) { double mid = (hi + lo) / 2; lo = mid - 10; hi = mid + 10; }
                if (lo < 0) lo = 0;
            }
            double span = hi - lo;
            if (span <= 0) span = 10;

            double step = span > 60 ? 20 : 10;

            using (var gridPen = new Pen(Color.FromArgb(30, 255, 255, 255), 1f))
            using (var axisBrush = new SolidBrush(ColDim2))
            {
                for (double t = lo; t <= hi + 0.001; t += step)
                {
                    float yy = (float)(plot.Bottom - (t - lo) / span * plot.Height);
                    g.DrawLine(gridPen, plot.Left, yy, plot.Right, yy);
                    g.DrawString(((int)Math.Round(t)).ToString(), _fAxis, axisBrush,
                        new RectangleF(plot.Left - 26, yy - 7, 22, 14), SF(ContentAlignment.MiddleRight));
                }
            }

            DrawSeries(g, _histCpu, ColCpu, plot, lo, hi);
            DrawSeries(g, _histGpu, ColGpu, plot, lo, hi);
        }

        private void DrawSeries(Graphics g, List<double?> data, Color color, RectangleF plot, double lo, double hi)
        {
            if (data.Count < 2) return;
            double span = hi - lo;
            if (span <= 0) span = 10;

            var pts = new List<PointF>();
            for (int i = 0; i < data.Count; i++)
            {
                if (!data[i].HasValue) continue;
                float x = plot.Left + (i / (float)(HistMax - 1)) * plot.Width;
                double v = Math.Max(lo, Math.Min(hi, data[i].Value));
                float y = (float)(plot.Bottom - (v - lo) / span * plot.Height);
                pts.Add(new PointF(x, y));
            }

            using (var pen = new Pen(color, 1.9f))
            {
                pen.LineJoin = LineJoin.Round;
                if (pts.Count >= 2) g.DrawLines(pen, pts.ToArray());
            }
            if (pts.Count >= 1)
            {
                var last = pts[pts.Count - 1];
                using (var b = new SolidBrush(color))
                    g.FillEllipse(b, last.X - 2.2f, last.Y - 2.2f, 4.4f, 4.4f);
            }
        }

        // ---------------- 交互 ----------------
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;

            // 先判断按钮，命中按钮就不进入拖动状态
            if (RcClose.Contains(e.Location)) { _pressed = 1; Invalidate(); return; }
            if (RcTop.Contains(e.Location)) { _pressed = 2; Invalidate(); return; }

            if (e.Y <= 50)
            {
                _dragging = true;
                _dragStart = Cursor.Position;
                _winStart = Location;
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);

            bool hc = RcClose.Contains(e.Location);
            bool ht = RcTop.Contains(e.Location);
            if (hc != _hoverClose || ht != _hoverTop)
            {
                _hoverClose = hc;
                _hoverTop = ht;
                Cursor = (hc || ht) ? Cursors.Hand : Cursors.Default;
                Invalidate();
            }

            if (_dragging)
            {
                var p = Cursor.Position;
                Location = new Point(_winStart.X + p.X - _dragStart.X, _winStart.Y + p.Y - _dragStart.Y);
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left) return;

            bool wasDragging = _dragging;
            int pressed = _pressed;
            _dragging = false;
            _pressed = 0;

            if (wasDragging)
            {
                SaveLayout();
                Invalidate();
                return;
            }

            // 按下和松开都落在同一个按钮里才算点击
            if (pressed == 1 && RcClose.Contains(e.Location)) { Close(); return; }
            if (pressed == 2 && RcTop.Contains(e.Location))
            {
                _pinned = !_pinned;
                TopMost = _pinned;
                Invalidate();
                return;
            }

            Invalidate();
            SaveLayout();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_pressed != 0) { _pressed = 0; Invalidate(); }
            if (_hoverClose || _hoverTop) { _hoverClose = _hoverTop = false; Invalidate(); }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Escape) Close();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            try { _timer.Stop(); } catch { }
            SaveLayout();
            try { _hub.Dispose(); } catch { }
            Program.Log("closed");
            base.OnFormClosing(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                try { _timer.Dispose(); } catch { }
                _fTitle.Dispose(); _fTag.Dispose(); _fName.Dispose(); _fBig.Dispose();
                _fUnit.Dispose(); _fInfo.Dispose(); _fSmall.Dispose(); _fLeg.Dispose();
                _fIcon.Dispose(); _fAxis.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
