using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

namespace GlassCast
{
    sealed class JpegEnc : IDisposable
    {
        readonly int width, height;
        readonly ImageCodecInfo codec;
        readonly EncoderParameters args;
        readonly Bitmap bmp;
        readonly Graphics gfx;
        readonly MemoryStream ms;
        bool disposed;

        public int Width { get { return width; } }
        public int Height { get { return height; } }

        public JpegEnc(int width, int height, int quality)
        {
            this.width = width;
            this.height = height;
            bmp = new Bitmap(width, height, PixelFormat.Format32bppRgb);
            gfx = Graphics.FromImage(bmp);
            gfx.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
            gfx.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBilinear;
            codec = Find();
            args = new EncoderParameters(1);
            SetQuality(quality);
            ms = new MemoryStream(128 * 1024);
        }

        public void SetQuality(int quality)
        {
            if (quality < 40) quality = 40;
            if (quality > 92) quality = 92;
            args.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, (long)quality);
        }

        public IntPtr GetHdc()
        {
            return gfx.GetHdc();
        }

        public void ReleaseHdc(IntPtr hdc)
        {
            gfx.ReleaseHdc(hdc);
        }

        public byte[] Encode()
        {
            ms.SetLength(0);
            bmp.Save(ms, codec, args);
            return ms.ToArray();
        }

        public byte[] EncodeBgra(byte[] bgra)
        {
            var rect = new Rectangle(0, 0, width, height);
            BitmapData data = bmp.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppRgb);
            try
            {
                int dstStride = data.Stride;
                int srcStride = width * 4;
                IntPtr dst = data.Scan0;
                if (dstStride == srcStride)
                    System.Runtime.InteropServices.Marshal.Copy(bgra, 0, dst, srcStride * height);
                else
                {
                    for (int y = 0; y < height; y++)
                        System.Runtime.InteropServices.Marshal.Copy(bgra, y * srcStride, new IntPtr(dst.ToInt64() + y * dstStride), srcStride);
                }
            }
            finally { bmp.UnlockBits(data); }
            return Encode();
        }

        static ImageCodecInfo Find()
        {
            foreach (var c in ImageCodecInfo.GetImageEncoders())
            {
                if (c.FormatID == ImageFormat.Jpeg.Guid) return c;
            }
            throw new InvalidOperationException("нет JPEG кодировщика GDI+");
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            gfx.Dispose();
            bmp.Dispose();
            args.Dispose();
            ms.Dispose();
        }
    }
}
