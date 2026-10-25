using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;

namespace GlassCast
{
    sealed class Monitor
    {
        public string Name;
        public string Friendly;
        public Rectangle Bounds;
        public bool Primary;
        public override string ToString()
        {
            string tag = Primary ? Lang.T("основной", "primary") : Lang.T("доп.", "extra");
            string label = string.IsNullOrEmpty(Friendly) ? Name : Friendly;
            return label + "  " + Bounds.Width + "×" + Bounds.Height + "  " + tag;
        }
    }

    sealed class AppWindow
    {
        public IntPtr Handle;
        public string Title;
        public override string ToString() { return Title; }
    }

    static class Screens
    {
        public static bool IsGlass(Monitor m)
        {
            if (m == null) return false;
            string n = ((m.Friendly ?? "") + " " + (m.Name ?? "")).ToLowerInvariant();
            return n.IndexOf("glasscast") >= 0 || n.IndexOf("glass cast") >= 0
                || n.IndexOf("mobile monitor") >= 0 || n.IndexOf("usbmm") >= 0
                || n.IndexOf("amyuni") >= 0 || n.IndexOf("virtual display") >= 0;
        }

        public static Monitor Glass()
        {
            foreach (Monitor m in All()) if (IsGlass(m)) return m;
            return null;
        }

