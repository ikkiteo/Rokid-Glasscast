using System;
using System.Runtime.InteropServices;

namespace GlassCast
{
    sealed class H264Encoder : IDisposable
    {
        readonly int width, height, fps;
        readonly long duration;
        IMFTransform xform;
        ICodecAPI api;
        MFT_OUTPUT_STREAM_INFO outInfo;
        bool provides;
        long pts;
        bool started;

        public H264Encoder(int width, int height, int fps, int bitrate)
        {
            this.width = Native.Align(width, 16);
            this.height = Native.Align(height, 16);
            this.fps = fps < 1 ? 30 : fps;
            duration = 10000000L / this.fps;
            Mf.CoInitializeEx(IntPtr.Zero, 2);
            int hr = Mf.MFStartup(Mf.MF_VERSION, 0);
            if (hr < 0) throw new InvalidOperationException("MFStartup 0x" + hr.ToString("X"));
            xform = CreateEncoder();
            Configure(bitrate);
            try { api = (ICodecAPI)xform; } catch { api = null; }
            Tune(bitrate);
            xform.GetOutputStreamInfo(0, out outInfo);
            provides = (outInfo.dwFlags & Mf.MFT_OUTPUT_STREAM_PROVIDES_SAMPLES) != 0
                || (outInfo.dwFlags & Mf.MFT_OUTPUT_STREAM_CAN_PROVIDE_SAMPLES) != 0;
            xform.ProcessMessage(0x10000000, IntPtr.Zero); // MFT_MESSAGE_NOTIFY_BEGIN_STREAMING
            xform.ProcessMessage(0x10000001, IntPtr.Zero); // MFT_MESSAGE_NOTIFY_START_OF_STREAM
            started = true;
        }

        public void ForceKey()
        {
            if (api == null) return;
            try
            {
                Guid g = Mf.CODECAPI_AVEncVideoForceKeyFrame;
                object one = 1;
                api.SetValue(ref g, ref one);
            }
            catch { }
        }

        public void SetBitrate(int bitrate)
        {
            if (api == null) return;
            try
            {
                Guid g = Mf.CODECAPI_AVEncCommonMeanBitRate;
                object v = bitrate;
                api.SetValue(ref g, ref v);
            }
            catch { }
        }

        public byte[] Encode(byte[] nv12, out bool keyframe)
        {
            keyframe = false;
            IMFMediaBuffer buf;
            int hr = Mf.MFCreateMemoryBuffer(nv12.Length, out buf);
            if (hr < 0) throw new InvalidOperationException("buffer 0x" + hr.ToString("X"));
            IntPtr ptr;
            int max, cur;
            buf.Lock(out ptr, out max, out cur);
            Marshal.Copy(nv12, 0, ptr, nv12.Length);
            buf.Unlock();
            buf.SetCurrentLength(nv12.Length);

            IMFSample sample;
            hr = Mf.MFCreateSample(out sample);
            if (hr < 0) throw new InvalidOperationException("sample 0x" + hr.ToString("X"));
            sample.AddBuffer(buf);
            sample.SetSampleTime(pts);
            sample.SetSampleDuration(duration);
            pts += duration;

            hr = xform.ProcessInput(0, sample, 0);
            Marshal.ReleaseComObject(sample);
            Marshal.ReleaseComObject(buf);
            if (hr == Mf.MF_E_NOTACCEPTING)
            {
                byte[] leftover = Drain(out keyframe);
                IMFSample extra = MakeInput(nv12);
                hr = xform.ProcessInput(0, extra, 0);
                Marshal.ReleaseComObject(extra);
                if (hr < 0 && hr != Mf.MF_E_NOTACCEPTING) Throw("ProcessInput", hr);
                if (leftover != null) return leftover;
            }
            else if (hr < 0) Throw("ProcessInput", hr);

            return Drain(out keyframe);
        }

        IMFSample MakeInput(byte[] nv12)
        {
            IMFMediaBuffer buf;
            Mf.MFCreateMemoryBuffer(nv12.Length, out buf);
            IntPtr ptr; int max, cur;
            buf.Lock(out ptr, out max, out cur);
            Marshal.Copy(nv12, 0, ptr, nv12.Length);
            buf.Unlock();
            buf.SetCurrentLength(nv12.Length);
            IMFSample sample;
            Mf.MFCreateSample(out sample);
            sample.AddBuffer(buf);
            sample.SetSampleTime(pts - duration);
            sample.SetSampleDuration(duration);
            Marshal.ReleaseComObject(buf);
            return sample;
        }

