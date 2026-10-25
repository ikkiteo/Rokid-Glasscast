using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;

namespace GlassCast
{
    /// <summary>
    /// Desktop Duplication. Reading the screen DC with BitBlt makes Windows hide the
    /// hardware cursor for every frame, which is the blink. This path never touches that DC.
    /// </summary>
    sealed class DxDesk : IDisposable
    {
        const int WaitTimeout = unchecked((int)0x887A0027);
        const int AccessLost = unchecked((int)0x887A0026);
        const int NotFound = unchecked((int)0x887A0002);

        static readonly Guid Factory1 = new Guid("770aae78-f26f-4dba-a829-253c83d1b387");
        static readonly Guid Output1 = new Guid("00cddea8-939b-4b83-a340-a685226666cc");
        static readonly Guid Tex2D = new Guid("6f15aaf2-d208-4e89-9ab4-489535d34f9c");

        IntPtr factory, output1, device, context, dup, staging;
        int outL, outT, texW, texH;
        int cropX, cropY, cropW, cropH;
        readonly int dstW, dstH;
        readonly byte[] frame;
        bool have;
        bool disposed;
        string fail = "";

        DxDesk(int dstW, int dstH)
        {
            this.dstW = dstW;
            this.dstH = dstH;
            frame = new byte[dstW * dstH * 4];
        }

        public static DxDesk Open(Rectangle src, int dstW, int dstH, out string error)
        {
            var desk = new DxDesk(dstW, dstH);
            error = desk.Attach(src);
            if (error != null)
            {
                desk.Dispose();
                return null;
            }
            return desk;
        }

        public static string Probe(Monitor mon)
        {
            var sb = new StringBuilder();
            sb.Append(mon.Name + " " + mon.Bounds.Width + "x" + mon.Bounds.Height);
            string err;
            DxDesk desk = null;
            try
            {
                desk = Open(mon.Bounds, 320, 180, out err);
                if (desk == null)
                {
                    sb.Append(" FAIL " + err);
                    return sb.ToString();
                }
                byte[] px = desk.Copy();
                int nz1 = NonZero(px);
                System.Threading.Thread.Sleep(250);
                px = desk.Copy();
                int nz2 = NonZero(px);
                sb.Append(" ok tex " + desk.texW + "x" + desk.texH + " nz " + nz1 + "/" + nz2 + " " + desk.fail);
            }
            catch (Exception e)
            {
                sb.Append(" EX " + e.GetType().Name + " " + e.Message);
            }
            finally
            {
                if (desk != null) desk.Dispose();
            }
            return sb.ToString();
        }

        static int NonZero(byte[] px)
        {
            if (px == null) return 0;
            int nz = 0;
            for (int i = 0; i < px.Length; i += 64)
                if (px[i] != 0 || px[i + 1] != 0 || px[i + 2] != 0) nz++;
            return nz;
        }

        public byte[] Copy()
        {
            if (!Pull() && !have)
            {
                Nudge();
                if (!have)
                {
                    System.Threading.Thread.Sleep(50);
                    Pull();
                }
            }
            return frame;
        }

