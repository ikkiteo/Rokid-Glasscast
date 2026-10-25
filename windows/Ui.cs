using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace GlassCast
{
    sealed class GlassCanvas : Form
    {
        public GlassCanvas(int w, int h)
        {
            Text = "GlassCast — холст очков";
            FormBorderStyle = FormBorderStyle.Sizable;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(32, 56, 40);
            ForeColor = Color.FromArgb(53, 232, 160);
            ClientSize = new Size(Math.Max(320, w), Math.Max(320, h));
            DoubleBuffered = true;
            TopMost = true;
            var hint = new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Color.FromArgb(53, 232, 160),
                Font = new Font("Consolas", 12f),
                Text = "это запасной холст, не монитор Windows\n\nдля отдельного экрана нажми «драйвер дисплея»,\nзатем источник «виртуальный дисплей» —\nмонитор включится только на время трансляции\n\nF11 разворачивает этот холст на текущий монитор"
            };
            Controls.Add(hint);
            KeyPreview = true;
            KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.F11)
                {
                    if (FormBorderStyle == FormBorderStyle.None)
                    {
                        FormBorderStyle = FormBorderStyle.Sizable;
                        WindowState = FormWindowState.Normal;
                    }
                    else
                    {
                        FormBorderStyle = FormBorderStyle.None;
                        WindowState = FormWindowState.Maximized;
                    }
                }
            };
        }
    }

    sealed class MainForm : Form
    {
        readonly ComboBox glasses = new ComboBox();
        readonly TextBox ip = new TextBox();
        readonly ComboBox source = new ComboBox();
        readonly ComboBox monitors = new ComboBox();
        readonly ComboBox windows = new ComboBox();
        readonly ComboBox size = new ComboBox();
        readonly ComboBox fps = new ComboBox();
        readonly TrackBar bitrate = new TrackBar();
        readonly Label bitrateLbl = new Label();
        readonly Label zoneLbl = new Label();
        readonly Label status = new Label();
        readonly Label stats = new Label();
        readonly Button start = new Button();
        readonly Button pickZone = new Button();
        readonly Button refreshWin = new Button();
        readonly Streamer live = new Streamer();
        bool streamWasOn;
        readonly Timer tick = new Timer();
        GlassCanvas canvas;
        List<Found> found = new List<Found>();
        List<Monitor> screens = new List<Monitor>();
        List<AppWindow> apps = new List<AppWindow>();
        Rectangle zone = Rectangle.Empty;

        public MainForm()
        {
            Text = "GlassCast";
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(540, 720);
            BackColor = Color.FromArgb(7, 16, 24);
            ForeColor = Color.FromArgb(215, 246, 234);
            Font = new Font("Consolas", 10f);

            int y = 14;
            Add(Title("GLASSCAST"), 16, y, 500, 28); y += 26;
            Add(Muted("экран компьютера → очки"), 16, y, 500, 18); y += 28;

            Button scan = Action("найти очки");
            scan.Click += (s, e) => Scan();
            Add(scan, 16, y, 150, 32);
            StyleBox(ip);
            Add(Muted("IP"), 176, y - 16, 40, 16);
            Add(ip, 176, y, 210, 28);
            Button use = Action("взять");
            use.Click += (s, e) => PickTyped();
            Add(use, 396, y, 128, 32);
            y += 44;

            StyleBox(glasses);
            glasses.DropDownStyle = ComboBoxStyle.DropDownList;
            glasses.SelectedIndexChanged += (s, e) =>
            {
                int i = glasses.SelectedIndex;
                if (i >= 0 && i < found.Count) ip.Text = found[i].Host;
            };
            Add(glasses, 16, y, 508, 28); y += 40;

            Add(Muted("источник"), 16, y, 200, 16); y += 18;
            StyleBox(source);
            source.DropDownStyle = ComboBoxStyle.DropDownList;
            source.Items.AddRange(new object[] {
                "монитор",
                "зона экрана",
                "окно программы",
                "холст очков",
                "виртуальный дисплей GlassCast"
            });
            source.SelectedIndex = 0;
            source.SelectedIndexChanged += (s, e) => SyncSource();
            Add(source, 16, y, 508, 28); y += 40;

            StyleBox(monitors);
            monitors.DropDownStyle = ComboBoxStyle.DropDownList;
            Add(Muted("монитор"), 16, y, 200, 16); y += 18;
            Add(monitors, 16, y, 508, 28); y += 40;

            pickZone.Text = "выделить зону";
            StyleAction(pickZone);
            pickZone.Click += (s, e) => PickZone();
            Add(pickZone, 16, y, 180, 32);
            zoneLbl.ForeColor = Color.FromArgb(122, 168, 154);
            zoneLbl.Text = "зона не выбрана";
            Add(zoneLbl, 206, y, 318, 32); y += 40;

            StyleBox(windows);
            windows.DropDownStyle = ComboBoxStyle.DropDownList;
            refreshWin.Text = "окна";
            StyleAction(refreshWin);
            refreshWin.Click += (s, e) => ReloadWindows();
            Add(Muted("окно программы"), 16, y, 200, 16); y += 18;
            Add(windows, 16, y, 390, 28);
            Add(refreshWin, 414, y, 110, 28); y += 40;

            Button open = Action("открыть холст");
            open.Click += (s, e) => OpenCanvas();
            Add(open, 16, y, 250, 32);
            Button idd = Action("драйвер дисплея");
            idd.Click += (s, e) => InstallIdd();
            Add(idd, 274, y, 250, 32); y += 44;

            StyleBox(size);
            size.DropDownStyle = ComboBoxStyle.DropDownList;
            size.Items.AddRange(new object[] {
                "2× панель очков",
                "1280×720",
                "чёткий (до 1280)",
                "полный монитор"
            });
            size.SelectedIndex = 0;
            StyleBox(fps);
            fps.DropDownStyle = ComboBoxStyle.DropDownList;
            fps.Items.AddRange(new object[] { "30", "60", "90" });
            fps.SelectedIndex = 0;
            Add(Muted("размер потока"), 16, y, 200, 16);
            Add(Muted("к/с"), 300, y, 80, 16); y += 18;
            Add(size, 16, y, 270, 28);
            Add(fps, 300, y, 80, 28); y += 44;

            bitrate.Minimum = 2; bitrate.Maximum = 20; bitrate.Value = 6;
            bitrate.TickFrequency = 2;
            bitrate.BackColor = BackColor;
            bitrate.ValueChanged += (s, e) => bitrateLbl.Text = "качество JPEG  " + bitrate.Value;
            bitrateLbl.Text = "качество JPEG  6";
            bitrateLbl.ForeColor = Color.FromArgb(122, 168, 154);
            Add(bitrateLbl, 16, y, 300, 16); y += 18;
            Add(bitrate, 16, y, 508, 40); y += 44;

            start.Text = "транслировать";
            StyleAction(start);
            start.Click += (s, e) => Toggle();
            Add(start, 16, y, 508, 36); y += 48;

            status.ForeColor = Color.FromArgb(53, 232, 160);
            status.Text = "найди очки или введи IP";
            Add(status, 16, y, 508, 36); y += 36;
            stats.ForeColor = Color.FromArgb(122, 168, 154);
            Add(stats, 16, y, 508, 22); y += 26;
            Add(Muted("Виртуальный дисплей включается только на время трансляции и гаснет, когда она останавливается."), 16, y, 508, 36);

            tick.Interval = 400;
            tick.Tick += (s, e) =>
            {
                if (streamWasOn && !live.Running && Screens.Glass() != null)
                {
                    string off = IddSetup.SetVirtual(false);
                    ReloadMonitors();
                    status.Text = off ?? "остановлено";
                }
                streamWasOn = live.Running;
                if (live.Running)
                {
                    status.Text = live.Status;
                    stats.Text = live.Fps + " к/с  ·  " + live.Kbps + " кбит/с  ·  кадров " + live.Frames;
                }
                else
                {
                    stats.Text = "";
                    if (live.Status != "готов" && live.Status != "остановлено")
                        status.Text = live.Status;
                }
                start.Text = live.Running ? "стоп" : "транслировать";
            };
            tick.Start();

            Load += (s, e) => { ReloadMonitors(); ReloadWindows(); SyncSource(); };
            Shown += (s, e) =>
            {
                if (Screens.Glass() == null) return;
                status.Text = "выключаю виртуальный экран…";
                Refresh();
                string off = IddSetup.SetVirtual(false);
                ReloadMonitors();
                SyncSource();
                status.Text = off ?? "виртуальный экран выключен — включится на время трансляции";
            };
            FormClosed += (s, e) =>
            {
                live.Stop();
                try { if (Screens.Glass() != null) IddSetup.SetVirtual(false); } catch { }
                if (canvas != null) canvas.Close();
            };
        }

        void SyncSource()
        {
            int i = source.SelectedIndex;
            monitors.Enabled = i == 0;
            pickZone.Enabled = i == 1;
            windows.Enabled = i == 2;
            refreshWin.Enabled = i == 2;
            if (i == 2 && windows.Items.Count == 0) ReloadWindows();
            if (i == 3 && (canvas == null || canvas.IsDisposed)) OpenCanvas();
            if (i == 4) PickGlassMonitor();
        }

        void Scan()
        {
            status.Text = "ищу очки…";
            Refresh();
            found = Discover.Scan(3000, ip.Text);
            glasses.Items.Clear();
            foreach (var g in found) glasses.Items.Add(g);
            if (found.Count == 0) status.Text = "очки не ответили · введи IP с экрана очков";
            else
            {
                glasses.SelectedIndex = 0;
                ip.Text = found[0].Host;
                status.Text = "нашёл " + found.Count;
            }
        }

        void PickTyped()
        {
            if (ip.Text.Trim().Length == 0) return;
            status.Text = "беру " + ip.Text.Trim();
        }

        void ReloadMonitors()
        {
            screens = Screens.All();
            monitors.Items.Clear();
            foreach (var m in screens) monitors.Items.Add(m);
            if (monitors.Items.Count > 0) monitors.SelectedIndex = 0;
        }

        void ReloadWindows()
        {
            IntPtr keep = IntPtr.Zero;
            AppWindow cur = windows.SelectedItem as AppWindow;
            if (cur != null) keep = cur.Handle;
            apps = AppWindows.Visible();
            windows.Items.Clear();
            int pick = -1;
            for (int i = 0; i < apps.Count; i++)
            {
                if (canvas != null && apps[i].Handle == canvas.Handle) continue;
                if (apps[i].Handle == Handle) continue;
                windows.Items.Add(apps[i]);
                if (apps[i].Handle == keep) pick = windows.Items.Count - 1;
            }
            if (windows.Items.Count > 0)
                windows.SelectedIndex = pick >= 0 ? pick : 0;
            status.Text = "окон " + windows.Items.Count;
        }

        void PickZone()
        {
            WindowState = FormWindowState.Minimized;
            try
            {
                using (var dlg = new RegionPick())
                {
                    if (dlg.ShowDialog() == DialogResult.OK)
                    {
                        zone = dlg.Picked;
                        zoneLbl.Text = zone.Width + "×" + zone.Height + "  @ " + zone.X + "," + zone.Y;
                        source.SelectedIndex = 1;
                        status.Text = "зона " + zoneLbl.Text;
                    }
                }
            }
            finally { WindowState = FormWindowState.Normal; }
        }

        void OpenCanvas()
        {
            Size s = EncodeSize(SourceRect());
            if (canvas != null) { try { canvas.Close(); } catch { } }
            Found g = glasses.SelectedItem as Found;
            int cw = g != null && g.Width > 0 ? g.Width : 480;
            int ch = g != null && g.Height > 0 ? g.Height : 640;
            canvas = new GlassCanvas(cw, ch);
            canvas.FormClosed += (s2, e2) => { if (canvas == s2) canvas = null; };
            canvas.Show(this);
            source.SelectedIndex = 3;
            status.Text = "холст открыт · F11 на весь монитор · это не виртуальный дисплей Windows";
        }

        Rectangle SourceRect()
        {
            int i = source.SelectedIndex;
            if (i == 1 && zone.Width > 8 && zone.Height > 8) return zone;
            if (i == 2)
            {
                AppWindow w = windows.SelectedItem as AppWindow;
                if (w != null) return AppWindows.Bounds(w.Handle);
            }
            if (i == 3 && canvas != null && !canvas.IsDisposed)
            {
                IntPtr hwnd = canvas.Handle;
                return AppWindows.Bounds(hwnd);
            }
            if (i == 4)
            {
                Monitor g = Screens.Glass();
                if (g != null) return g.Bounds;
            }
            int mon = monitors.SelectedIndex;
            if (mon < 0) mon = 0;
            if (screens.Count > 0 && mon < screens.Count) return screens[mon].Bounds;
            return Screen.PrimaryScreen.Bounds;
        }

        void PickGlassMonitor()
        {
            ReloadMonitors();
            Monitor g = Screens.Glass();
            if (g != null)
            {
                for (int i = 0; i < screens.Count; i++)
                {
                    if (screens[i].Name == g.Name)
                    {
                        monitors.SelectedIndex = i;
                        status.Text = "виртуальный дисплей: " + screens[i];
                        return;
                    }
                }
            }
            status.Text = IddSetup.Status();
        }

        void InstallIdd()
        {
            status.Text = "устанавливаю драйвер…";
            Refresh();
            try
            {
                var p = Process.Start(new ProcessStartInfo
                {
                    FileName = Application.ExecutablePath,
                    Arguments = "--install-idd",
                    UseShellExecute = true,
                    Verb = "runas"
                });
                if (p != null) p.WaitForExit(60000);
            }
            catch (Exception e)
            {
                status.Text = e.Message;
                return;
            }
            ReloadMonitors();
            PickGlassMonitor();
            source.SelectedIndex = 4;
        }

        void Toggle()
        {
            if (live.Running)
            {
                live.Stop();
                string off = Screens.Glass() != null ? IddSetup.SetVirtual(false) : null;
                ReloadMonitors();
                status.Text = off ?? "остановлено";
                streamWasOn = false;
                return;
            }
            string host = ip.Text.Trim();
            if (host.Length == 0)
            {
                status.Text = "нет адреса очков";
                return;
            }
            int srcMode = source.SelectedIndex;
            if (srcMode == 1 && (zone.Width < 32 || zone.Height < 32))
            {
                status.Text = "сначала выдели зону";
                return;
            }
            if (srcMode == 2 && windows.SelectedItem == null)
            {
                status.Text = "выбери окно программы";
                return;
            }
            if (srcMode == 3 && (canvas == null || canvas.IsDisposed))
            {
                OpenCanvas();
                if (canvas == null) return;
            }
            if (srcMode == 4)
            {
                status.Text = "включаю виртуальный экран…";
                Refresh();
                string on = IddSetup.SetVirtual(true);
                if (on != null) { status.Text = on; return; }
                ReloadMonitors();
                if (Screens.Glass() == null) { status.Text = "виртуальный экран не появился"; return; }
            }
            else if (Screens.Glass() != null)
            {
                IddSetup.SetVirtual(false);
                ReloadMonitors();
            }

            Found g = glasses.SelectedItem as Found;
            Rectangle bounds = SourceRect();
            Size enc = EncodeSize(bounds);
            int f = int.Parse((string)fps.SelectedItem);
            int br = bitrate.Value * 1000 * 1000;
            int rotate = 0;

            IntPtr winHwnd = IntPtr.Zero;
            if (srcMode == 2)
            {
                AppWindow w = windows.SelectedItem as AppWindow;
                if (w != null) winHwnd = w.Handle;
            }
            else if (srcMode == 3)
            {
                canvas.Invoke(new Action(() => winHwnd = canvas.Handle));
            }

            live.Start(() =>
            {
                Grabber grab = null;
                try
                {
                    if (winHwnd != IntPtr.Zero)
                        grab = new Grabber(winHwnd, enc.Width, enc.Height);
                    else
                        grab = new Grabber(bounds, enc.Width, enc.Height);
                    int port = g != null && g.Port > 0 ? g.Port : 47810;
                    TcpCast.Run(
                        live, host, port,
                        "pc", Environment.MachineName,
                        grab.Width, grab.Height, f, br, rotate,
                        grab);
                }
                finally
                {
                    if (grab != null) grab.Dispose();
                }
            });
            streamWasOn = srcMode == 4;
        }

        Size EncodeSize(Rectangle src)
        {
            Found g = glasses.SelectedItem as Found;
            int pw = g != null && g.Width > 0 ? g.Width : 480;
            int ph = g != null && g.Height > 0 ? g.Height : 640;
            int sw = Math.Max(16, src.Width);
            int sh = Math.Max(16, src.Height);
            switch (size.SelectedIndex)
            {
                case 0:
                    return new Size(Native.Align(pw * 2, 16), Native.Align(ph * 2, 16));
                case 1:
                    return Fit(sw, sh, 1280, 720);
                case 2:
                    return Fit(sw, sh, 1280, 1280);
                default:
                    return Fit(sw, sh, 1600, 1600);
            }
        }

        static Size Fit(int sw, int sh, int mw, int mh)
        {
            float s = Math.Min(mw / (float)sw, mh / (float)sh);
            return new Size(Native.Align((int)(sw * s), 16), Native.Align((int)(sh * s), 16));
        }

        void Add(Control c, int x, int y, int w, int h)
        {
            c.SetBounds(x, y, w, h);
            Controls.Add(c);
        }

        Label Title(string t)
        {
            return new Label { Text = t, ForeColor = Color.FromArgb(53, 232, 160), Font = new Font("Consolas", 18f, FontStyle.Bold) };
        }

        Label Muted(string t)
        {
            return new Label { Text = t, ForeColor = Color.FromArgb(122, 168, 154) };
        }

        Button Action(string t)
        {
            var b = new Button { Text = t };
            StyleAction(b);
            return b;
        }

        void StyleAction(Button b)
        {
            b.FlatStyle = FlatStyle.Flat;
            b.BackColor = Color.FromArgb(53, 232, 160);
            b.ForeColor = Color.FromArgb(7, 16, 24);
            b.FlatAppearance.BorderSize = 0;
            b.Font = new Font("Consolas", 10f, FontStyle.Bold);
        }

        void StyleBox(Control c)
        {
            c.BackColor = Color.FromArgb(14, 28, 36);
            c.ForeColor = Color.FromArgb(215, 246, 234);
        }
    }

    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            try { Native.SetProcessDPIAware(); } catch { }
            if (args != null && args.Length > 0 && args[0] == "--dxtest")
            {
                var sb = new System.Text.StringBuilder();
                try
                {
                    foreach (Monitor m in Screens.All()) sb.AppendLine(DxDesk.Probe(m));
                }
                catch (Exception e) { sb.AppendLine(e.ToString()); }
                string path = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "dxtest.txt");
                System.IO.File.WriteAllText(path, sb.ToString());
                return;
            }
            if (args != null && args.Length > 0 && args[0] == "--install-idd")
            {
                try
                {
                    string msg = IddSetup.Install();
                    MessageBox.Show(msg, "GlassCast");
                }
                catch (Exception e)
                {
                    MessageBox.Show(e.Message, "GlassCast");
                }
                return;
            }
            if (args != null && args.Length > 0 && args[0] == "--selftest")
            {
                SelfTest();
                return;
            }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }

        static void SelfTest()
        {
            string path = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "selftest.txt");
            try
            {
                var log = new System.Text.StringBuilder();
                Mf.CoInitializeEx(IntPtr.Zero, 2);
                int mf = Mf.MFStartup(Mf.MF_VERSION, 0);
                log.AppendLine("MFStartup 0x" + mf.ToString("X"));
                var mon = Screens.All()[0];
                using (var grab = new Grabber(mon.Bounds, 320, 180))
                using (var enc = new JpegEnc(320, 180, 70))
                {
                    IntPtr hdc = enc.GetHdc();
                    try { grab.Blit(hdc); }
                    finally { enc.ReleaseHdc(hdc); }
                    byte[] au = enc.Encode();
                    log.AppendLine("jpeg " + (au == null ? 0 : au.Length) + " bytes from " + mon.Name);
                    System.IO.File.WriteAllText(path, log.ToString());
                    if (au == null || au.Length < 100) Environment.Exit(2);
                }
            }
            catch (Exception e)
            {
                string extra = "";
                try { extra = System.IO.File.ReadAllText(path); } catch { }
                System.IO.File.WriteAllText(path, extra + "\r\n" + H264Encoder.LastCreateNote + "\r\n" + e);
                Environment.Exit(1);
            }
        }
    }
}