        byte[] Drain(out bool keyframe)
        {
            keyframe = false;
            byte[] last = null;
            for (int n = 0; n < 4; n++)
            {
                var data = new MFT_OUTPUT_DATA_BUFFER[1];
                data[0].dwStreamID = 0;
                IMFSample owned = null;
                if (!provides)
                {
                    int size = outInfo.cbSize > 0 ? outInfo.cbSize : width * height;
                    IMFMediaBuffer buf;
                    Mf.MFCreateMemoryBuffer(size, out buf);
                    Mf.MFCreateSample(out owned);
                    owned.AddBuffer(buf);
                    Marshal.ReleaseComObject(buf);
                    data[0].pSample = owned;
                }
                int status;
                int hr = xform.ProcessOutput(0, 1, data, out status);
                if (hr == Mf.MF_E_TRANSFORM_NEED_MORE_INPUT)
                {
                    if (owned != null) Marshal.ReleaseComObject(owned);
                    break;
                }
                if (hr == Mf.MF_E_TRANSFORM_STREAM_CHANGE)
                {
                    if (owned != null) Marshal.ReleaseComObject(owned);
                    AcceptNewOutput();
                    continue;
                }
                if (hr < 0)
                {
                    if (owned != null) Marshal.ReleaseComObject(owned);
                    Throw("ProcessOutput", hr);
                }
                IMFSample outSample = data[0].pSample;
                if (outSample != null)
                {
                    last = ReadSample(outSample, out keyframe);
                    Marshal.ReleaseComObject(outSample);
                }
            }
            return last;
        }

        void AcceptNewOutput()
        {
            IMFMediaType type;
            xform.GetOutputAvailableType(0, 0, out type);
            xform.SetOutputType(0, type, 0);
            Marshal.ReleaseComObject(type);
            xform.GetOutputStreamInfo(0, out outInfo);
            provides = (outInfo.dwFlags & Mf.MFT_OUTPUT_STREAM_PROVIDES_SAMPLES) != 0;
        }

        static byte[] ReadSample(IMFSample sample, out bool keyframe)
        {
            keyframe = false;
            try
            {
                Guid clean = new Guid("9cdf01d8-a0ac-4263-8d23-8ccaccdc18bb"); // MFSampleExtension_CleanPoint
                int v;
                sample.GetUINT32(ref clean, out v);
                keyframe = v != 0;
            }
            catch { }
            IMFMediaBuffer buf;
            sample.ConvertToContiguousBuffer(out buf);
            IntPtr ptr; int max, cur;
            buf.Lock(out ptr, out max, out cur);
            byte[] bytes = new byte[cur];
            if (cur > 0) Marshal.Copy(ptr, bytes, 0, cur);
            buf.Unlock();
            Marshal.ReleaseComObject(buf);
            return AnnexB.Ensure(bytes);
        }

        IMFTransform CreateEncoder()
        {
            IMFTransform t = FromClsid(Mf.CLSID_CMSH264EncoderMFT);
            if (t != null) return t;
            var info = new MFT_REGISTER_TYPE_INFO();
            info.guidMajorType = Mf.MFMediaType_Video;
            info.guidSubtype = Mf.MFVideoFormat_H264;
            t = TryEnum(info, Mf.MFT_ENUM_FLAG_ALL | Mf.MFT_ENUM_FLAG_SORTANDFILTER);
            if (t != null) return t;
            t = TryEnum(info, Mf.MFT_ENUM_FLAG_SYNCMFT);
            if (t != null) return t;
            t = TryEnum(info, Mf.MFT_ENUM_FLAG_HARDWARE | Mf.MFT_ENUM_FLAG_ASYNCMFT);
            if (t != null) return t;
            throw new InvalidOperationException(Lang.T("в Windows нет H.264 кодировщика (поставь Media Feature Pack)", "Windows has no H.264 encoder (install the Media Feature Pack)"));
        }

