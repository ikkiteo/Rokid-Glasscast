using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;

namespace GlassCast
{
    static class IddSetup
    {
        static readonly Guid DisplayClass = new Guid("4d36e968-e325-11ce-bfc1-08002be10318");
        const string Hwid = "Root\\GlassCastIdd";
        const uint DIGCF_PRESENT = 0x00000002;
        const uint DIGCF_ALLCLASSES = 0x00000004;
        const uint SPDRP_HARDWAREID = 1;
        const int DIF_REGISTERDEVICE = 0x00000019;
        const int DICD_GENERATE_ID = 0x00000001;
        const uint INSTALLFLAG_FORCE = 0x00000001;
        const uint SDC_TOPOLOGY_EXTEND = 0x00000004;
        const uint SDC_APPLY = 0x00000080;
        const uint SDC_SAVE_TO_DATABASE = 0x00000200;
        const uint SDC_ALLOW_CHANGES = 0x00000400;

        public static string DriverFolder()
        {
            string here = AppDomain.CurrentDomain.BaseDirectory;
            string[] guesses = new string[] {
                Path.Combine(here, "driver", "IddSampleDriver"),
                Path.Combine(here, "..", "driver", "IddSampleDriver"),
                Path.Combine(here, "..", "..", "driver", "IddSampleDriver")
            };
            foreach (string g in guesses)
            {
                string inf = Path.GetFullPath(Path.Combine(g, "IddSampleDriver.inf"));
                if (File.Exists(inf)) return Path.GetDirectoryName(inf);
            }
            return Path.GetFullPath(Path.Combine(here, "..", "driver", "IddSampleDriver"));
        }

        public static string UsbFolder()
        {
            string here = AppDomain.CurrentDomain.BaseDirectory;
            string[] guesses = new string[] {
                Path.Combine(here, "driver", "usbmmidd", "usbmmidd_v2"),
                Path.Combine(here, "..", "driver", "usbmmidd", "usbmmidd_v2"),
                Path.Combine(here, "..", "..", "driver", "usbmmidd", "usbmmidd_v2")
            };
            foreach (string g in guesses)
            {
                string dir = Path.GetFullPath(g);
                if (File.Exists(Path.Combine(dir, "deviceinstaller64.exe")) && File.Exists(Path.Combine(dir, "usbmmIdd.inf")))
                    return dir;
            }
            return null;
        }

        public static string Status()
        {
            if (UsbFolder() == null) return Lang.T("нет пакета драйвера driver\\usbmmidd", "driver package driver\\usbmmidd is missing");
            if (Screens.Glass() != null) return Lang.T("виртуальный монитор включён", "virtual monitor is on");
            return Lang.T("виртуальный экран выключен — включится на время трансляции", "virtual display is off — it turns on while you stream");
        }

        public static string SetVirtual(bool on)
        {
            if (on)
            {
                Monitor have = Screens.Glass();
                if (have != null)
                {
                    SetMode(have, 1920, 1080);
                    ExtendDesktop();
                    return null;
                }
                string ans = AskReady("on");
                if (ans == null) return Lang.T("не удалось включить службу экрана", "could not start the display service") + SetupNote();
                if (!ans.StartsWith("ok")) return Lang.T("виртуальный экран: ", "virtual display: ") + ans;
                Monitor mon = WaitForGlass(12);
                if (mon == null) return Lang.T("виртуальный экран не появился", "the virtual display did not appear");
                SetMode(mon, 1920, 1080);
                ExtendDesktop();
                for (int i = 0; i < 10; i++)
                {
                    mon = Screens.Glass();
                    if (mon != null && mon.Bounds.Width == 1920 && mon.Bounds.Height == 1080) break;
                    System.Threading.Thread.Sleep(200);
                }
                System.Threading.Thread.Sleep(300);
                return null;
            }
            for (int n = 0; n < 4 && Screens.Glass() != null; n++)
            {
                string ans = AskReady("off");
                if (ans == null) return Lang.T("не удалось выключить виртуальный экран", "could not turn the virtual display off") + SetupNote();
                if (!ans.StartsWith("ok")) return Lang.T("не выключил экран: ", "did not turn the display off: ") + ans;
                for (int i = 0; i < 16; i++)
                {
                    if (Screens.Glass() == null) return null;
                    System.Threading.Thread.Sleep(200);
                }
            }
            return Screens.Glass() == null ? null : Lang.T("виртуальный экран не погас", "the virtual display stayed on");
        }

