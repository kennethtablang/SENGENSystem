using System.Runtime.InteropServices;
using System.Text;

namespace ClassInsight.Agent.Services;

/// <summary>
/// Win32 interop the agent needs to measure the machine and capture the screen without
/// extra NuGet dependencies: total CPU times, physical memory, primary-screen metrics and
/// the foreground window title. Uses classic runtime marshalling (<see cref="DllImportAttribute"/>)
/// because the memory-status class and text buffers aren't supported by source-generated P/Invoke.
/// </summary>
internal static class NativeMethods
{
    // ---- CPU ----
    [StructLayout(LayoutKind.Sequential)]
    public struct FILETIME
    {
        public uint DwLowDateTime;
        public uint DwHighDateTime;
        public readonly ulong ToUInt64() => ((ulong)DwHighDateTime << 32) | DwLowDateTime;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetSystemTimes(out FILETIME idleTime, out FILETIME kernelTime, out FILETIME userTime);

    // ---- memory ----
    [StructLayout(LayoutKind.Sequential)]
    public class MEMORYSTATUSEX
    {
        public uint dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX lpBuffer);

    // ---- screen ----
    public const int SM_CXSCREEN = 0;
    public const int SM_CYSCREEN = 1;

    [DllImport("user32.dll")]
    public static extern int GetSystemMetrics(int nIndex);

    // ---- foreground window ----
    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetWindowTextLength(IntPtr hWnd);

    public static string? GetForegroundWindowTitle()
    {
        var h = GetForegroundWindow();
        if (h == IntPtr.Zero) return null;
        var len = GetWindowTextLength(h);
        if (len <= 0) return null;
        var sb = new StringBuilder(len + 1);
        var copied = GetWindowText(h, sb, sb.Capacity);
        return copied > 0 ? sb.ToString() : null;
    }
}