        public static int LastCreateHr;
        public static string LastCreateNote = "";

        static IMFTransform FromClsid(Guid clsid)
        {
            try
            {
                Guid c = clsid;
                Guid iid = Mf.IID_IUnknown;
                IntPtr p;
                int hr = Mf.CoCreateInstance(ref c, IntPtr.Zero, Mf.CLSCTX_INPROC_SERVER, ref iid, out p);
                LastCreateHr = hr;
                if (hr < 0 || p == IntPtr.Zero)
                {
                    LastCreateNote = "cocreate 0x" + hr.ToString("X");
                    return null;
                }
                object obj = Marshal.GetObjectForIUnknown(p);
                Marshal.Release(p);
                LastCreateNote = "cocreate ok";
                IMFTransform direct = obj as IMFTransform;
                if (direct != null) return direct;
                var act = obj as IMFActivate;
                if (act != null)
                {
                    Guid want = Mf.IID_IUnknown;
                    object opened;
                    act.ActivateObject(ref want, out opened);
                    LastCreateNote = "cocreate activate ok";
                    return (IMFTransform)opened;
                }
                try { return (IMFTransform)obj; }
                catch (InvalidCastException)
                {
                    LastCreateNote = "no IMFTransform on encoder object";
                    return null;
                }
            }
            catch (Exception e)
            {
                LastCreateNote = e.GetType().Name + ": " + e.Message;
                return null;
            }
        }

        static IMFTransform TryEnum(MFT_REGISTER_TYPE_INFO info, uint flags)
        {
            IntPtr array;
            uint count;
            int hr = Mf.MFTEnumEx(Mf.MFT_CATEGORY_VIDEO_ENCODER, flags, IntPtr.Zero, ref info, out array, out count);
            if (hr < 0 || count == 0 || array == IntPtr.Zero) return null;
            IMFTransform found = null;
            try
            {
                for (int i = 0; i < count; i++)
                {
                    IntPtr p = Marshal.ReadIntPtr(array, i * IntPtr.Size);
                    if (p == IntPtr.Zero) continue;
                    try
                    {
                        IntPtr opened = Mf.RawActivate(p);
                        IntPtr xf = Mf.Qi(opened, Mf.IID_IMFTransform);
                        Marshal.Release(opened);
                        Marshal.Release(p);
                        if (xf == IntPtr.Zero) continue;
                        object obj = Marshal.GetObjectForIUnknown(xf);
                        Marshal.Release(xf);
                        found = (IMFTransform)obj;
                        LastCreateNote = "activate #" + i + " ok";
                        break;
                    }
                    catch (Exception e)
                    {
                        LastCreateNote = "activate #" + i + " " + e.Message;
                        try { Marshal.Release(p); } catch { }
                    }
                }
            }
            finally
            {
                Marshal.FreeCoTaskMem(array);
            }
            return found;
        }

        void Configure(int bitrate)
        {
            IMFMediaType outType;
            Mf.MFCreateMediaType(out outType);
            Guid major = Mf.MFMediaType_Video;
            Guid sub = Mf.MFVideoFormat_H264;
            Guid k1 = Mf.MF_MT_MAJOR_TYPE, k2 = Mf.MF_MT_SUBTYPE;
            outType.SetGUID(ref k1, ref major);
            outType.SetGUID(ref k2, ref sub);
            Guid br = Mf.MF_MT_AVG_BITRATE;
            outType.SetUINT32(ref br, bitrate);
            Guid inter = Mf.MF_MT_INTERLACE_MODE;
            outType.SetUINT32(ref inter, 2);
            SetSize(outType, Mf.MF_MT_FRAME_SIZE, width, height);
            SetSize(outType, Mf.MF_MT_FRAME_RATE, fps, 1);
            SetSize(outType, Mf.MF_MT_PIXEL_ASPECT_RATIO, 1, 1);
            xform.SetOutputType(0, outType, 0);
            Marshal.ReleaseComObject(outType);

            IMFMediaType inType;
            Mf.MFCreateMediaType(out inType);
            Guid nv = Mf.MFVideoFormat_NV12;
            inType.SetGUID(ref k1, ref major);
            inType.SetGUID(ref k2, ref nv);
            inType.SetUINT32(ref inter, 2);
            SetSize(inType, Mf.MF_MT_FRAME_SIZE, width, height);
            SetSize(inType, Mf.MF_MT_FRAME_RATE, fps, 1);
            SetSize(inType, Mf.MF_MT_PIXEL_ASPECT_RATIO, 1, 1);
            Guid stride = Mf.MF_MT_DEFAULT_STRIDE;
            inType.SetUINT32(ref stride, width);
            xform.SetInputType(0, inType, 0);
            Marshal.ReleaseComObject(inType);
        }

