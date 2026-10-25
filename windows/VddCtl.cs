using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading;

namespace GlassCast
{
    static class VddCtl
    {
        public const string Pipe = "GlassCastVdd";
        public const string Service = "GlassCastVdd";

        public static string Ask(string cmd)
        {
            try
            {
                using (var pipe = new NamedPipeClientStream(".", Pipe, PipeDirection.InOut))
                {
                    pipe.Connect(700);
                    var w = new StreamWriter(pipe, new UTF8Encoding(false)) { AutoFlush = true };
                    var r = new StreamReader(pipe, Encoding.UTF8);
                    w.WriteLine(cmd ?? "");
                    return r.ReadLine();
                }
            }
            catch { return null; }
        }

        public static string Enable(bool on)
        {
            string dir = Folder();
            if (dir == null) return Lang.T("err нет пакета драйвера", "err driver package is missing");
            string exe = Path.Combine(dir, "deviceinstaller64.exe");
            string result = Run(exe, on ? "enableidd 1" : "enableidd 0", dir, 30000);
            if (result != null && result.StartsWith("FAIL")) return "err " + result.Substring(5).Trim();
            return "ok";
        }

        public static string InstallService()
        {
            string exe = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "VddHost.exe");
            if (!File.Exists(exe)) return Lang.T("нет VddHost.exe", "VddHost.exe is missing");
            string binPath = exe.IndexOf(' ') >= 0 ? "\"\\\"" + exe + "\\\"\"" : exe;
            string bin = "binPath= " + binPath;
            var log = new StringBuilder();
            if (!ServiceExists())
            {
                string c = Run("sc.exe", "create " + Service + " " + bin + " start= auto", null, 15000);
                log.AppendLine(c ?? "");
                if (c != null && c.StartsWith("FAIL")) return Lang.T("служба: ", "service: ") + c.Substring(5).Trim();
            }
            else
            {
                Run("sc.exe", "stop " + Service, null, 15000);
                Thread.Sleep(500);
                string c = Run("sc.exe", "config " + Service + " " + bin, null, 15000);
                log.AppendLine(c ?? "");
                if (c != null && c.StartsWith("FAIL")) return Lang.T("служба: ", "service: ") + c.Substring(5).Trim();
            }
            string started = Run("sc.exe", "start " + Service, null, 20000);
            log.AppendLine(started ?? "");
            if (started != null && started.StartsWith("FAIL") && started.IndexOf("1056") < 0)
                return Lang.T("запуск службы: ", "service start: ") + started.Substring(5).Trim();
            for (int i = 0; i < 25; i++)
            {
                string pong = Ask("ping");
                if (pong != null && pong.StartsWith("ok")) return null;
                Thread.Sleep(200);
            }
            return Lang.T("служба не открыла канал. ", "the service did not open its channel. ") + log.ToString().Trim();
        }

        public static PipeSecurity Security()
        {
            var sec = new PipeSecurity();
            Allow(sec, WellKnownSidType.LocalSystemSid, PipeAccessRights.FullControl);
            Allow(sec, WellKnownSidType.BuiltinAdministratorsSid, PipeAccessRights.FullControl);
            PipeAccessRights use = PipeAccessRights.ReadWrite | PipeAccessRights.CreateNewInstance;
            Allow(sec, WellKnownSidType.AuthenticatedUserSid, use);
            Allow(sec, WellKnownSidType.BuiltinUsersSid, use);
            return sec;
        }

        static void Allow(PipeSecurity sec, WellKnownSidType sid, PipeAccessRights rights)
        {
            sec.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(sid, null), rights, AccessControlType.Allow));
        }

        static bool ServiceExists()
        {
            string q = Run("sc.exe", "query " + Service, null, 8000);
            if (q == null) return true;
            if (q.IndexOf("1060") >= 0) return false;
            return !q.StartsWith("FAIL");
        }

        public static string Folder()
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

        public static string Run(string file, string args, string cwd, int ms)
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
    }
}
