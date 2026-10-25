using System;
using System.IO;
using System.IO.Pipes;
using System.ServiceProcess;
using System.Text;
using System.Threading;

namespace GlassCast
{
    static class VddHostMain
    {
        static int Main(string[] args)
        {
            if (args != null && args.Length > 0 && args[0] == "--install")
            {
                string err = VddCtl.InstallService();
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "vdd-setup.txt");
                try { File.WriteAllText(path, err == null ? "ok" : err); } catch { }
                return err == null ? 0 : 1;
            }
            ServiceBase.Run(new VddSvc());
            return 0;
        }
    }

    sealed class VddSvc : ServiceBase
    {
        Thread thread;
        volatile bool stop;

        public VddSvc()
        {
            ServiceName = VddCtl.Service;
            CanStop = true;
        }

        protected override void OnStart(string[] args)
        {
            stop = false;
            thread = new Thread(Loop);
            thread.IsBackground = true;
            thread.Start();
        }

        protected override void OnStop()
        {
            stop = true;
            try
            {
                using (var pipe = new NamedPipeClientStream(".", VddCtl.Pipe, PipeDirection.InOut))
                    pipe.Connect(400);
            }
            catch { }
        }

        void Loop()
        {
            while (!stop)
            {
                NamedPipeServerStream pipe = null;
                try
                {
                    pipe = new NamedPipeServerStream(VddCtl.Pipe, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.None, 1024, 1024, VddCtl.Security());
                    pipe.WaitForConnection();
                    if (stop) break;
                    var reader = new StreamReader(pipe, Encoding.UTF8);
                    var writer = new StreamWriter(pipe, new UTF8Encoding(false)) { AutoFlush = true };
                    string cmd = reader.ReadLine();
                    string ans = "err команда";
                    if (cmd == "ping") ans = "ok";
                    else if (cmd == "on") ans = VddCtl.Enable(true);
                    else if (cmd == "off") ans = VddCtl.Enable(false);
                    writer.WriteLine(ans);
                    try { pipe.WaitForPipeDrain(); } catch { }
                }
                catch { Thread.Sleep(200); }
                finally
                {
                    if (pipe != null) { try { pipe.Dispose(); } catch { } }
                }
            }
        }
    }
}
