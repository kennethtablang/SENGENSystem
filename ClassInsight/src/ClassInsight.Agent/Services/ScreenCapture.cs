using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using ClassInsight.Shared.Contracts;
using static ClassInsight.Agent.Services.NativeMethods;

namespace ClassInsight.Agent.Services;

/// <summary>A display as the capture layer sees it: the wire description plus its desktop rectangle.</summary>
public readonly record struct CaptureDisplay(DisplayInfo Info, int Left, int Top, int Width, int Height);

/// <summary>Enumerates attached displays, primary first (FR-AGENT-04).</summary>
[SupportedOSPlatform("windows")]
public static class DisplayEnumerator
{
    public static IReadOnlyList<CaptureDisplay> GetDisplays()
    {
        var found = new List<(RECT Rect, bool Primary, string Name)>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr hMon, IntPtr hdc, ref RECT clip, IntPtr data) =>
        {
            var mi = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>() };
            if (GetMonitorInfo(hMon, ref mi))
                found.Add((mi.rcMonitor, (mi.dwFlags & MONITORINFOF_PRIMARY) != 0, mi.szDevice));
            return true;
        }, IntPtr.Zero);

        if (found.Count == 0)
        {
            // Headless/odd session: fall back to the primary screen metrics.
            var w = Math.Max(1, GetSystemMetrics(SM_CXSCREEN));
            var h = Math.Max(1, GetSystemMetrics(SM_CYSCREEN));
            found.Add((new RECT { Right = w, Bottom = h }, true, "DISPLAY"));
        }

        return found
            .OrderByDescending(d => d.Primary)
            .ThenBy(d => d.Rect.Left).ThenBy(d => d.Rect.Top)
            .Select((d, i) => new CaptureDisplay(
                new DisplayInfo
                {
                    Index = i,
                    Width = d.Rect.Width,
                    Height = d.Rect.Height,
                    IsPrimary = d.Primary,
                    Name = d.Name,
                },
                d.Rect.Left, d.Rect.Top, d.Rect.Width, d.Rect.Height))
            .ToArray();
    }
}