        string Attach(Rectangle src)
        {
            try { CoInitializeEx(IntPtr.Zero, 0); } catch { }
            Guid iid = Factory1;
            int hr = CreateDXGIFactory1(ref iid, out factory);
            if (hr < 0 || factory == IntPtr.Zero) return "CreateDXGIFactory1 0x" + hr.ToString("X8");

            IntPtr bestOut = IntPtr.Zero;
            int bestArea = 0;
            int bestL = 0, bestT = 0, bestR = 0, bestB = 0;
            IntPtr bestAd = IntPtr.Zero;
            var enumAdapters = Fn<Enum1>(factory, 12);

            for (uint ai = 0; ai < 8; ai++)
            {
                IntPtr ad;
                hr = enumAdapters(factory, ai, out ad);
                if (hr == NotFound) break;
                if (hr < 0 || ad == IntPtr.Zero) return "EnumAdapters1 0x" + hr.ToString("X8");
                var enumOutputs = Fn<Enum1>(ad, 7);
                bool keepAd = false;
                for (uint oi = 0; oi < 8; oi++)
                {
                    IntPtr op;
                    hr = enumOutputs(ad, oi, out op);
                    if (hr == NotFound) break;
                    if (hr < 0 || op == IntPtr.Zero) { Release(op); continue; }
                    int l, t, r, b;
                    if (!ReadDesc(op, out l, out t, out r, out b)) { Release(op); continue; }
                    int iw = Overlap(src.Left, src.Right, l, r);
                    int ih = Overlap(src.Top, src.Bottom, t, b);
                    int area = iw * ih;
                    if (area > bestArea)
                    {
                        if (bestOut != IntPtr.Zero) Release(bestOut);
                        if (bestAd != IntPtr.Zero && bestAd != ad) Release(bestAd);
                        bestOut = op;
                        bestAd = ad;
                        keepAd = true;
                        bestArea = area;
                        bestL = l; bestT = t; bestR = r; bestB = b;
                    }
                    else Release(op);
                }
                if (!keepAd) Release(ad);
            }
            if (bestOut == IntPtr.Zero) return "DXGI не видит этот экран";

            Guid o1 = Output1;
            hr = Fn<QiFn>(bestOut, 0)(bestOut, ref o1, out output1);
            Release(bestOut);
            if (hr < 0 || output1 == IntPtr.Zero) return "IDXGIOutput1 0x" + hr.ToString("X8");

            int fl;
            hr = D3D11CreateDevice(bestAd, 0, IntPtr.Zero, 0x20, IntPtr.Zero, 0, 7, out device, out fl, out context);
            Release(bestAd);
            if (hr < 0 || device == IntPtr.Zero) return "D3D11CreateDevice 0x" + hr.ToString("X8");

            hr = Fn<DupFn>(output1, 22)(output1, device, out dup);
            if (hr < 0 || dup == IntPtr.Zero) return "DuplicateOutput 0x" + hr.ToString("X8");

            outL = bestL;
            outT = bestT;
            texW = Math.Max(1, bestR - bestL);
            texH = Math.Max(1, bestB - bestT);
            cropX = src.X - outL;
            cropY = src.Y - outT;
            cropW = src.Width;
            cropH = src.Height;
            ClampCrop();
            hr = MakeStaging(texW, texH);
            if (hr < 0) return "CreateTexture2D 0x" + hr.ToString("X8");
            return null;
        }

        bool Pull()
        {
            if (dup == IntPtr.Zero || staging == IntPtr.Zero) return false;
            var info = new FrameInfo();
            IntPtr resource;
            int hr = Fn<AcquireFn>(dup, 8)(dup, have ? 8u : 80u, ref info, out resource);
            if (hr == WaitTimeout) { fail = "timeout"; return false; }
            if (hr == AccessLost)
            {
                Release(dup);
                dup = IntPtr.Zero;
                int again = Fn<DupFn>(output1, 22)(output1, device, out dup);
                if (again < 0) fail = "AccessLost 0x" + again.ToString("X8");
                return false;
            }
            if (hr < 0)
            {
                fail = "Acquire 0x" + hr.ToString("X8");
                return false;
            }
            try
            {
                if (resource == IntPtr.Zero) return false;
                Guid tid = Tex2D;
                IntPtr tex;
                int q = Fn<QiFn>(resource, 0)(resource, ref tid, out tex);
                if (q < 0 || tex == IntPtr.Zero) return false;
                try
                {
                    var desc = new TexDesc();
                    Fn<GetTexDesc>(tex, 10)(tex, ref desc);
                    if (desc.Width > 0 && desc.Height > 0 && (desc.Width != texW || desc.Height != texH))
                    {
                        texW = desc.Width;
                        texH = desc.Height;
                        ClampCrop();
                        MakeStaging(texW, texH);
                    }
                    Fn<CopyFn>(context, 47)(context, staging, tex);
                }
                finally { Release(tex); }
            }
            finally
            {
                Release(resource);
                Fn<ReleaseFrameFn>(dup, 14)(dup);
            }

            var mapped = new Mapped();
            hr = Fn<MapFn>(context, 14)(context, staging, 0, 1, 0, out mapped);
            if (hr < 0 || mapped.pData == IntPtr.Zero)
            {
                fail = "Map 0x" + hr.ToString("X8");
                return false;
            }
            try { Scale(mapped.pData, (int)mapped.RowPitch); }
            finally { Fn<UnmapFn>(context, 15)(context, staging, 0); }
            have = true;
            fail = "frame";
            return true;
        }

