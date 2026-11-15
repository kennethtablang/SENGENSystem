using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using SharpGen.Runtime;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using static Vortice.Direct3D11.D3D11;
using static Vortice.DXGI.DXGI;

namespace ClassInsight.Agent.Services;

/// <summary>
/// GPU screen capture for one display via DXGI Desktop Duplication (NFR-PERF-04).
///
/// Instead of reading the whole screen back to the CPU every frame (GDI: ~27–45 ms and ~10 % of a
/// core for a 5 fps thumbnail), the desktop image stays on the GPU: it is copied into a texture
/// with a mip chain, the GPU averages it down (<c>GenerateMips</c>), and only the small mip level
/// nearest the target size is read back — about 2.5 ms per frame. When nothing on screen has
/// changed, DXGI says so and no work is done at all.
///
/// Duplication can be unavailable or lost (secure desktop, display mode change, too many
/// duplications of one output); callers fall back to GDI capture whenever a method returns
/// <see cref="Outcome.Failed"/>.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class DxgiDuplicator : IDisposable
{
    public enum Outcome { NewImage, PointerOnly, NoChange, Failed }

    private readonly string _deviceName;
    private ID3D11Device? _device;
    private ID3D11DeviceContext? _ctx;
    private IDXGIOutputDuplication? _dup;
    private ID3D11Texture2D? _mipTex;
    private ID3D11ShaderResourceView? _srv;
    private ID3D11Texture2D? _staging;
    private int _stagingLevel = -1;

    public int Width { get; private set; }
    public int Height { get; private set; }

    public DxgiDuplicator(string deviceName) => _deviceName = deviceName;

    /// <summary>
    /// Try to start duplicating the output named <paramref name="deviceName"/> (e.g. <c>\\.\DISPLAY1</c>).
    /// Returns null if DXGI duplication isn't available for it.
    /// </summary>
    public static DxgiDuplicator? TryCreate(string? deviceName)
    {
        if (string.IsNullOrEmpty(deviceName)) return null;
        var d = new DxgiDuplicator(deviceName);
        if (d.Init()) return d;
        d.Dispose();
        return null;
    }

    private bool Init()
    {
        try
        {
            if (CreateDXGIFactory1(out IDXGIFactory1? factory).Failure || factory is null) return false;
            using (factory)
            {
                for (uint a = 0; factory.EnumAdapters1(a, out IDXGIAdapter1 adapter).Success; a++)
                {
                    using (adapter)
                    {
                        for (uint o = 0; adapter.EnumOutputs(o, out IDXGIOutput output).Success; o++)
                        {
                            using (output)
                            {
                                var desc = output.Description;
                                if (!string.Equals(desc.DeviceName, _deviceName, StringComparison.OrdinalIgnoreCase)) continue;

                                Width = desc.DesktopCoordinates.Right - desc.DesktopCoordinates.Left;
                                Height = desc.DesktopCoordinates.Bottom - desc.DesktopCoordinates.Top;

                                if (D3D11CreateDevice(adapter, DriverType.Unknown, DeviceCreationFlags.BgraSupport,
                                        [FeatureLevel.Level_11_0, FeatureLevel.Level_10_1, FeatureLevel.Level_10_0],
                                        out _device, out _ctx).Failure || _device is null)
                                    return false;

                                using var output1 = output.QueryInterface<IDXGIOutput1>();
                                _dup = output1.DuplicateOutput(_device);

                                _mipTex = _device.CreateTexture2D(new Texture2DDescription
                                {
                                    Width = (uint)Width,
                                    Height = (uint)Height,
                                    MipLevels = 0,   // full chain
                                    ArraySize = 1,
                                    Format = Format.B8G8R8A8_UNorm,
                                    SampleDescription = new SampleDescription(1, 0),
                                    Usage = ResourceUsage.Default,
                                    BindFlags = BindFlags.RenderTarget | BindFlags.ShaderResource,
                                    MiscFlags = ResourceOptionFlags.GenerateMips,
                                });
                                _srv = _device.CreateShaderResourceView(_mipTex);
                                return true;
                            }
                        }
                    }
                }
            }
        }
        catch (SharpGenException)
        {
            // E_ACCESSDENIED (secure desktop / not the input desktop), NOT_CURRENTLY_AVAILABLE
            // (too many duplications), unsupported driver… → GDI fallback.
        }
        return false;
    }

    /// <summary>
    /// The mip level to read for a target of <paramref name="tw"/>×<paramref name="th"/>: the
    /// smallest level that is still at least the target size (a few rows of tolerance, since
    /// targets are rounded to multiples of 16).
    /// </summary>
    public int LevelFor(int tw, int th) => LevelFor(Width, Height, tw, th);

    internal static int LevelFor(int width, int height, int tw, int th)
    {
        var level = 0;
        while ((width >> (level + 1)) >= tw && (height >> (level + 1)) >= th - 4 && (width >> (level + 1)) > 0)
            level++;
        return level;
    }

    /// <summary>
    /// Poll for a new desktop image without blocking. On <see cref="Outcome.NewImage"/>, the chosen
    /// mip level has been copied into <paramref name="dst"/> (BGRA, top-down, <paramref name="dstStride"/>
    /// bytes per row, sized <c>(Width&gt;&gt;level)×(Height&gt;&gt;level)</c>).
    /// </summary>
    public unsafe Outcome TryAcquire(int level, IntPtr dst, int dstStride)
    {
        if (_dup is null || _ctx is null || _mipTex is null || _srv is null) return Outcome.Failed;

        IDXGIResource? res = null;
        try
        {
            var hr = _dup.AcquireNextFrame(0, out var info, out res);
            if (hr == Vortice.DXGI.ResultCode.WaitTimeout) return Outcome.NoChange;
            if (hr.Failure || res is null) return Outcome.Failed;

            try
            {
                // Only the pointer moved: no new desktop image.
                if (info.LastPresentTime == 0) return Outcome.PointerOnly;

                using (var tex = res.QueryInterface<ID3D11Texture2D>())
                    _ctx.CopySubresourceRegion(_mipTex, 0, 0, 0, 0, tex, 0);
            }
            finally
            {
                res.Dispose();
                res = null;
                _dup.ReleaseFrame();
            }

            _ctx.GenerateMips(_srv);

            var lw = Math.Max(1, Width >> level);
            var lh = Math.Max(1, Height >> level);
            EnsureStaging(level, lw, lh);
            _ctx.CopySubresourceRegion(_staging!, 0, 0, 0, 0, _mipTex, (uint)level);

            var map = _ctx.Map(_staging!, 0, MapMode.Read);
            try
            {
                var rowBytes = lw * 4;
                for (var y = 0; y < lh; y++)
                    Buffer.MemoryCopy((byte*)map.DataPointer + y * map.RowPitch, (byte*)dst + y * dstStride, dstStride, rowBytes);
            }
            finally
            {
                _ctx.Unmap(_staging!, 0);
            }
            return Outcome.NewImage;
        }
        catch (SharpGenException)
        {
            // ACCESS_LOST (mode change, secure desktop, fullscreen switch): caller recreates.
            res?.Dispose();
            return Outcome.Failed;
        }
    }

    private void EnsureStaging(int level, int lw, int lh)
    {
        if (_staging is not null && _stagingLevel == level) return;
        _staging?.Dispose();
        _staging = _device!.CreateTexture2D(new Texture2DDescription
        {
            Width = (uint)lw,
            Height = (uint)lh,
            MipLevels = 1,
            ArraySize = 1,
            Format = Format.B8G8R8A8_UNorm,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Staging,
            CPUAccessFlags = CpuAccessFlags.Read,
        });
        _stagingLevel = level;
    }

    public void Dispose()
    {
        _staging?.Dispose();
        _srv?.Dispose();
        _mipTex?.Dispose();
        _dup?.Dispose();
        _ctx?.Dispose();
        _device?.Dispose();
    }
}
