using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace ClassInsight.Agent.Services;

/// <summary>
/// Captures the primary screen and returns a tightly-packed 32-bpp BGRA frame scaled to a
/// target width, with even dimensions (VP8/I420 requires them). Reuses its bitmaps between
/// frames so the continuous stream doesn't thrash the GC (NFR-PERF). Windows-only (GDI).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class ScreenCapture : IDisposable
{
    private readonly int _screenWidth;
    private readonly int _screenHeight;

    private Bitmap? _full;      // native-resolution grab
    private Bitmap? _scaled;    // downscaled target
    private int _scaledW, _scaledH;

    public int ScreenWidth => _screenWidth;
    public int ScreenHeight => _screenHeight;

    public ScreenCapture()
    {
        _screenWidth = Math.Max(1, NativeMethods.GetSystemMetrics(NativeMethods.SM_CXSCREEN));
        _screenHeight = Math.Max(1, NativeMethods.GetSystemMetrics(NativeMethods.SM_CYSCREEN));
    }

    /// <summary>A single captured frame: BGRA, top-down, stride = Width*4.</summary>
    public readonly record struct Frame(int Width, int Height, byte[] Bgra);

    /// <summary>
    /// Grab the screen and scale so the longest edge is at most <paramref name="maxWidth"/>
    /// wide, preserving aspect ratio. Returns null if capture fails this tick (transient;
    /// the stream loop simply skips the frame).
    /// </summary>
    public Frame? Capture(int maxWidth)
    {
        try
        {
            _full ??= new Bitmap(_screenWidth, _screenHeight, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(_full))
                g.CopyFromScreen(0, 0, 0, 0, new Size(_screenWidth, _screenHeight), CopyPixelOperation.SourceCopy);

            var (tw, th) = TargetSize(maxWidth);

            if (_scaled is null || _scaledW != tw || _scaledH != th)
            {
                _scaled?.Dispose();
                _scaled = new Bitmap(tw, th, PixelFormat.Format32bppArgb);
                _scaledW = tw; _scaledH = th;
            }

            using (var g = Graphics.FromImage(_scaled))
            {
                g.InterpolationMode = InterpolationMode.Bilinear;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                g.DrawImage(_full, new Rectangle(0, 0, tw, th));
            }

            return new Frame(tw, th, ToBgra(_scaled));
        }
        catch
        {
            // Secure desktop (UAC/lock screen) and session switches make CopyFromScreen throw.
            return null;
        }
    }

    private (int W, int H) TargetSize(int maxWidth)
    {
        var scale = Math.Min(1.0, (double)maxWidth / _screenWidth);
        var w = (int)Math.Round(_screenWidth * scale);
        var h = (int)Math.Round(_screenHeight * scale);
        // Force even dimensions (I420 chroma subsampling needs them).
        w = Math.Max(2, w & ~1);
        h = Math.Max(2, h & ~1);
        return (w, h);
    }

    private static byte[] ToBgra(Bitmap bmp)
    {
        var rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
        var data = bmp.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var stride = data.Stride;
            var tight = bmp.Width * 4;
            var buffer = new byte[tight * bmp.Height];

            if (stride == tight)
            {
                Marshal.Copy(data.Scan0, buffer, 0, buffer.Length);
            }
            else
            {
                // Drop row padding so the consumer gets a packed buffer.
                for (var y = 0; y < bmp.Height; y++)
                    Marshal.Copy(data.Scan0 + y * stride, buffer, y * tight, tight);
            }
            return buffer;
        }
        finally
        {
            bmp.UnlockBits(data);
        }
    }

    public void Dispose()
    {
        _full?.Dispose();
        _scaled?.Dispose();
    }
}