/// <summary>
/// Captures one display and returns a tightly-packed 32-bpp BGRA frame scaled to about a target
/// width, with both dimensions multiples of 16 — the managed VP8 encoder only accepts whole
/// macroblocks (it rejects e.g. 480×270), so sizes are rounded and the aspect ratio shifts by
/// at most a few percent.
///
/// The image comes from DXGI Desktop Duplication when available (GPU capture + GPU mip
/// downsampling, ~2.5 ms/frame, free when nothing changed; see <see cref="DxgiDuplicator"/>),
/// otherwise from one GDI <c>StretchBlt</c> (HALFTONE) straight from the screen. Either way the
/// final resize is a HALFTONE stretch into a reused target DIB section, and the
/// mouse cursor is drawn on top so the teacher can see where the student is pointing. Two
/// buffers alternate so the caller can skip encoding frames that haven't changed (NFR-PERF-04).
/// Requires per-monitor DPI awareness (set in Program) so scaled displays are captured whole.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class ScreenCapture : IDisposable
{
    private readonly CaptureDisplay _display;

    private IntPtr _memDc;
    private IntPtr _dib;
    private IntPtr _oldObj;
    private IntPtr _bits;
    private int _w, _h;

    private byte[] _current = [];
    private byte[] _previous = [];
    private bool _haveFrame;

    // DXGI path (GPU capture) and the DIB that receives its downsampled image.
    private DxgiDuplicator? _dxgi;
    private DateTime _dxgiRetryAt;
    private IntPtr _srcDc, _srcDib, _srcOld, _srcBits;
    private int _srcW, _srcH, _srcLevel = -1;
    private bool _srcValid;

    /// <summary>Force GDI capture everywhere (diagnostics: <c>ClassInsight:CaptureBackend=GDI</c>).</summary>
    public static bool DisableDxgi { get; set; }

    public ScreenCapture(CaptureDisplay display) => _display = display;

    public CaptureDisplay Display => _display;

    /// <summary>
    /// A captured frame: BGRA, top-down, stride = Width*4. <see cref="Bgra"/> is owned by the
    /// capture: it stays valid while the <i>next</i> <see cref="Capture"/> runs (so a caller may
    /// encode frame N while capturing N+1) and is overwritten by the one after that.
    /// <see cref="Changed"/> is false when the pixels equal the previous frame's.
    /// </summary>
    public readonly record struct Frame(int Width, int Height, byte[] Bgra, bool Changed);

    /// <summary>
    /// Grab the display scaled to at most <paramref name="maxWidth"/> wide (aspect preserved).
    /// Returns null if capture fails this tick — e.g. the secure desktop (UAC / lock screen) is
    /// showing — which the stream loop treats as a skipped frame.
    /// </summary>
    public Frame? Capture(int maxWidth)
    {
        var (tw, th) = TargetSize(maxWidth);
        if (!EnsureSurface(tw, th)) return null;

        SetStretchBltMode(_memDc, HALFTONE);
        SetBrushOrgEx(_memDc, 0, 0, IntPtr.Zero);

        if (TryDxgi(tw, th, out var noChange, out var srcW, out var srcH))
        {
            // Nothing on screen changed (not even the pointer): hand back the last frame, no work.
            if (noChange && _haveFrame && _previous.Length == tw * th * 4)
                return new Frame(tw, th, _previous, false);

            // GPU-downsampled image -> final size (a small CPU-side stretch, no screen readback).
            if (!StretchBlt(_memDc, 0, 0, tw, th, _srcDc, 0, 0, srcW, srcH, SRCCOPY))
                return null;
        }
        else
        {
            var screen = GetDC(IntPtr.Zero);
            if (screen == IntPtr.Zero) return null;
            try
            {
                if (!StretchBlt(_memDc, 0, 0, tw, th, screen,
                        _display.Left, _display.Top, _display.Width, _display.Height, SRCCOPY | CAPTUREBLT))
                    return null;
            }
            finally
            {
                ReleaseDC(IntPtr.Zero, screen);
            }
        }

        DrawCursor(tw);
        GdiFlush();

        var len = tw * th * 4;
        if (_current.Length != len) { _current = new byte[len]; _previous = new byte[len]; }
        Marshal.Copy(_bits, _current, 0, len);

        var changed = !_haveFrame || !_current.AsSpan().SequenceEqual(_previous);
        (_current, _previous) = (_previous, _current);   // _previous now holds this frame
        _haveFrame = true;
        return new Frame(tw, th, _previous, changed);
    }

    /// <summary>"DXGI" while GPU duplication is active for this display, else "GDI".</summary>
    public string Backend => _dxgi is null ? "GDI" : "DXGI";

    /// <summary>
    /// Try the DXGI Desktop Duplication path (see <see cref="DxgiDuplicator"/>). On success the
    /// GPU-downsampled image is in the source DIB (<paramref name="srcW"/> x <paramref name="srcH"/>),
    /// or <paramref name="noChange"/> says the screen is unchanged. Returns false to use GDI for
    /// this frame: duplication unavailable, lost (it is retried later), or not primed yet.
    /// </summary>
    private bool TryDxgi(int tw, int th, out bool noChange, out int srcW, out int srcH)
    {
        noChange = false;
        srcW = srcH = 0;
        if (DisableDxgi) return false;

        if (_dxgi is null)
        {
            if (DateTime.UtcNow < _dxgiRetryAt) return false;
            _dxgi = DxgiDuplicator.TryCreate(_display.Info.Name);
            // Only trust it if it describes the same pixels we enumerated (guards odd multi-GPU setups).
            if (_dxgi is null || _dxgi.Width != _display.Width || _dxgi.Height != _display.Height)
            {
                _dxgi?.Dispose();
                _dxgi = null;
                _dxgiRetryAt = DateTime.UtcNow.AddSeconds(5);
                return false;
            }
            _srcValid = false;
        }

        var level = _dxgi.LevelFor(tw, th);
        srcW = Math.Max(1, _dxgi.Width >> level);
        srcH = Math.Max(1, _dxgi.Height >> level);
        if (!EnsureSource(srcW, srcH)) return false;
        if (level != _srcLevel) { _srcValid = false; _srcLevel = level; }

        switch (_dxgi.TryAcquire(level, _srcBits, srcW * 4))
        {
            case DxgiDuplicator.Outcome.NewImage:
                _srcValid = true;
                return true;
            case DxgiDuplicator.Outcome.PointerOnly:
                return _srcValid;   // recomposite the cursor over the last image
            case DxgiDuplicator.Outcome.NoChange:
                noChange = _srcValid;
                return _srcValid;
            default:
                // Lost (mode change, secure desktop...): drop it, use GDI, retry shortly.
                _dxgi.Dispose();
                _dxgi = null;
                _srcValid = false;
                _dxgiRetryAt = DateTime.UtcNow.AddSeconds(2);
                return false;
        }
    }

    private bool EnsureSource(int w, int h)
    {
        if (_srcDib != IntPtr.Zero && _srcW == w && _srcH == h) return true;
        FreeSource();

        var screen = GetDC(IntPtr.Zero);
        if (screen == IntPtr.Zero) return false;
        try
        {
            _srcDc = CreateCompatibleDC(screen);
            var bmi = new BITMAPINFOHEADER
            {
                biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(),
                biWidth = w,
                biHeight = -h,
                biPlanes = 1,
                biBitCount = 32,
            };
            _srcDib = CreateDIBSection(screen, ref bmi, DIB_RGB_COLORS, out _srcBits, IntPtr.Zero, 0);
            if (_srcDc == IntPtr.Zero || _srcDib == IntPtr.Zero || _srcBits == IntPtr.Zero)
            {
                FreeSource();
                return false;
            }
            _srcOld = SelectObject(_srcDc, _srcDib);
            _srcW = w; _srcH = h;
            _srcValid = false;
            return true;
        }
        finally
        {
            ReleaseDC(IntPtr.Zero, screen);
        }
    }

    private void FreeSource()
    {
        if (_srcDc != IntPtr.Zero && _srcOld != IntPtr.Zero) SelectObject(_srcDc, _srcOld);
        if (_srcDib != IntPtr.Zero) DeleteObject(_srcDib);
        if (_srcDc != IntPtr.Zero) DeleteDC(_srcDc);
        _srcDc = _srcDib = _srcOld = _srcBits = IntPtr.Zero;
        _srcW = _srcH = 0;
        _srcValid = false;
    }

    private void DrawCursor(int targetWidth)
    {
        try
        {
            var ci = new CURSORINFO { cbSize = Marshal.SizeOf<CURSORINFO>() };
            if (!GetCursorInfo(ref ci) || (ci.flags & CURSOR_SHOWING) == 0 || ci.hCursor == IntPtr.Zero) return;

            var x = ci.ptScreenPos.X - _display.Left;
            var y = ci.ptScreenPos.Y - _display.Top;
            if (x < 0 || y < 0 || x >= _display.Width || y >= _display.Height) return;   // on another display

            int hotX = 0, hotY = 0;
            if (GetIconInfo(ci.hCursor, out var info))
            {
                hotX = info.xHotspot; hotY = info.yHotspot;
                if (info.hbmMask != IntPtr.Zero) DeleteObject(info.hbmMask);
                if (info.hbmColor != IntPtr.Zero) DeleteObject(info.hbmColor);
            }

            var scale = (double)targetWidth / _display.Width;
            // Keep the pointer legible on small thumbnails.
            var size = Math.Max(16, (int)Math.Round(32 * scale));
            var cursorScale = size / 32.0;
            DrawIconEx(_memDc,
                (int)Math.Round(x * scale - hotX * cursorScale),
                (int)Math.Round(y * scale - hotY * cursorScale),
                ci.hCursor, size, size, 0, IntPtr.Zero, DI_NORMAL);
        }
        catch
        {
            // Cursor is a nicety; never fail a frame over it.
        }
    }

    private bool EnsureSurface(int w, int h)
    {
        if (_dib != IntPtr.Zero && _w == w && _h == h) return true;
        FreeSurface();

        var screen = GetDC(IntPtr.Zero);
        if (screen == IntPtr.Zero) return false;
        try
        {
            _memDc = CreateCompatibleDC(screen);
            var bmi = new BITMAPINFOHEADER
            {
                biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(),
                biWidth = w,
                biHeight = -h,          // negative = top-down rows
                biPlanes = 1,
                biBitCount = 32,
                biCompression = 0,      // BI_RGB → BGRA byte order
            };
            _dib = CreateDIBSection(screen, ref bmi, DIB_RGB_COLORS, out _bits, IntPtr.Zero, 0);
            if (_memDc == IntPtr.Zero || _dib == IntPtr.Zero || _bits == IntPtr.Zero)
            {
                FreeSurface();
                return false;
            }
            _oldObj = SelectObject(_memDc, _dib);
            _w = w; _h = h;
            return true;
        }
        finally
        {
            ReleaseDC(IntPtr.Zero, screen);
        }
    }

    internal (int W, int H) TargetSize(int maxWidth) => TargetSize(_display.Width, _display.Height, maxWidth);

    /// <summary>Scale to at most <paramref name="maxWidth"/> wide, then round each side to a multiple of 16.</summary>
    internal static (int W, int H) TargetSize(int width, int height, int maxWidth)
    {
        var scale = Math.Min(1.0, (double)maxWidth / width);
        static int Mb(double v) => Math.Max(16, (int)Math.Round(v / 16.0) * 16);
        var w = Math.Min(Mb(width * scale), Math.Max(16, maxWidth / 16 * 16));
        return (w, Mb(height * scale));
    }

    private void FreeSurface()
    {
        if (_memDc != IntPtr.Zero && _oldObj != IntPtr.Zero) SelectObject(_memDc, _oldObj);
        if (_dib != IntPtr.Zero) DeleteObject(_dib);
        if (_memDc != IntPtr.Zero) DeleteDC(_memDc);
        _memDc = _dib = _oldObj = _bits = IntPtr.Zero;
        _w = _h = 0;
    }

    public void Dispose()
    {
        FreeSurface();
        FreeSource();
        _dxgi?.Dispose();
        _dxgi = null;
    }
}