        static string AskReady(string cmd)
        {
            string ans = VddCtl.Ask(cmd);
            if (ans != null) return ans;
            if (!ElevateHost()) return null;
            return VddCtl.Ask(cmd);
        }

        static bool ElevateHost()
        {
            string host = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "VddHost.exe");
            if (!File.Exists(host)) return false;
            try
            {
                var p = Process.Start(new ProcessStartInfo
                {
                    FileName = host,
                    Arguments = "--install",
                    UseShellExecute = true,
                    Verb = "runas"
                });
                if (p == null) return false;
                p.WaitForExit(25000);
                return p.ExitCode == 0;
            }
            catch { return false; }
        }

        static string SetupNote()
        {
            try
            {
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "vdd-setup.txt");
                if (!File.Exists(path)) return "";
                string t = File.ReadAllText(path).Trim();
                if (t == "ok" || t.Length == 0) return "";
                if (t.Length > 180) t = t.Substring(0, 180);
                return " (" + t + ")";
            }
            catch { return ""; }
        }

        public static string Install()
        {
            if (!IsAdmin()) return Lang.T("нужны права администратора", "administrator rights are required");
            string usb = UsbFolder();
            if (usb == null) return Lang.T("нет driver\\usbmmidd\\usbmmidd_v2 рядом с программой", "driver\\usbmmidd\\usbmmidd_v2 is not next to the program");
            return InstallUsb(usb);
        }

        static string InstallUsb(string dir)
        {
            string exe = Path.Combine(dir, "deviceinstaller64.exe");
            string inst = Run(exe, "install usbmmIdd.inf usbmmidd", 120000, dir);
            if (inst != null && inst.StartsWith("FAIL")) return Lang.T("установка драйвера: ", "driver install: ") + inst.Substring(5).Trim();
            string svc = VddCtl.InstallService();
            for (int i = 0; i < 4; i++)
            {
                string off = Run(exe, "enableidd 0", 30000, dir);
                if (off != null && off.StartsWith("FAIL")) break;
                if (Screens.Glass() == null) break;
                System.Threading.Thread.Sleep(300);
            }
            if (svc != null) return Lang.T("Драйвер поставлен, но служба экрана не запустилась: ", "The driver is installed, but the display service did not start: ") + svc + Lang.T(" Драйвер — USB Mobile Monitor, Amyuni Technologies.", " The driver is USB Mobile Monitor by Amyuni Technologies.");
            return Lang.T("Драйвер экрана установлен (USB Mobile Monitor, Amyuni Technologies). Виртуальный монитор включается только на время трансляции.", "Display driver installed (USB Mobile Monitor, Amyuni Technologies). The virtual monitor turns on only while you stream.");
        }

        static HashSet<string> MonitorNames()
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Monitor m in Screens.All()) if (!string.IsNullOrEmpty(m.Name)) names.Add(m.Name);
            return names;
        }

        static Monitor FindNew(HashSet<string> before)
        {
            foreach (Monitor m in Screens.All())
            {
                if (Screens.IsGlass(m)) return m;
            }
            foreach (Monitor m in Screens.All())
            {
                if (!string.IsNullOrEmpty(m.Name) && !before.Contains(m.Name)) return m;
            }
            return null;
        }

        static Monitor WaitForNew(HashSet<string> before, int seconds)
        {
            for (int i = 0; i < seconds * 2; i++)
            {
                Monitor m = FindNew(before);
                if (m != null) return m;
                System.Threading.Thread.Sleep(500);
            }
            return null;
        }

        static string InstallIddSampleUnused()
        {
            if (!IsAdmin()) return Lang.T("нужны права администратора", "administrator rights are required");
            string dir = DriverFolder();
            string inf = Path.Combine(dir, "IddSampleDriver.inf");
            string dll = DllPath(dir);
            if (!File.Exists(inf)) return Lang.T("нет ", "missing ") + inf;
            if (dll == null) return Lang.T("нет IddSampleDriver.dll", "IddSampleDriver.dll is missing");

            string copy = Path.Combine(dir, "IddSampleDriver.dll");
            if (!string.Equals(dll, copy, StringComparison.OrdinalIgnoreCase))
            {
                try { File.Copy(dll, copy, true); }
                catch (Exception e) { return Lang.T("не скопировал dll: ", "could not copy the dll: ") + e.Message; }
            }

            if (!TestSignActive())
            {
                string boot = EnableTestSigning();
                if (boot != null) return boot;
                return Lang.T("Тестовая подпись включена. Перезагрузи компьютер и снова нажми «драйвер дисплея». Без перезагрузки Windows этот драйвер не загрузит.", "Test signing is on. Reboot, then press Display driver again. Windows will not load this driver before a reboot.");
            }

            string signed = Sign(dir);
            if (signed != null) return signed;

            string listed = Run("pnputil.exe", "/add-driver \"" + inf + "\" /install", 60000);
            if (listed != null && listed.StartsWith("FAIL")) return listed;

            string err;
            if (!HasDevice())
            {
                if (!CreateRootDevice(inf, out err)) return err;
            }
            else
            {
                bool reboot;
                if (!UpdateDriverForPlugAndPlayDevices(IntPtr.Zero, Hwid, inf, INSTALLFLAG_FORCE, out reboot))
                    return "UpdateDriver 0x" + Marshal.GetLastWin32Error().ToString("X");
            }

            ExtendDesktop();
            Monitor mon = WaitForGlass(25);
            ExtendDesktop();
            if (mon == null) mon = WaitForGlass(10);
            if (mon == null)
                return Lang.T("драйвер поставлен, монитор ещё не виден. Параметры → Система → Экран → несколько дисплеев → Расширить.", "The driver is installed, but the monitor is not visible yet. Settings → System → Display → Multiple displays → Extend.");
            return Lang.T("виртуальный монитор GlassCast включён, рабочий стол расширен. Выбери источник «виртуальный дисплей GlassCast» и транслируй. Окна перетаскиваются на этот экран как на обычный монитор.", "The GlassCast virtual monitor is on and the desktop is extended. Choose the GlassCast virtual display source and stream. Drag windows onto it like any other monitor.");
        }

        static string DllPath(string dir)
        {
            string[] guesses = new string[] {
                Path.Combine(dir, "IddSampleDriver.dll"),
                Path.Combine(dir, "x64", "Release", "IddSampleDriver.dll"),
                Path.Combine(dir, "x64", "Debug", "IddSampleDriver.dll")
            };
            foreach (string g in guesses) if (File.Exists(g)) return g;
            return null;
        }

        static string Sign(string dir)
        {
            string script = Path.GetFullPath(Path.Combine(dir, "..", "sign.ps1"));
            if (!File.Exists(script)) return Lang.T("нет ", "missing ") + script;
            string output = Run("powershell.exe", "-NoProfile -ExecutionPolicy Bypass -File \"" + script + "\" -Dir \"" + dir + "\"", 120000);
            if (output != null && output.StartsWith("FAIL")) return output.Substring(5).Trim();
            if (!File.Exists(Path.Combine(dir, "GlassCastIdd.cat"))) return Lang.T("каталог драйвера не создан. Нужен Windows SDK (inf2cat, signtool).", "The driver catalog was not created. The Windows SDK (inf2cat, signtool) is required.");
            return null;
        }

        static Monitor WaitForGlass(int seconds)
        {
            for (int i = 0; i < seconds * 2; i++)
            {
                Monitor m = Screens.Glass();
                if (m != null) return m;
                System.Threading.Thread.Sleep(500);
            }
            return null;
        }

        public static int ExtendDesktop()
        {
            return SetDisplayConfig(0, IntPtr.Zero, 0, IntPtr.Zero,
                SDC_TOPOLOGY_EXTEND | SDC_APPLY | SDC_ALLOW_CHANGES | SDC_SAVE_TO_DATABASE);
        }

        static void SetMode(Monitor mon, int width, int height)
        {
            if (mon == null || string.IsNullOrEmpty(mon.Name)) return;
            if (mon.Bounds.Width == width && mon.Bounds.Height == height) return;
            string dev = mon.Name.StartsWith(@"\\.\") ? mon.Name : @"\\.\" + mon.Name;
            var dm = new DEVMODE();
            dm.dmSize = (short)Marshal.SizeOf(typeof(DEVMODE));
            if (!EnumDisplaySettings(dev, -1, ref dm)) return;
            dm.dmPelsWidth = width;
            dm.dmPelsHeight = height;
            dm.dmFields |= 0x80000 | 0x100000;
            dm.dmSize = (short)Marshal.SizeOf(typeof(DEVMODE));
            ChangeDisplaySettingsEx(dev, ref dm, IntPtr.Zero, 0x1, IntPtr.Zero);
        }

        static bool IsAdmin()
        {
            try
            {
                var id = WindowsIdentity.GetCurrent();
                return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch { return false; }
        }

        public static bool TestSignActive()
        {
            IntPtr buf = Marshal.AllocHGlobal(8);
            try
            {
                Marshal.WriteInt32(buf, 0, 8);
                int ret;
                int st = NtQuerySystemInformation(103, buf, 8, out ret);
                if (st != 0) return false;
                uint opts = (uint)Marshal.ReadInt32(buf, 4);
                return (opts & 0x2) != 0;
            }
            catch { return false; }
            finally { Marshal.FreeHGlobal(buf); }
        }

        static string EnableTestSigning()
        {
            string output = Run("bcdedit.exe", "/set testsigning on", 15000);
            if (output != null && output.StartsWith("FAIL")) return "bcdedit: " + output.Substring(5).Trim();
            return null;
        }

        static string Run(string file, string args, int ms)
        {
            return Run(file, args, ms, null);
        }

        static string Run(string file, string args, int ms, string cwd)
        {
            try
            {
                var info = new ProcessStartInfo
                {
                    FileName = file,
                    Arguments = args,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                if (!string.IsNullOrEmpty(cwd)) info.WorkingDirectory = cwd;
                var p = Process.Start(info);
                if (p == null) return "FAIL " + Lang.T("не запустился ", "did not start ") + file;
                string o = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
                if (!p.WaitForExit(ms)) return "FAIL " + Lang.T("таймаут ", "timed out ") + file;
                if (p.ExitCode != 0 && p.ExitCode != 3010) return "FAIL " + file + " " + p.ExitCode + " " + o;
                return o;
            }
            catch (Exception e) { return "FAIL " + e.Message; }
        }

        static bool HasDevice()
        {
            IntPtr list = SetupDiGetClassDevs(IntPtr.Zero, "ROOT", IntPtr.Zero, DIGCF_PRESENT | DIGCF_ALLCLASSES);
            if (list == IntPtr.Zero || list == new IntPtr(-1)) return false;
            try
            {
                var data = new SP_DEVINFO_DATA();
                data.cbSize = Marshal.SizeOf(typeof(SP_DEVINFO_DATA));
                for (int i = 0; SetupDiEnumDeviceInfo(list, i, ref data); i++)
                {
                    string id = ReadHardwareId(list, ref data);
                    if (id != null && id.IndexOf("GlassCastIdd", StringComparison.OrdinalIgnoreCase) >= 0) return true;
                }
                return false;
            }
            finally { SetupDiDestroyDeviceInfoList(list); }
        }

        static string ReadHardwareId(IntPtr list, ref SP_DEVINFO_DATA data)
        {
            uint kind;
            int needed;
            byte[] buf = new byte[512];
            if (!SetupDiGetDeviceRegistryProperty(list, ref data, SPDRP_HARDWAREID, out kind, buf, buf.Length, out needed))
                return null;
            return Encoding.Unicode.GetString(buf).Trim('\0', ' ');
        }

        static bool CreateRootDevice(string inf, out string err)
        {
            err = "";
            Guid cls = DisplayClass;
            IntPtr list = SetupDiCreateDeviceInfoList(ref cls, IntPtr.Zero);
            if (list == IntPtr.Zero || list == new IntPtr(-1))
            {
                err = "SetupDiCreateDeviceInfoList 0x" + Marshal.GetLastWin32Error().ToString("X");
                return false;
            }
            try
            {
                var data = new SP_DEVINFO_DATA();
                data.cbSize = Marshal.SizeOf(typeof(SP_DEVINFO_DATA));
                if (!SetupDiCreateDeviceInfo(list, "GlassCastIdd", ref cls, "GlassCast Glasses", IntPtr.Zero, DICD_GENERATE_ID, ref data))
                {
                    err = "CreateDeviceInfo 0x" + Marshal.GetLastWin32Error().ToString("X");
                    return false;
                }
                byte[] id = Encoding.Unicode.GetBytes(Hwid + "\0\0");
                if (!SetupDiSetDeviceRegistryProperty(list, ref data, SPDRP_HARDWAREID, id, id.Length))
                {
                    err = "SetHardwareId 0x" + Marshal.GetLastWin32Error().ToString("X");
                    return false;
                }
                if (!SetupDiCallClassInstaller(DIF_REGISTERDEVICE, list, ref data))
                {
                    err = "RegisterDevice 0x" + Marshal.GetLastWin32Error().ToString("X");
                    return false;
                }
                bool reboot;
                if (!UpdateDriverForPlugAndPlayDevices(IntPtr.Zero, Hwid, inf, INSTALLFLAG_FORCE, out reboot))
                {
                    err = "UpdateDriver 0x" + Marshal.GetLastWin32Error().ToString("X") + Lang.T(" (тестовая подпись должна быть включена в текущей загрузке)", " (test signing must be on for this boot)");
                    return false;
                }
                return true;
            }
            finally
            {
                SetupDiDestroyDeviceInfoList(list);
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        struct SP_DEVINFO_DATA
        {
            public int cbSize;
            public Guid ClassGuid;
            public int DevInst;
            public IntPtr Reserved;
        }

        [DllImport("ntdll.dll")]
        static extern int NtQuerySystemInformation(int cls, IntPtr info, int len, out int ret);

        [DllImport("user32.dll")]
        static extern int SetDisplayConfig(uint pathCount, IntPtr paths, uint modeCount, IntPtr modes, uint flags);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        static extern bool EnumDisplaySettings(string device, int mode, ref DEVMODE devMode);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        static extern int ChangeDisplaySettingsEx(string device, ref DEVMODE devMode, IntPtr hwnd, int flags, IntPtr param);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        struct DEVMODE
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string dmDeviceName;
            public short dmSpecVersion;
            public short dmDriverVersion;
            public short dmSize;
            public short dmDriverExtra;
            public int dmFields;
            public int dmPositionX;
            public int dmPositionY;
            public int dmDisplayOrientation;
            public int dmDisplayFixedOutput;
            public short dmColor;
            public short dmDuplex;
            public short dmYResolution;
            public short dmTTOption;
            public short dmCollate;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string dmFormName;
            public short dmLogPixels;
            public int dmBitsPerPel;
            public int dmPelsWidth;
            public int dmPelsHeight;
            public int dmDisplayFlags;
            public int dmDisplayFrequency;
            public int dmICMMethod;
            public int dmICMIntent;
            public int dmMediaType;
            public int dmDitherType;
            public int dmReserved1;
            public int dmReserved2;
            public int dmPanningWidth;
            public int dmPanningHeight;
        }

        [DllImport("setupapi.dll", CharSet = CharSet.Auto, SetLastError = true)]
        static extern IntPtr SetupDiCreateDeviceInfoList(ref Guid classGuid, IntPtr hwnd);

        [DllImport("setupapi.dll", CharSet = CharSet.Auto, SetLastError = true)]
        static extern IntPtr SetupDiGetClassDevs(IntPtr classGuid, string enumerator, IntPtr hwnd, uint flags);

        [DllImport("setupapi.dll", CharSet = CharSet.Auto, SetLastError = true)]
        static extern bool SetupDiEnumDeviceInfo(IntPtr list, int index, ref SP_DEVINFO_DATA data);

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool SetupDiGetDeviceRegistryProperty(IntPtr list, ref SP_DEVINFO_DATA data, uint prop, out uint regType, byte[] buf, int size, out int needed);

        [DllImport("setupapi.dll", CharSet = CharSet.Auto, SetLastError = true)]
        static extern bool SetupDiDestroyDeviceInfoList(IntPtr list);

        [DllImport("setupapi.dll", CharSet = CharSet.Auto, SetLastError = true)]
        static extern bool SetupDiCreateDeviceInfo(IntPtr list, string name, ref Guid classGuid, string desc, IntPtr hwnd, int flags, ref SP_DEVINFO_DATA data);

        [DllImport("setupapi.dll", CharSet = CharSet.Auto, SetLastError = true)]
        static extern bool SetupDiSetDeviceRegistryProperty(IntPtr list, ref SP_DEVINFO_DATA data, uint prop, byte[] buf, int size);

        [DllImport("setupapi.dll", SetLastError = true)]
        static extern bool SetupDiCallClassInstaller(int fn, IntPtr list, ref SP_DEVINFO_DATA data);

        [DllImport("newdev.dll", CharSet = CharSet.Auto, SetLastError = true)]
        static extern bool UpdateDriverForPlugAndPlayDevices(IntPtr hwnd, string hardwareId, string inf, uint flags, out bool reboot);
    }
}