        void ClampCrop()
        {
            if (cropX < 0) { cropW += cropX; cropX = 0; }
            if (cropY < 0) { cropH += cropY; cropY = 0; }
            if (cropX + cropW > texW) cropW = texW - cropX;
            if (cropY + cropH > texH) cropH = texH - cropY;
            if (cropW < 1) cropW = 1;
            if (cropH < 1) cropH = 1;
            if (cropX >= texW) cropX = 0;
            if (cropY >= texH) cropY = 0;
        }

        int MakeStaging(int w, int h)
        {
            if (staging != IntPtr.Zero) { Release(staging); staging = IntPtr.Zero; }
            var desc = new TexDesc();
            desc.Width = w;
            desc.Height = h;
            desc.MipLevels = 1;
            desc.ArraySize = 1;
            desc.Format = 87;
            desc.SampleCount = 1;
            desc.SampleQuality = 0;
            desc.Usage = 3;
            desc.BindFlags = 0;
            desc.CPUAccessFlags = 0x20000;
            desc.MiscFlags = 0;
            return Fn<CreateTex>(device, 5)(device, ref desc, IntPtr.Zero, out staging);
        }

        unsafe void Scale(IntPtr src, int pitch)
        {
            if (pitch < texW * 4) pitch = texW * 4;
            int sw = cropW;
            int sh = cropH;
            float s = Math.Min(dstW / (float)sw, dstH / (float)sh);
            int dw = Math.Max(1, (int)(sw * s));
            int dh = Math.Max(1, (int)(sh * s));
            if (dw > dstW) dw = dstW;
            if (dh > dstH) dh = dstH;
            int ox = (dstW - dw) / 2;
            int oy = (dstH - dh) / 2;
            Array.Clear(frame, 0, frame.Length);
            byte* p = (byte*)src;
            fixed (byte* d0 = frame)
            {
                for (int y = 0; y < dh; y++)
                {
                    int sy = cropY + (int)((long)y * sh / dh);
                    byte* row = p + sy * pitch + cropX * 4;
                    byte* dst = d0 + ((oy + y) * dstW + ox) * 4;
                    for (int x = 0; x < dw; x++)
                    {
                        byte* pix = row + (int)((long)x * sw / dw) * 4;
                        dst[0] = pix[0];
                        dst[1] = pix[1];
                        dst[2] = pix[2];
                        dst[3] = 255;
                        dst += 4;
                    }
                }
            }
        }