        public static List<Monitor> All()
        {
            var list = new List<Monitor>();
            Dictionary<string, string> names = Native.MonitorNames();
            Native.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr h, IntPtr hdc, ref Native.RECT r, IntPtr data) =>
            {
                var info = new Native.MONITORINFOEX();
                info.Size = Marshal.SizeOf(typeof(Native.MONITORINFOEX));
                if (!Native.GetMonitorInfo(h, ref info)) return true;
                var b = info.Monitor;
                string friendly = null;
                if (!string.IsNullOrEmpty(info.Device)) names.TryGetValue(info.Device, out friendly);
                list.Add(new Monitor
                {
                    Name = string.IsNullOrEmpty(info.Device) ? "DISPLAY" : info.Device.Replace("\\\\.\\", ""),
                    Friendly = friendly,
                    Bounds = Rectangle.FromLTRB(b.Left, b.Top, b.Right, b.Bottom),
                    Primary = (info.Flags & Native.MONITORINFOF_PRIMARY) != 0
                });
                return true;
            }, IntPtr.Zero);
            if (list.Count == 0)
            {
                var s = System.Windows.Forms.Screen.PrimaryScreen.Bounds;
                list.Add(new Monitor { Name = "DISPLAY", Bounds = s, Primary = true });
            }
            return list;
        }
    }

    static class AppWindows
    {
        public static List<AppWindow> Visible()
        {
            var list = new List<AppWindow>();
            Native.EnumWindows((hwnd, l) =>
            {
                try
                {
                    if (!Native.IsWindowVisible(hwnd) || Native.IsIconic(hwnd)) return true;
                    int n = Native.GetWindowTextLength(hwnd);
                    if (n <= 0) return true;
                    var title = new StringBuilder(n + 1);
                    Native.GetWindowText(hwnd, title, title.Capacity);
                    if (title.Length == 0) return true;
                    var cls = new StringBuilder(64);
                    Native.GetClassName(hwnd, cls, cls.Capacity);
                    string c = cls.ToString();
                    if (c == "Shell_TrayWnd" || c == "Progman" || c == "WorkerW" || c == "Button") return true;
                    Native.RECT r;
                    if (!Native.GetWindowRect(hwnd, out r)) return true;
                    int w = r.Right - r.Left, h = r.Bottom - r.Top;
                    if (w < 80 || h < 80) return true;
                    list.Add(new AppWindow { Handle = hwnd, Title = title.ToString() });
                }
                catch { }
                return true;
            }, IntPtr.Zero);
            return list;
        }

        public static Rectangle Bounds(IntPtr hwnd)
        {
            Native.RECT r;
            if (Native.DwmGetWindowAttribute(hwnd, Native.DWMWA_EXTENDED_FRAME_BOUNDS, out r, Marshal.SizeOf(typeof(Native.RECT))) != 0)
                Native.GetWindowRect(hwnd, out r);
            return Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
        }
    }

    sealed class Grabber : IDisposable
    {
        readonly int dstW, dstH;
        IntPtr srcDc;
        IntPtr fill;
        IntPtr tmpDc, tmpBmp, tmpOld;
        int tmpW, tmpH;
        Rectangle src;
        IntPtr hwnd;
        bool window;
        bool disposed;
        DxDesk dx;

        public int Width { get { return dstW; } }
        public int Height { get { return dstH; } }
        public string Note;

        public Grabber(Rectangle source, int dstW, int dstH)
        {
            this.dstW = Native.Align(dstW, 16);
            this.dstH = Native.Align(dstH, 16);
            src = source;
            window = false;
            srcDc = Native.GetDC(IntPtr.Zero);
            fill = Native.CreateSolidBrush(0x00000000);
            try
            {
                string err;
                dx = DxDesk.Open(source, this.dstW, this.dstH, out err);
                if (dx == null) Note = Short(err);
            }
            catch (Exception e) { Note = Short(e.Message); }
        }

        public Grabber(IntPtr hwnd, int dstW, int dstH)
        {
            this.dstW = Native.Align(dstW, 16);
            this.dstH = Native.Align(dstH, 16);
            this.hwnd = hwnd;
            window = true;
            src = AppWindows.Bounds(hwnd);
            srcDc = Native.GetDC(IntPtr.Zero);
            fill = Native.CreateSolidBrush(0x00000000);
        }

        void EnsureTmp(int w, int h)
        {
            if (w < 1) w = 1;
            if (h < 1) h = 1;
            if (tmpBmp != IntPtr.Zero && tmpW == w && tmpH == h) return;
            FreeTmp();
            tmpDc = Native.CreateCompatibleDC(srcDc);
            tmpBmp = Native.CreateCompatibleBitmap(srcDc, w, h);
            tmpOld = Native.SelectObject(tmpDc, tmpBmp);
            tmpW = w;
            tmpH = h;
        }

        void FreeTmp()
        {
            if (tmpOld != IntPtr.Zero && tmpDc != IntPtr.Zero) Native.SelectObject(tmpDc, tmpOld);
            if (tmpBmp != IntPtr.Zero) Native.DeleteObject(tmpBmp);
            if (tmpDc != IntPtr.Zero) Native.DeleteDC(tmpDc);
            tmpOld = tmpBmp = tmpDc = IntPtr.Zero;
            tmpW = tmpH = 0;
        }

        public void Blit(IntPtr dstDc)
        {
            Native.RECT all;
            all.Left = 0; all.Top = 0; all.Right = dstW; all.Bottom = dstH;
            Native.FillRect(dstDc, ref all, fill);
            Native.SetStretchBltMode(dstDc, Native.HALFTONE);
            Native.SetBrushOrgEx(dstDc, 0, 0, IntPtr.Zero);

            Rectangle r = src;
            if (window)
            {
                if (!Native.IsWindow(hwnd)) return;
                r = AppWindows.Bounds(hwnd);
                if (r.Width < 8 || r.Height < 8) return;
                EnsureTmp(r.Width, r.Height);
                bool printed = Native.PrintWindow(hwnd, tmpDc, Native.PW_RENDERFULLCONTENT);
                if (!printed) return;
                Contain(dstDc, tmpDc, 0, 0, r.Width, r.Height);
                return;
            }
            Contain(dstDc, srcDc, r.X, r.Y, r.Width, r.Height);
        }

        public bool CopyFrame(out byte[] pixels)
        {
            pixels = null;
            if (dx == null) return false;
            try
            {
                pixels = dx.Copy();
                return pixels != null && pixels.Length == dstW * dstH * 4;
            }
            catch (Exception e)
            {
                Note = Short(e.Message);
                try { dx.Dispose(); } catch { }
                dx = null;
                return false;
            }
        }

        static string Short(string s)
        {
            if (string.IsNullOrEmpty(s)) s = "DXGI";
            if (s.Length > 140) s = s.Substring(0, 140);
            return Lang.T("курсор может мигать: ", "the cursor may flicker: ") + s;
        }

        void Contain(IntPtr dstDc, IntPtr from, int sx, int sy, int sw, int sh)
        {
            if (sw < 1) sw = 1;
            if (sh < 1) sh = 1;
            float s = Math.Min(dstW / (float)sw, dstH / (float)sh);
            int w = Math.Max(1, (int)(sw * s));
            int h = Math.Max(1, (int)(sh * s));
            int x = (dstW - w) / 2;
            int y = (dstH - h) / 2;
            Native.StretchBlt(dstDc, x, y, w, h, from, sx, sy, sw, sh, Native.SRCCOPY);
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (dx != null) { try { dx.Dispose(); } catch { } dx = null; }
            FreeTmp();
            if (fill != IntPtr.Zero) Native.DeleteObject(fill);
            if (srcDc != IntPtr.Zero) Native.ReleaseDC(IntPtr.Zero, srcDc);
            fill = srcDc = IntPtr.Zero;
        }
    }

    static class Yuv
    {
        public static byte[] BgraToNv12(byte[] bgra, int srcW, int srcH, int dstW, int dstH)
        {
            int ySize = dstW * dstH;
            byte[] nv = new byte[ySize + ySize / 2];
            for (int y = 0; y < dstH; y++)
            {
                int sy = y * srcH / dstH;
                int srcRow = sy * srcW * 4;
                int yRow = y * dstW;
                for (int x = 0; x < dstW; x++)
                {
                    int sx = x * srcW / dstW;
                    int i = srcRow + sx * 4;
                    int b = bgra[i], g = bgra[i + 1], r = bgra[i + 2];
                    int Y = ((66 * r + 129 * g + 25 * b + 128) >> 8) + 16;
                    if (Y < 0) Y = 0; else if (Y > 255) Y = 255;
                    nv[yRow + x] = (byte)Y;
                }
            }
            int uv = ySize;
            for (int y = 0; y < dstH; y += 2)
            {
                int sy = y * srcH / dstH;
                int srcRow = sy * srcW * 4;
                for (int x = 0; x < dstW; x += 2)
                {
                    int sx = x * srcW / dstW;
                    int i = srcRow + sx * 4;
                    int b = bgra[i], g = bgra[i + 1], r = bgra[i + 2];
                    int U = ((-38 * r - 74 * g + 112 * b + 128) >> 8) + 128;
                    int V = ((112 * r - 94 * g - 18 * b + 128) >> 8) + 128;
                    if (U < 0) U = 0; else if (U > 255) U = 255;
                    if (V < 0) V = 0; else if (V > 255) V = 255;
                    nv[uv++] = (byte)U;
                    nv[uv++] = (byte)V;
                }
            }
            return nv;
        }
    }
}