        void Tune(int bitrate)
        {
            if (api == null) return;
            TrySet(Mf.CODECAPI_AVLowLatencyMode, true);
            TrySet(Mf.CODECAPI_AVEncCommonRateControlMode, 1); // peak constrained
            TrySet(Mf.CODECAPI_AVEncCommonMeanBitRate, bitrate);
            TrySet(Mf.CODECAPI_AVEncMPVGOPSize, fps);
            TrySet(Mf.CODECAPI_AVEncCommonQualityVsSpeed, 0);
        }

        void TrySet(Guid key, object value)
        {
            try { api.SetValue(ref key, ref value); } catch { }
        }

        static void SetSize(IMFMediaType t, Guid key, int a, int b)
        {
            Guid k = key;
            ulong v = ((ulong)(uint)a << 32) | (uint)b;
            t.SetUINT64(ref k, v);
        }

        static void Throw(string where, int hr)
        {
            throw new InvalidOperationException(where + " 0x" + hr.ToString("X"));
        }

        public void Dispose()
        {
            if (started)
            {
                try { xform.ProcessMessage(0x10000002, IntPtr.Zero); } catch { } // END_OF_STREAM
                try { xform.ProcessMessage(0x00000001, IntPtr.Zero); } catch { } // COMMAND_DRAIN
            }
            if (api != null) try { Marshal.ReleaseComObject(api); } catch { }
            if (xform != null) try { Marshal.ReleaseComObject(xform); } catch { }
            api = null;
            xform = null;
            try { Mf.MFShutdown(); } catch { }
        }
    }

    static class AnnexB
    {
        public static byte[] Ensure(byte[] data)
        {
            if (data == null || data.Length < 4) return data;
            if (data[0] == 0 && data[1] == 0 && (data[2] == 1 || (data[2] == 0 && data[3] == 1)))
                return data;
            return FromAvcc(data);
        }

        static byte[] FromAvcc(byte[] data)
        {
            var outb = new System.Collections.Generic.List<byte>(data.Length + 16);
            int i = 0;
            while (i + 4 <= data.Length)
            {
                int n = (data[i] << 24) | (data[i + 1] << 16) | (data[i + 2] << 8) | data[i + 3];
                i += 4;
                if (n <= 0 || i + n > data.Length) return data;
                outb.Add(0); outb.Add(0); outb.Add(0); outb.Add(1);
                for (int k = 0; k < n; k++) outb.Add(data[i + k]);
                i += n;
            }
            return outb.ToArray();
        }

        public static bool LooksConfig(byte[] nal)
        {
            int t = NalType(nal);
            return t == 7 || t == 8;
        }

        public static bool LooksIdr(byte[] nal)
        {
            int i = 0;
            while (i + 4 < nal.Length)
            {
                int t = NalTypeAt(nal, ref i);
                if (t == 5) return true;
                if (t < 0) break;
            }
            return false;
        }

        static int NalType(byte[] nal)
        {
            int i = 0;
            return NalTypeAt(nal, ref i);
        }

        static int NalTypeAt(byte[] nal, ref int i)
        {
            while (i + 4 < nal.Length)
            {
                if (nal[i] == 0 && nal[i + 1] == 0 && nal[i + 2] == 1)
                {
                    int t = nal[i + 3] & 0x1F;
                    i += 4;
                    return t;
                }
                if (nal[i] == 0 && nal[i + 1] == 0 && nal[i + 2] == 0 && nal[i + 3] == 1 && i + 5 <= nal.Length)
                {
                    int t = nal[i + 4] & 0x1F;
                    i += 5;
                    return t;
                }
                i++;
            }
            return -1;
        }
    }
}
