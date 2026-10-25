using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace GlassCast
{
    static class Native
    {
        public const int SRCCOPY = 0x00CC0020;
        public const int CAPTUREBLT = 0x40000000;
        public const int PW_RENDERFULLCONTENT = 0x00000002;
        public const int BI_RGB = 0;
        public const int DIB_RGB_COLORS = 0;
        public const uint MONITORINFOF_PRIMARY = 1;

        [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
        [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr hwnd);
        [DllImport("user32.dll")] public static extern IntPtr GetWindowDC(IntPtr hwnd);
        [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);
        [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr hwnd, out RECT r);
        [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out RECT r);
        [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);
        [DllImport("user32.dll")] public static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc proc, IntPtr data);
        [DllImport("user32.dll", CharSet = CharSet.Auto)] public static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFOEX info);
        [DllImport("gdi32.dll")] public static extern IntPtr CreateCompatibleDC(IntPtr hdc);
        [DllImport("gdi32.dll")] public static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int w, int h);
        [DllImport("gdi32.dll")] public static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);
        [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr obj);
        [DllImport("gdi32.dll")] public static extern bool DeleteDC(IntPtr hdc);
        [DllImport("gdi32.dll")] public static extern bool BitBlt(IntPtr dst, int x, int y, int w, int h, IntPtr src, int sx, int sy, int rop);
        [DllImport("gdi32.dll")] public static extern bool StretchBlt(IntPtr dst, int x, int y, int w, int h, IntPtr src, int sx, int sy, int sw, int sh, int rop);
        [DllImport("gdi32.dll")] public static extern int SetStretchBltMode(IntPtr hdc, int mode);
        public const int COLORONCOLOR = 3;
        public const int HALFTONE = 4;
        [DllImport("gdi32.dll")] public static extern bool SetBrushOrgEx(IntPtr hdc, int x, int y, IntPtr prev);
        [DllImport("gdi32.dll")] public static extern IntPtr CreateSolidBrush(int color);
        [DllImport("user32.dll")] public static extern int FillRect(IntPtr hdc, ref RECT r, IntPtr brush);
        [DllImport("gdi32.dll")] public static extern int GetDIBits(IntPtr hdc, IntPtr bmp, int start, int lines, byte[] bits, ref BITMAPINFO info, int usage);
        [DllImport("gdi32.dll")] public static extern int GetDIBits(IntPtr hdc, IntPtr bmp, int start, int lines, IntPtr bits, ref BITMAPINFO info, int usage);

        [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc proc, IntPtr l);
        public delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr l);
        [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hwnd);
        [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr hwnd);
        [DllImport("user32.dll")] public static extern int GetWindowTextLength(IntPtr hwnd);
        [DllImport("user32.dll", CharSet = CharSet.Auto)] public static extern int GetWindowText(IntPtr hwnd, System.Text.StringBuilder text, int max);
        [DllImport("user32.dll", CharSet = CharSet.Auto)] public static extern int GetClassName(IntPtr hwnd, System.Text.StringBuilder text, int max);
        [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr hwnd);
        [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out RECT rect, int size);
        public const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;

        public delegate bool MonitorEnumProc(IntPtr monitor, IntPtr hdc, ref RECT rect, IntPtr data);

        public const int DISPLAY_DEVICE_ACTIVE = 0x00000001;

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern bool EnumDisplayDevices(string device, uint devNum, ref DISPLAY_DEVICE dd, uint flags);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        public struct DISPLAY_DEVICE
        {
            public int cb;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string DeviceName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string DeviceString;
            public int StateFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string DeviceID;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string DeviceKey;
        }

        public static Dictionary<string, string> MonitorNames()
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var adapter = new DISPLAY_DEVICE();
            adapter.cb = Marshal.SizeOf(typeof(DISPLAY_DEVICE));
            for (uint i = 0; i < 32; i++)
            {
                if (!EnumDisplayDevices(null, i, ref adapter, 0)) break;
                string dev = adapter.DeviceName;
                var mon = new DISPLAY_DEVICE();
                mon.cb = Marshal.SizeOf(typeof(DISPLAY_DEVICE));
                string label = null;
                if (!string.IsNullOrEmpty(adapter.DeviceString))
                {
                    string a = adapter.DeviceString.ToLowerInvariant();
                    if (a.IndexOf("mobile monitor") >= 0 || a.IndexOf("usbmm") >= 0 || a.IndexOf("virtual display") >= 0 || a.IndexOf("glasscast") >= 0 || a.IndexOf("amyuni") >= 0)
                        label = adapter.DeviceString;
                }
                if (label == null && !string.IsNullOrEmpty(dev) && EnumDisplayDevices(dev, 0, ref mon, 0))
                {
                    if (!string.IsNullOrEmpty(mon.DeviceString)) label = mon.DeviceString;
                }
                if (label != null && !string.IsNullOrEmpty(dev)) map[dev] = label;
                adapter = new DISPLAY_DEVICE();
                adapter.cb = Marshal.SizeOf(typeof(DISPLAY_DEVICE));
            }
            return map;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        public struct MONITORINFOEX
        {
            public int Size;
            public RECT Monitor;
            public RECT Work;
            public uint Flags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string Device;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct BITMAPINFOHEADER
        {
            public int biSize, biWidth, biHeight;
            public short biPlanes, biBitCount;
            public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct BITMAPINFO
        {
            public BITMAPINFOHEADER bmiHeader;
            public int bmiColors;
        }

        public static int Align(int v, int step)
        {
            int n = (v / step) * step;
            return n < step ? step : n;
        }
    }

    static class Mf
    {
        public const int MF_VERSION = 0x00020070;
        public const int MFT_ENUM_FLAG_SYNCMFT = 0x00000001;
        public const int MFT_ENUM_FLAG_ASYNCMFT = 0x00000002;
        public const int MFT_ENUM_FLAG_HARDWARE = 0x00000004;
        public const int MFT_ENUM_FLAG_SORTANDFILTER = 0x00000040;
        public const int MFT_ENUM_FLAG_ALL = 0x0000003F;
        public const int MFT_OUTPUT_STREAM_PROVIDES_SAMPLES = 0x00000008;
        public const int MFT_OUTPUT_STREAM_CAN_PROVIDE_SAMPLES = 0x00000100;
        public const int METransformNeedInput = 0x00000200;
        public static readonly Guid MFMediaType_Video = new Guid("73646976-0000-0010-8000-00AA00389B71");
        public static readonly Guid MFVideoFormat_H264 = new Guid("34363248-0000-0010-8000-00AA00389B71");
        public static readonly Guid MFVideoFormat_NV12 = new Guid("3231564E-0000-0010-8000-00AA00389B71");
        public static readonly Guid MF_MT_MAJOR_TYPE = new Guid("48eba18e-f8c9-4687-bf11-0a74c9f96a8f");
        public static readonly Guid MF_MT_SUBTYPE = new Guid("f7e34c9a-42e8-4714-b74b-cb29d72c35e5");
        public static readonly Guid MF_MT_FRAME_SIZE = new Guid("1652c33d-d6b2-4012-b834-72030849a37d");
        public static readonly Guid MF_MT_FRAME_RATE = new Guid("c459a2e8-3d80-4a38-9c91-d73c93ecba0c");
        public static readonly Guid MF_MT_PIXEL_ASPECT_RATIO = new Guid("c6376a1e-8d0a-4027-9830-4277f2891827");
        public static readonly Guid MF_MT_AVG_BITRATE = new Guid("20332624-fb0d-4d9e-bd0d-cbf6786c102e");
        public static readonly Guid MF_MT_INTERLACE_MODE = new Guid("e2724bb8-e676-4806-b4b2-a8d6efb44ccd");
        public static readonly Guid MF_MT_DEFAULT_STRIDE = new Guid("644b4e48-1e02-4516-b0eb-c01ca9d49ac6");
        public static readonly Guid MFT_CATEGORY_VIDEO_ENCODER = new Guid("f79eac7d-e545-4387-bdee-d647d7bde42a");
        public static readonly Guid CODECAPI_AVLowLatencyMode = new Guid("9c27891a-ed7a-40e1-88e8-b22727a024ee");
        public static readonly Guid CODECAPI_AVEncCommonRateControlMode = new Guid("1c0608e9-370c-4710-8a58-cb6181c42423");
        public static readonly Guid CODECAPI_AVEncCommonMeanBitRate = new Guid("f7222374-2144-4815-b550-a37f8e12ee63");
        public static readonly Guid CODECAPI_AVEncMPVGOPSize = new Guid("95f31b26-95f5-4848-8961-dd01076d25ad");
        public static readonly Guid CODECAPI_AVEncVideoForceKeyFrame = new Guid("398c1b98-8353-475a-9ef2-8f265d260345");
        public static readonly Guid CODECAPI_AVEncCommonQualityVsSpeed = new Guid("98332df8-03cd-488b-924a-14e143f8f356");
        public static readonly Guid MF_TRANSFORM_ASYNC_UNLOCK = new Guid("e5666d6b-3422-4eb6-a421-da7db1f8e207");
        public static readonly Guid MFT_FRIENDLY_NAME = new Guid("314ffba9-5b74-4768-9ca4-6f1d81a0bc56");
        public static readonly Guid IID_IMFTransform = new Guid("bf94c121-5b05-4e6f-88ba-dfc1573d4afa");
        public static readonly Guid IID_ICodecAPI = new Guid("901db4c7-31ce-41a2-85dc-8fa0bf41b8da");
        public static readonly Guid IID_IUnknown = new Guid("00000000-0000-0000-C000-000000000046");
        public const int MF_E_TRANSFORM_NEED_MORE_INPUT = unchecked((int)0xC00D6D72);
        public const int MF_E_TRANSFORM_STREAM_CHANGE = unchecked((int)0xC00D6D61);
        public const int MF_E_NOTACCEPTING = unchecked((int)0xC00D6D80);

        public static readonly Guid CLSID_CMSH264EncoderMFT = new Guid("6CA50344-051A-4DED-9779-A43305165E35");
        public const uint CLSCTX_INPROC_SERVER = 1;

        [DllImport("ole32.dll")] public static extern int CoInitializeEx(IntPtr reserved, int flags);
        [DllImport("ole32.dll", ExactSpelling = true, PreserveSig = true)]
        public static extern int CoCreateInstance(ref Guid clsid, IntPtr outer, uint ctx, ref Guid iid, out IntPtr ppv);
        [DllImport("mfplat.dll")] public static extern int MFStartup(int version, int flags);
        [DllImport("mfplat.dll")] public static extern int MFShutdown();
        [DllImport("mfplat.dll")] public static extern int MFCreateMediaType(out IMFMediaType type);
        [DllImport("mfplat.dll")] public static extern int MFCreateMemoryBuffer(int size, out IMFMediaBuffer buffer);
        [DllImport("mfplat.dll")] public static extern int MFCreateSample(out IMFSample sample);
        [DllImport("mfplat.dll")] public static extern int MFTEnumEx(
            Guid category, uint flags,
            IntPtr input, ref MFT_REGISTER_TYPE_INFO output,
            out IntPtr activateArray, out uint count);

        public static void SetSize(IMFAttributes a, Guid key, int w, int h)
        {
            ulong v = ((ulong)(uint)w << 32) | (uint)h;
            a.SetUINT64(key, v);
        }

        public static void SetRatio(IMFAttributes a, Guid key, int num, int den)
        {
            ulong v = ((ulong)(uint)num << 32) | (uint)den;
            a.SetUINT64(key, v);
        }

        public static IntPtr Qi(IntPtr unk, Guid iid)
        {
            if (unk == IntPtr.Zero) return IntPtr.Zero;
            IntPtr vtbl = Marshal.ReadIntPtr(unk);
            IntPtr fn = Marshal.ReadIntPtr(vtbl, 0);
            var qi = (QiDel)Marshal.GetDelegateForFunctionPointer(fn, typeof(QiDel));
            Guid g = iid;
            IntPtr p;
            int hr = qi(unk, ref g, out p);
            return hr < 0 ? IntPtr.Zero : p;
        }

        public static string Probe(IntPtr unk)
        {
            var sb = new System.Text.StringBuilder();
            TryQi(sb, unk, "IMFTransform", IID_IMFTransform);
            TryQi(sb, unk, "ICodecAPI", IID_ICodecAPI);
            TryQi(sb, unk, "IMFActivate", new Guid("7FEE9E9A-4A89-47A6-899C-B6A53A70FB67"));
            TryQi(sb, unk, "IMFAttributes", new Guid("2cd2d921-c447-44a7-a13c-4adabfc247e3"));
            TryQi(sb, unk, "IMFMediaEventGenerator", new Guid("2CD0BD52-BCD5-4B89-B62C-EADC0C031E7D"));
            TryQi(sb, unk, "IMediaObject", new Guid("d8ad0f58-5494-4102-97c5-ec798e59bcf4"));
            TryQi(sb, unk, "IMFGetService", new Guid("fa993888-4383-415a-a930-dd472a8cf6f7"));
            TryQi(sb, unk, "IMFRealTimeClient", new Guid("2347d60b-3dd7-4e36-9b6e-6d6d0eccd4fa"));
            return sb.ToString();
        }

        static void TryQi(System.Text.StringBuilder sb, IntPtr unk, string name, Guid iid)
        {
            IntPtr p = Qi(unk, iid);
            sb.Append(name).Append('=').Append(p != IntPtr.Zero ? "yes" : "no").Append(' ');
            if (p != IntPtr.Zero) Marshal.Release(p);
        }

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        delegate int QiDel(IntPtr self, ref Guid iid, out IntPtr ppv);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        delegate int ActivateDel(IntPtr self, ref Guid iid, out IntPtr ppv);

        public static IntPtr RawActivate(IntPtr activate)
        {
            UnlockAsync(activate);
            IntPtr vtbl = Marshal.ReadIntPtr(activate);
            IntPtr fn = Marshal.ReadIntPtr(vtbl, 33 * IntPtr.Size);
            var del = (ActivateDel)Marshal.GetDelegateForFunctionPointer(fn, typeof(ActivateDel));
            Guid unk = IID_IUnknown;
            IntPtr p;
            int hr = del(activate, ref unk, out p);
            if (hr < 0) throw new InvalidOperationException("ActivateObject 0x" + hr.ToString("X"));
            return p;
        }

        public static void UnlockAsync(IntPtr attrs)
        {
            IntPtr vtbl = Marshal.ReadIntPtr(attrs);
            IntPtr fn = Marshal.ReadIntPtr(vtbl, 21 * IntPtr.Size);
            var del = (SetUint32Del)Marshal.GetDelegateForFunctionPointer(fn, typeof(SetUint32Del));
            Guid key = MF_TRANSFORM_ASYNC_UNLOCK;
            del(attrs, ref key, 1);
        }

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        delegate int SetUint32Del(IntPtr self, ref Guid key, int value);
    }

    [StructLayout(LayoutKind.Sequential)]
    struct MFT_REGISTER_TYPE_INFO
    {
        public Guid guidMajorType;
        public Guid guidSubtype;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct MFT_OUTPUT_STREAM_INFO
    {
        public uint dwFlags;
        public int cbSize;
        public int cbAlignment;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct MFT_INPUT_STREAM_INFO
    {
        public long hnsMaxLatency;
        public uint dwFlags;
        public int cbSize;
        public int cbMaxLookahead;
        public int cbAlignment;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct MFT_OUTPUT_DATA_BUFFER
    {
        public int dwStreamID;
        public IMFSample pSample;
        public int dwStatus;
        public IntPtr pEvents;
    }

    [ComImport, Guid("2cd2d921-c447-44a7-a13c-4adabfc247e3"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMFAttributes
    {
        void GetItem(ref Guid guidKey, IntPtr pValue);
        void GetItemType(ref Guid guidKey, out int pType);
        void CompareItem(ref Guid guidKey, IntPtr value, out int pbResult);
        void Compare(IMFAttributes pTheirs, int match, out int pbResult);
        void GetUINT32(ref Guid guidKey, out int punValue);
        void GetUINT64(ref Guid guidKey, out ulong punValue);
        void GetDouble(ref Guid guidKey, out double pfValue);
        void GetGUID(ref Guid guidKey, out Guid pguidValue);
        void GetStringLength(ref Guid guidKey, out int pcchLength);
        void GetString(ref Guid guidKey, [Out] char[] pwszValue, int cchBufSize, IntPtr pcchLength);
        void GetAllocatedString(ref Guid guidKey, out IntPtr ppwszValue, out int pcchLength);
        void GetBlobSize(ref Guid guidKey, out int pcbBlobSize);
        void GetBlob(ref Guid guidKey, [Out] byte[] pBuf, int cbBufSize, IntPtr pcbBlobSize);
        void GetAllocatedBlob(ref Guid guidKey, out IntPtr ppBuf, out int pcbSize);
        void GetUnknown(ref Guid guidKey, ref Guid riid, out IntPtr ppv);
        void SetItem(ref Guid guidKey, IntPtr value);
        void DeleteItem(ref Guid guidKey);
        void DeleteAllItems();
        void SetUINT32(ref Guid guidKey, int unValue);
        void SetUINT64(ref Guid guidKey, ulong unValue);
        void SetDouble(ref Guid guidKey, double fValue);
        void SetGUID(ref Guid guidKey, ref Guid guidValue);
        void SetString(ref Guid guidKey, [MarshalAs(UnmanagedType.LPWStr)] string wszValue);
        void SetBlob(ref Guid guidKey, [In] byte[] pBuf, int cbBufSize);
        void SetUnknown(ref Guid guidKey, [MarshalAs(UnmanagedType.IUnknown)] object pUnknown);
        void LockStore();
        void UnlockStore();
        void GetCount(out int pcItems);
        void GetItemByIndex(int unIndex, out Guid pguidKey, IntPtr pValue);
        void CopyAllItems(IMFAttributes pDest);
    }

    [ComImport, Guid("44ae0fa8-ea31-4109-8d2e-4cae499e8b13"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMFMediaType
    {
        void GetItem(ref Guid guidKey, IntPtr pValue);
        void GetItemType(ref Guid guidKey, out int pType);
        void CompareItem(ref Guid guidKey, IntPtr value, out int pbResult);
        void Compare(IMFAttributes pTheirs, int match, out int pbResult);
        void GetUINT32(ref Guid guidKey, out int punValue);
        void GetUINT64(ref Guid guidKey, out ulong punValue);
        void GetDouble(ref Guid guidKey, out double pfValue);
        void GetGUID(ref Guid guidKey, out Guid pguidValue);
        void GetStringLength(ref Guid guidKey, out int pcchLength);
        void GetString(ref Guid guidKey, [Out] char[] pwszValue, int cchBufSize, IntPtr pcchLength);
        void GetAllocatedString(ref Guid guidKey, out IntPtr ppwszValue, out int pcchLength);
        void GetBlobSize(ref Guid guidKey, out int pcbBlobSize);
        void GetBlob(ref Guid guidKey, [Out] byte[] pBuf, int cbBufSize, IntPtr pcbBlobSize);
        void GetAllocatedBlob(ref Guid guidKey, out IntPtr ppBuf, out int pcbSize);
        void GetUnknown(ref Guid guidKey, ref Guid riid, out IntPtr ppv);
        void SetItem(ref Guid guidKey, IntPtr value);
        void DeleteItem(ref Guid guidKey);
        void DeleteAllItems();
        void SetUINT32(ref Guid guidKey, int unValue);
        void SetUINT64(ref Guid guidKey, ulong unValue);
        void SetDouble(ref Guid guidKey, double fValue);
        void SetGUID(ref Guid guidKey, ref Guid guidValue);
        void SetString(ref Guid guidKey, [MarshalAs(UnmanagedType.LPWStr)] string wszValue);
        void SetBlob(ref Guid guidKey, [In] byte[] pBuf, int cbBufSize);
        void SetUnknown(ref Guid guidKey, [MarshalAs(UnmanagedType.IUnknown)] object pUnknown);
        void LockStore();
        void UnlockStore();
        void GetCount(out int pcItems);
        void GetItemByIndex(int unIndex, out Guid pguidKey, IntPtr pValue);
        void CopyAllItems(IMFAttributes pDest);
        void GetMajorType(out Guid pguidMajorType);
        void IsCompressedFormat(out int pfCompressed);
        void IsEqual(IMFMediaType pIMediaType, out int pdwFlags);
        void GetRepresentation(Guid guidRepresentation, out IntPtr ppvRepresentation);
        void FreeRepresentation(Guid guidRepresentation, IntPtr pvRepresentation);
    }

    [ComImport, Guid("c40a00f2-b93a-4d80-ae8c-5a1c634f58e4"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMFSample
    {
        void GetItem(ref Guid guidKey, IntPtr pValue);
        void GetItemType(ref Guid guidKey, out int pType);
        void CompareItem(ref Guid guidKey, IntPtr value, out int pbResult);
        void Compare(IMFAttributes pTheirs, int match, out int pbResult);
        void GetUINT32(ref Guid guidKey, out int punValue);
        void GetUINT64(ref Guid guidKey, out ulong punValue);
        void GetDouble(ref Guid guidKey, out double pfValue);
        void GetGUID(ref Guid guidKey, out Guid pguidValue);
        void GetStringLength(ref Guid guidKey, out int pcchLength);
        void GetString(ref Guid guidKey, [Out] char[] pwszValue, int cchBufSize, IntPtr pcchLength);
        void GetAllocatedString(ref Guid guidKey, out IntPtr ppwszValue, out int pcchLength);
        void GetBlobSize(ref Guid guidKey, out int pcbBlobSize);
        void GetBlob(ref Guid guidKey, [Out] byte[] pBuf, int cbBufSize, IntPtr pcbBlobSize);
        void GetAllocatedBlob(ref Guid guidKey, out IntPtr ppBuf, out int pcbSize);
        void GetUnknown(ref Guid guidKey, ref Guid riid, out IntPtr ppv);
        void SetItem(ref Guid guidKey, IntPtr value);
        void DeleteItem(ref Guid guidKey);
        void DeleteAllItems();
        void SetUINT32(ref Guid guidKey, int unValue);
        void SetUINT64(ref Guid guidKey, ulong unValue);
        void SetDouble(ref Guid guidKey, double fValue);
        void SetGUID(ref Guid guidKey, ref Guid guidValue);
        void SetString(ref Guid guidKey, [MarshalAs(UnmanagedType.LPWStr)] string wszValue);
        void SetBlob(ref Guid guidKey, [In] byte[] pBuf, int cbBufSize);
        void SetUnknown(ref Guid guidKey, [MarshalAs(UnmanagedType.IUnknown)] object pUnknown);
        void LockStore();
        void UnlockStore();
        void GetCount(out int pcItems);
        void GetItemByIndex(int unIndex, out Guid pguidKey, IntPtr pValue);
        void CopyAllItems(IMFAttributes pDest);
        void GetSampleFlags(out int pdwSampleFlags);
        void SetSampleFlags(int dwSampleFlags);
        void GetSampleTime(out long phnsSampleTime);
        void SetSampleTime(long hnsSampleTime);
        void GetSampleDuration(out long phnsSampleDuration);
        void SetSampleDuration(long hnsSampleDuration);
        void GetBufferCount(out int pdwBufferCount);
        void GetBufferByIndex(int dwIndex, out IMFMediaBuffer ppBuffer);
        void ConvertToContiguousBuffer(out IMFMediaBuffer ppBuffer);
        void AddBuffer(IMFMediaBuffer pBuffer);
        void RemoveBufferByIndex(int dwIndex);
        void RemoveAllBuffers();
        void GetTotalLength(out int pcbTotalLength);
        void CopyToBuffer(IMFMediaBuffer pBuffer);
    }

    [ComImport, Guid("045fa593-8799-42b8-bc8d-8968c6453507"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMFMediaBuffer
    {
        void Lock(out IntPtr ppbBuffer, out int pcbMaxLength, out int pcbCurrentLength);
        void Unlock();
        void GetCurrentLength(out int pcbCurrentLength);
        void SetCurrentLength(int cbCurrentLength);
        void GetMaxLength(out int pcbMaxLength);
    }

    [ComImport, Guid("bf94c121-5b05-4e6f-88ba-dfc1573d4afa"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMFTransform
    {
        void GetStreamLimits(out int pdwInputMinimum, out int pdwInputMaximum, out int pdwOutputMinimum, out int pdwOutputMaximum);
        void GetStreamCount(out int pcInputStreams, out int pcOutputStreams);
        void GetStreamIDs(int dwInputIDArraySize, int[] pdwInputIDs, int dwOutputIDArraySize, int[] pdwOutputIDs);
        void GetInputStreamInfo(int dwInputStreamID, out MFT_INPUT_STREAM_INFO pStreamInfo);
        void GetOutputStreamInfo(int dwOutputStreamID, out MFT_OUTPUT_STREAM_INFO pStreamInfo);
        void GetAttributes(out IMFAttributes pAttributes);
        void GetInputStreamAttributes(int dwInputStreamID, out IMFAttributes pAttributes);
        void GetOutputStreamAttributes(int dwOutputStreamID, out IMFAttributes pAttributes);
        void DeleteInputStream(int dwStreamID);
        void AddInputStreams(int cStreams, int[] adwStreamIDs);
        void GetInputAvailableType(int dwInputStreamID, int dwTypeIndex, out IMFMediaType ppType);
        void GetOutputAvailableType(int dwOutputStreamID, int dwTypeIndex, out IMFMediaType ppType);
        void SetInputType(int dwInputStreamID, IMFMediaType pType, int dwFlags);
        void SetOutputType(int dwOutputStreamID, IMFMediaType pType, int dwFlags);
        void GetInputCurrentType(int dwInputStreamID, out IMFMediaType ppType);
        void GetOutputCurrentType(int dwOutputStreamID, out IMFMediaType ppType);
        void GetInputStatus(int dwInputStreamID, out int pdwFlags);
        void GetOutputStatus(out int pdwFlags);
        void SetOutputBounds(long hnsLowerBound, long hnsUpperBound);
        void ProcessEvent(int dwInputStreamID, IntPtr pEvent);
        void ProcessMessage(int eMessage, IntPtr ulParam);
        [PreserveSig] int ProcessInput(int dwInputStreamID, IMFSample pSample, int dwFlags);
        [PreserveSig] int ProcessOutput(int dwFlags, int cOutputBufferCount, [In, Out] MFT_OUTPUT_DATA_BUFFER[] pOutputSamples, out int pdwStatus);
    }

    [ComImport, Guid("7FED0016-AE1E-47C8-9767-E6116082FD19"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface ICodecAPI
    {
        void IsSupported(ref Guid Api);
        void IsModifiable(ref Guid Api);
        void GetParameterRange(ref Guid Api, out object ValueMin, out object ValueMax, out object SteppingDelta);
        void GetParameterValues(ref Guid Api, out IntPtr Values, out uint ValuesCount);
        void GetDefaultValue(ref Guid Api, out object Value);
        void GetValue(ref Guid Api, out object Value);
        void SetValue(ref Guid Api, ref object Value);
        void RegisterForEvent(ref Guid Api, IntPtr userData);
        void UnregisterForEvent(ref Guid Api);
        void SetAllDefaults();
        void SetValueWithNotify(ref Guid Api, ref object Value, out IntPtr ChangedParam, out int ChangedParamCount);
        void SetAllDefaultsWithNotify(out IntPtr ChangedParam, out int ChangedParamCount);
        void GetAllSettings(IntPtr pStream);
        void SetAllSettings(IntPtr pStream);
        void SetAllSettingsWithNotify(IntPtr pStream, out IntPtr ChangedParam, out int ChangedParamCount);
    }

    [ComImport, Guid("7FEE9E9A-4A89-47A6-899C-B6A53A70FB67"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMFActivate
    {
        void GetItem(ref Guid guidKey, IntPtr pValue);
        void GetItemType(ref Guid guidKey, out int pType);
        void CompareItem(ref Guid guidKey, IntPtr value, out int pbResult);
        void Compare(IMFAttributes pTheirs, int match, out int pbResult);
        void GetUINT32(ref Guid guidKey, out int punValue);
        void GetUINT64(ref Guid guidKey, out ulong punValue);
        void GetDouble(ref Guid guidKey, out double pfValue);
        void GetGUID(ref Guid guidKey, out Guid pguidValue);
        void GetStringLength(ref Guid guidKey, out int pcchLength);
        void GetString(ref Guid guidKey, [Out] char[] pwszValue, int cchBufSize, IntPtr pcchLength);
        void GetAllocatedString(ref Guid guidKey, out IntPtr ppwszValue, out int pcchLength);
        void GetBlobSize(ref Guid guidKey, out int pcbBlobSize);
        void GetBlob(ref Guid guidKey, [Out] byte[] pBuf, int cbBufSize, IntPtr pcbBlobSize);
        void GetAllocatedBlob(ref Guid guidKey, out IntPtr ppBuf, out int pcbSize);
        void GetUnknown(ref Guid guidKey, ref Guid riid, out IntPtr ppv);
        void SetItem(ref Guid guidKey, IntPtr value);
        void DeleteItem(ref Guid guidKey);
        void DeleteAllItems();
        void SetUINT32(ref Guid guidKey, int unValue);
        void SetUINT64(ref Guid guidKey, ulong unValue);
        void SetDouble(ref Guid guidKey, double fValue);
        void SetGUID(ref Guid guidKey, ref Guid guidValue);
        void SetString(ref Guid guidKey, [MarshalAs(UnmanagedType.LPWStr)] string wszValue);
        void SetBlob(ref Guid guidKey, [In] byte[] pBuf, int cbBufSize);
        void SetUnknown(ref Guid guidKey, [MarshalAs(UnmanagedType.IUnknown)] object pUnknown);
        void LockStore();
        void UnlockStore();
        void GetCount(out int pcItems);
        void GetItemByIndex(int unIndex, out Guid pguidKey, IntPtr pValue);
        void CopyAllItems(IMFAttributes pDest);
        void ActivateObject(ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out object ppv);
        void ShutdownObject();
        void DetachObject();
    }
}
