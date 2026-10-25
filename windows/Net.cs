using System;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace GlassCast
{
    sealed class Found
    {
        public string Host, Name;
        public int Port, Width, Height, Fps;
        public override string ToString()
        {
            return Name + "  " + Host + "  " + Width + "×" + Height;
        }
    }

    sealed class LocalIf
    {
        public IPAddress Addr;
        public IPAddress Bcast;
        public bool Sweep24;
    }

    static class Discover
    {
        const int Udp = 47811;

        public static List<Found> Scan(int millis)
        {
            return Scan(millis, null);
        }

        public static List<Found> Scan(int millis, string hint)
        {
            var map = new Dictionary<string, Found>();
            var socks = new List<Socket>();
            byte[] probe = Encoding.ASCII.GetBytes("GCASTQ?\n");
            try
            {
                List<LocalIf> locals = LocalV4();
                if (locals.Count == 0)
                {
                    LocalIf any = new LocalIf();
                    any.Addr = IPAddress.Any;
                    any.Bcast = IPAddress.Broadcast;
                    any.Sweep24 = false;
                    locals.Add(any);
                }

                foreach (LocalIf loc in locals)
                {
                    Socket sock = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                    sock.EnableBroadcast = true;
                    sock.ReceiveTimeout = 1;
                    try { sock.Bind(new IPEndPoint(loc.Addr, 0)); }
                    catch
                    {
                        try { sock.Close(); } catch { }
                        continue;
                    }
                    socks.Add(sock);
                    SendQuiet(sock, probe, new IPEndPoint(IPAddress.Broadcast, Udp));
                    if (loc.Bcast != null)
                        SendQuiet(sock, probe, new IPEndPoint(loc.Bcast, Udp));
                    IPAddress typed = ParseV4(hint);
                    if (typed != null)
                        SendQuiet(sock, probe, new IPEndPoint(typed, Udp));
                }

                foreach (LocalIf loc in locals)
                {
                    if (!loc.Sweep24 || loc.Addr == null) continue;
                    byte[] raw = loc.Addr.GetAddressBytes();
                    Socket sock = SocketFor(socks, loc.Addr);
                    if (sock == null) continue;
                    for (int i = 1; i <= 254; i++)
                    {
                        if (raw[3] == (byte)i) continue;
                        IPAddress dest = new IPAddress(new byte[] { raw[0], raw[1], raw[2], (byte)i });
                        SendQuiet(sock, probe, new IPEndPoint(dest, Udp));
                    }
                }

                long until = Environment.TickCount + millis;
                byte[] buf = new byte[1024];
                while (Environment.TickCount < until)
                {
                    if (socks.Count == 0) break;
                    List<Socket> check = new List<Socket>(socks);
                    try { Socket.Select(check, null, null, 40000); }
                    catch { Thread.Sleep(20); continue; }
                    if (check.Count == 0) continue;
                    foreach (Socket sock in check)
                    {
                        try
                        {
                            EndPoint any = new IPEndPoint(IPAddress.Any, 0);
                            int n = sock.ReceiveFrom(buf, ref any);
                            string json = Encoding.UTF8.GetString(buf, 0, n);
                            if (json.IndexOf("glasscast") < 0) continue;
                            IPEndPoint ep = (IPEndPoint)any;
                            map[ep.Address.ToString()] = new Found
                            {
                                Host = ep.Address.ToString(),
                                Port = Json.Int(json, "port", 47810),
                                Name = Json.Str(json, "name", ep.Address.ToString()),
                                Width = Json.Int(json, "w", 480),
                                Height = Json.Int(json, "h", 640),
                                Fps = Json.Int(json, "fps", 60)
                            };
                        }
                        catch { }
                    }
                }
            }
            finally
            {
                foreach (Socket s in socks)
                {
                    try { s.Close(); } catch { }
                }
            }
            return new List<Found>(map.Values);
        }

        static Socket SocketFor(List<Socket> socks, IPAddress addr)
        {
            foreach (Socket s in socks)
            {
                try
                {
                    IPEndPoint ep = s.LocalEndPoint as IPEndPoint;
                    if (ep != null && ep.Address.Equals(addr)) return s;
                }
                catch { }
            }
            return socks.Count > 0 ? socks[0] : null;
        }

        static void SendQuiet(Socket sock, byte[] probe, EndPoint dest)
        {
            try { sock.SendTo(probe, dest); } catch { }
        }

        static IPAddress ParseV4(string hint)
        {
            if (hint == null) return null;
            string t = hint.Trim();
            if (t.Length == 0) return null;
            IPAddress ip;
            if (!IPAddress.TryParse(t, out ip)) return null;
            if (ip.AddressFamily != AddressFamily.InterNetwork) return null;
            return ip;
        }

        static List<LocalIf> LocalV4()
        {
            var list = new List<LocalIf>();
            NetworkInterface[] nics;
            try { nics = NetworkInterface.GetAllNetworkInterfaces(); }
            catch { return list; }
            foreach (NetworkInterface ni in nics)
            {
                try
                {
                    if (ni.OperationalStatus != OperationalStatus.Up) continue;
                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    foreach (UnicastIPAddressInformation ua in ni.GetIPProperties().UnicastAddresses)
                    {
                        if (ua.Address == null || ua.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                        byte[] ip = ua.Address.GetAddressBytes();
                        if (ip[0] == 127) continue;
                        LocalIf loc = new LocalIf();
                        loc.Addr = ua.Address;
                        loc.Bcast = IPAddress.Broadcast;
                        loc.Sweep24 = false;
                        try
                        {
                            if (ua.IPv4Mask != null)
                            {
                                byte[] mask = ua.IPv4Mask.GetAddressBytes();
                                byte[] bcast = new byte[4];
                                for (int i = 0; i < 4; i++)
                                    bcast[i] = (byte)(ip[i] | (~mask[i] & 0xFF));
                                loc.Bcast = new IPAddress(bcast);
                                loc.Sweep24 = mask[0] == 255 && mask[1] == 255 && mask[2] == 255 && mask[3] == 0
                                    && ip[0] != 169;
                            }
                        }
                        catch { }
                        list.Add(loc);
                    }
                }
                catch { }
            }
            return list;
        }
    }

    static class Json
    {
        public static string Str(string json, string key, string fallback)
        {
            string q = "\"" + key + "\"";
            int i = json.IndexOf(q);
            if (i < 0) return fallback;
            i = json.IndexOf(':', i + q.Length);
            if (i < 0) return fallback;
            i++;
            while (i < json.Length && json[i] <= ' ') i++;
            if (i < json.Length && json[i] == '"')
            {
                int j = json.IndexOf('"', i + 1);
                return j < 0 ? fallback : json.Substring(i + 1, j - i - 1);
            }
            return fallback;
        }

        public static int Int(string json, string key, int fallback)
        {
            string q = "\"" + key + "\"";
            int i = json.IndexOf(q);
            if (i < 0) return fallback;
            i = json.IndexOf(':', i + q.Length);
            if (i < 0) return fallback;
            i++;
            int v = 0, sign = 1, got = 0;
            while (i < json.Length && json[i] <= ' ') i++;
            if (i < json.Length && json[i] == '-') { sign = -1; i++; }
            while (i < json.Length && json[i] >= '0' && json[i] <= '9')
            {
                v = v * 10 + (json[i] - '0');
                i++;
                got = 1;
            }
            return got == 0 ? fallback : v * sign;
        }
    }

    sealed class WireWriter
    {
        readonly NetworkStream stream;
        readonly object gate = new object();
        readonly byte[] head = new byte[13];

        public WireWriter(NetworkStream stream) { this.stream = stream; }

        public void Hello(string src, string name, int w, int h, int fps, int rotate)
        {
            string json = "{\"src\":\"" + Esc(src) + "\",\"name\":\"" + Esc(name) + "\",\"w\":" + w
                + ",\"h\":" + h + ",\"fps\":" + fps + ",\"codec\":\"mjpg\",\"rotate\":" + rotate + "}";
            byte[] body = Encoding.UTF8.GetBytes(json);
            byte[] pkt = new byte[6 + body.Length];
            pkt[0] = (byte)'G'; pkt[1] = (byte)'C'; pkt[2] = (byte)'S'; pkt[3] = (byte)'1';
            pkt[4] = (byte)(body.Length & 0xFF);
            pkt[5] = (byte)((body.Length >> 8) & 0xFF);
            Buffer.BlockCopy(body, 0, pkt, 6, body.Length);
            lock (gate)
            {
                stream.Write(pkt, 0, pkt.Length);
                stream.Flush();
            }
        }

        public void Frame(int kind, byte[] payload, long ptsUs)
        {
            int n = payload == null ? 0 : payload.Length;
            head[0] = (byte)kind;
            PutInt(head, 1, n);
            PutLong(head, 5, ptsUs);
            lock (gate)
            {
                stream.Write(head, 0, 13);
                if (n > 0) stream.Write(payload, 0, n);
                stream.Flush();
            }
        }

        static void PutInt(byte[] b, int at, int v)
        {
            b[at] = (byte)v; b[at + 1] = (byte)(v >> 8); b[at + 2] = (byte)(v >> 16); b[at + 3] = (byte)(v >> 24);
        }

        static void PutLong(byte[] b, int at, long v)
        {
            for (int i = 0; i < 8; i++) b[at + i] = (byte)(v >> (8 * i));
        }

        static string Esc(string s)
        {
            if (s == null) return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }
    }

    sealed class Streamer
    {
        public volatile bool Running;
        public volatile string Status = "готов";
        public volatile int Fps, Kbps;
        long frames;
        public long Frames { get { return Interlocked.Read(ref frames); } }
        public void AddFrame() { Interlocked.Increment(ref frames); }

        volatile bool wantKey;
        volatile int newBitrate;
        Thread worker;
        readonly object pipeLock = new object();
        Socket pipe;

        public void Bind(Socket s)
        {
            lock (pipeLock) pipe = s;
        }

        public void Unbind(Socket s)
        {
            lock (pipeLock) { if (pipe == s) pipe = null; }
        }

        public void Start(Action loop)
        {
            Stop();
            Running = true;
            worker = new Thread(() =>
            {
                try { loop(); }
                catch (Exception e) { if (Running) Status = e.Message; }
                Running = false;
            });
            worker.IsBackground = true;
            worker.Priority = ThreadPriority.Highest;
            worker.Start();
        }

        public void Stop()
        {
            Running = false;
            if (Status == "готов" || Status.StartsWith("трансляция")) Status = "остановлено";
            Socket s;
            lock (pipeLock) s = pipe;
            if (s != null)
            {
                try { s.Shutdown(SocketShutdown.Both); } catch { }
                try { s.Close(); } catch { }
            }
            if (worker != null)
            {
                try { worker.Join(1500); } catch { }
                worker = null;
            }
        }

        public void RequestKey() { wantKey = true; }
        public void SetBitrate(int b) { newBitrate = b; }

        public bool TakeKey()
        {
            if (!wantKey) return false;
            wantKey = false;
            return true;
        }

        public int TakeBitrate()
        {
            int b = newBitrate;
            newBitrate = 0;
            return b;
        }
    }

    static class TcpCast
    {
        public static void Run(
            Streamer live,
            string host, int port,
            string src, string name,
            int w, int h, int fps, int bitrate, int rotate,
            Grabber grab)
        {
            live.Status = "подключаюсь к " + host + "…";
            while (live.Running)
            {
                try
                {
                    using (var sock = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp))
                    {
                        sock.NoDelay = true;
                        sock.SendBufferSize = 64 * 1024;
                        var ar = sock.BeginConnect(host, port, null, null);
                        if (!ar.AsyncWaitHandle.WaitOne(8000, false))
                            throw new TimeoutException("очки не приняли TCP " + host + ":" + port);
                        sock.EndConnect(ar);
                        if (!live.Running) break;
                        live.Bind(sock);
                        try
                        {
                        var ns = new NetworkStream(sock, false);
                        var wr = new WireWriter(ns);
                        wr.Hello(src, name, w, h, fps, rotate);
                        int quality = BitrateToQuality(bitrate);
                        using (var enc = new JpegEnc(w, h, quality))
                        {
                            live.Status = "трансляция на " + host + " · JPEG " + w + "×" + h;
                            if (!string.IsNullOrEmpty(grab.Note)) live.Status += " · " + grab.Note;
                            var ctl = new Thread(() => ReadControlJpeg(ns, live, enc));
                            ctl.IsBackground = true;
                            ctl.Start();

                            var sw = System.Diagnostics.Stopwatch.StartNew();
                            long next = 0;
                            long step = 1000 / Math.Max(1, fps);
                            long lastMark = 0, lastFrames = 0, sent = 0;
                            long t0 = sw.ElapsedMilliseconds;

                            while (live.Running)
                            {
                                live.TakeKey();
                                int nb = live.TakeBitrate();
                                if (nb > 0) enc.SetQuality(BitrateToQuality(nb));

                                byte[] px;
                                byte[] au;
                                if (grab.CopyFrame(out px)) au = enc.EncodeBgra(px);
                                else
                                {
                                    IntPtr hdc = enc.GetHdc();
                                    try { grab.Blit(hdc); }
                                    finally { enc.ReleaseHdc(hdc); }
                                    au = enc.Encode();
                                }
                                long pts = (sw.ElapsedMilliseconds - t0) * 1000;
                                if (au != null && au.Length > 0)
                                {
                                    wr.Frame(1, au, pts);
                                    live.AddFrame();
                                    sent += au.Length;
                                }

                                long now = sw.ElapsedMilliseconds;
                                if (now - lastMark >= 500)
                                {
                                    long span = Math.Max(1, now - lastMark);
                                    live.Fps = (int)((live.Frames - lastFrames) * 1000 / span);
                                    live.Kbps = (int)(sent * 8 / span);
                                    lastFrames = live.Frames;
                                    sent = 0;
                                    lastMark = now;
                                }
                                next += step;
                                int sleep = (int)(next - now);
                                if (sleep > 0) Thread.Sleep(sleep);
                                else next = now;
                            }
                        }
                        }
                        finally { live.Unbind(sock); }
                    }
                }
                catch (Exception e)
                {
                    if (!live.Running) break;
                    live.Status = e.Message;
                    Thread.Sleep(1200);
                }
            }
        }

        static int BitrateToQuality(int bitrate)
        {
            int q = 70 + bitrate / 500000;
            if (q < 62) q = 62;
            if (q > 90) q = 90;
            return q;
        }

        static void ReadControlJpeg(NetworkStream ns, Streamer live, JpegEnc enc)
        {
            ReadControl(ns, live, null);
        }

        static void ReadControl(NetworkStream ns, Streamer live, H264Encoder enc)
        {
            byte[] b = new byte[5];
            try
            {
                while (live.Running)
                {
                    int got = 0;
                    while (got < 5)
                    {
                        int n = ns.Read(b, got, 5 - got);
                        if (n <= 0) return;
                        got += n;
                    }
                    int cmd = b[0];
                    int arg = b[1] | (b[2] << 8) | (b[3] << 16) | (b[4] << 24);
                    if (cmd == 1) live.RequestKey();
                    else if (cmd == 2) { live.SetBitrate(arg * 1000); if (enc != null) enc.SetBitrate(arg * 1000); }
                    else if (cmd == 4) { live.Running = false; return; }
                }
            }
            catch { }
        }
    }
}