        void Nudge()
        {
            IntPtr hwnd = IntPtr.Zero;
            try
            {
                hwnd = CreateWindowEx(0x08000080, "Static", "", unchecked((int)0x80000000), outL, outT, 2, 2, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
                if (hwnd == IntPtr.Zero) return;
                ShowWindow(hwnd, 8);
                UpdateWindow(hwnd);
                System.Threading.Thread.Sleep(30);
                Pull();
            }
            catch { }
            finally
            {
                if (hwnd != IntPtr.Zero) DestroyWindow(hwnd);
            }
        }

        static int Overlap(int a0, int a1, int b0, int b1)
        {
            int l = a0 > b0 ? a0 : b0;
            int r = a1 < b1 ? a1 : b1;
            return r > l ? r - l : 0;
        }

        static bool ReadDesc(IntPtr output, out int l, out int t, out int r, out int b)
        {
            l = t = r = b = 0;
            IntPtr mem = Marshal.AllocHGlobal(128);
            try
            {
                for (int i = 0; i < 128; i++) Marshal.WriteByte(mem, i, 0);
                int hr = Fn<GetDescFn>(output, 7)(output, mem);
                if (hr < 0) return false;
                l = Marshal.ReadInt32(mem, 64);
                t = Marshal.ReadInt32(mem, 68);
                r = Marshal.ReadInt32(mem, 72);
                b = Marshal.ReadInt32(mem, 76);
                return r > l && b > t;
            }
            finally { Marshal.FreeHGlobal(mem); }
        }

        static T Fn<T>(IntPtr com, int slot) where T : class
        {
            IntPtr vt = Marshal.ReadIntPtr(com);
            IntPtr fp = Marshal.ReadIntPtr(vt, slot * IntPtr.Size);
            return (T)(object)Marshal.GetDelegateForFunctionPointer(fp, typeof(T));
        }

        static void Release(IntPtr p)
        {
            if (p == IntPtr.Zero) return;
            try { Fn<RelFn>(p, 2)(p); } catch { }
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            Release(staging);
            Release(dup);
            Release(context);
            Release(device);
            Release(output1);
            Release(factory);
            staging = dup = context = device = output1 = factory = IntPtr.Zero;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 8)]
        struct FrameInfo
        {
            public long LastPresentTime;
            public long LastMouseUpdateTime;
            public uint AccumulatedFrames;
            public int RectsCoalesced;
            public int ProtectedContentMaskedOut;
            public int PointerX;
            public int PointerY;
            public int PointerVisible;
            public uint TotalMetadataBufferSize;
            public uint PointerShapeBufferSize;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct TexDesc
        {
            public int Width;
            public int Height;
            public int MipLevels;
            public int ArraySize;
            public int Format;
            public int SampleCount;
            public int SampleQuality;
            public int Usage;
            public uint BindFlags;
            public uint CPUAccessFlags;
            public uint MiscFlags;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct Mapped
        {
            public IntPtr pData;
            public uint RowPitch;
            public uint DepthPitch;
        }

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        delegate int QiFn(IntPtr self, ref Guid iid, out IntPtr pp);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        delegate uint RelFn(IntPtr self);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        delegate int Enum1(IntPtr self, uint index, out IntPtr item);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        delegate int GetDescFn(IntPtr self, IntPtr desc);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        delegate int DupFn(IntPtr self, IntPtr device, out IntPtr dup);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        delegate int AcquireFn(IntPtr self, uint timeout, ref FrameInfo info, out IntPtr resource);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        delegate int ReleaseFrameFn(IntPtr self);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        delegate int CreateTex(IntPtr self, ref TexDesc desc, IntPtr initial, out IntPtr tex);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        delegate void GetTexDesc(IntPtr self, ref TexDesc desc);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        delegate void CopyFn(IntPtr self, IntPtr dst, IntPtr src);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        delegate int MapFn(IntPtr self, IntPtr resource, uint sub, int mapType, uint flags, out Mapped mapped);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        delegate void UnmapFn(IntPtr self, IntPtr resource, uint sub);

        [DllImport("dxgi.dll")]
        static extern int CreateDXGIFactory1(ref Guid iid, out IntPtr factory);

        [DllImport("d3d11.dll")]
        static extern int D3D11CreateDevice(IntPtr adapter, int driverType, IntPtr software, uint flags, IntPtr levels, uint nlevels, uint sdk, out IntPtr device, out int featureLevel, out IntPtr context);

        [DllImport("ole32.dll")]
        static extern int CoInitializeEx(IntPtr reserved, uint coinit);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr CreateWindowEx(int ex, string cls, string name, int style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr inst, IntPtr param);

        [DllImport("user32.dll")]
        static extern bool DestroyWindow(IntPtr hwnd);

        [DllImport("user32.dll")]
        static extern bool ShowWindow(IntPtr hwnd, int cmd);

        [DllImport("user32.dll")]
        static extern bool UpdateWindow(IntPtr hwnd);
    }
}
