using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace DogePet;

/// <summary>
/// Updates a layered WinForms window using premultiplied 32-bit ARGB bitmap data.
/// </summary>
internal static class LayeredWindowHelper
{
    private const int UlwAlpha = 0x02;
    private const int AcSrcOver = 0x00;
    private const int AcSrcAlpha = 0x01;

    public static void SetBitmap(Form form, Bitmap bitmap)
    {
        if (!form.IsHandleCreated)
            return;

        if (bitmap.PixelFormat != PixelFormat.Format32bppArgb)
            throw new InvalidOperationException("Layered window bitmap must be 32bpp ARGB.");

        IntPtr screenDc = GetDC(IntPtr.Zero);
        IntPtr memoryDc = CreateCompatibleDC(screenDc);

        BITMAPINFO bitmapInfo = new()
        {
            bmiHeader =
            {
                biSize = Marshal.SizeOf<BITMAPINFOHEADER>(),
                biWidth = bitmap.Width,
                biHeight = -bitmap.Height,
                biPlanes = 1,
                biBitCount = 32,
                biCompression = 0
            }
        };

        IntPtr dib = CreateDIBSection(screenDc, ref bitmapInfo, 0, out IntPtr bits, IntPtr.Zero, 0);
        if (dib == IntPtr.Zero || bits == IntPtr.Zero)
        {
            ReleaseDC(IntPtr.Zero, screenDc);
            DeleteDC(memoryDc);
            return;
        }

        IntPtr oldBitmap = SelectObject(memoryDc, dib);
        CopyPremultipliedPixels(bitmap, bits);

        var windowSize = new Size(bitmap.Width, bitmap.Height);
        var windowPos = new Point(form.Left, form.Top);
        var sourcePos = Point.Empty;
        var blend = new BlendFunction
        {
            BlendOp = AcSrcOver,
            BlendFlags = 0,
            SourceConstantAlpha = 255,
            AlphaFormat = AcSrcAlpha
        };

        UpdateLayeredWindow(
            form.Handle,
            screenDc,
            ref windowPos,
            ref windowSize,
            memoryDc,
            ref sourcePos,
            0,
            ref blend,
            UlwAlpha);

        SelectObject(memoryDc, oldBitmap);
        DeleteObject(dib);
        DeleteDC(memoryDc);
        ReleaseDC(IntPtr.Zero, screenDc);
    }

    private static void CopyPremultipliedPixels(Bitmap bitmap, IntPtr destination)
    {
        var rect = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
        BitmapData sourceData = bitmap.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);

        try
        {
            int byteCount = Math.Abs(sourceData.Stride) * sourceData.Height;
            byte[] pixels = new byte[byteCount];
            Marshal.Copy(sourceData.Scan0, pixels, 0, byteCount);

            for (int i = 0; i < byteCount; i += 4)
            {
                byte alpha = pixels[i + 3];
                if (alpha == 0)
                {
                    pixels[i] = 0;
                    pixels[i + 1] = 0;
                    pixels[i + 2] = 0;
                    continue;
                }

                pixels[i] = (byte)(pixels[i] * alpha / 255);
                pixels[i + 1] = (byte)(pixels[i + 1] * alpha / 255);
                pixels[i + 2] = (byte)(pixels[i + 2] * alpha / 255);
            }

            Marshal.Copy(pixels, 0, destination, byteCount);
        }
        finally
        {
            bitmap.UnlockBits(sourceData);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public int biSize;
        public int biWidth;
        public int biHeight;
        public short biPlanes;
        public short biBitCount;
        public int biCompression;
        public int biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public int biClrUsed;
        public int biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFO
    {
        public BITMAPINFOHEADER bmiHeader;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BlendFunction
    {
        public byte BlendOp;
        public byte BlendFlags;
        public byte SourceConstantAlpha;
        public byte AlphaFormat;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UpdateLayeredWindow(
        IntPtr hwnd,
        IntPtr hdcDst,
        ref Point pptDst,
        ref Size psize,
        IntPtr hdcSrc,
        ref Point pptSrc,
        int crKey,
        ref BlendFunction pblend,
        int dwFlags);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern bool DeleteObject(IntPtr hObject);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern bool DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr CreateDIBSection(
        IntPtr hdc,
        ref BITMAPINFO pbmi,
        uint iUsage,
        out IntPtr ppvBits,
        IntPtr hSection,
        uint dwOffset);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
}