using System.Diagnostics;
using System.Net.NetworkInformation;
using ClassInsight.Shared.Contracts;

namespace ClassInsight.Agent.Services;

/// <summary>
/// Samples live machine metrics for the telemetry stream (FR-TEL-01..04): total CPU,
/// physical memory, instantaneous network bandwidth, the top processes by resource use,
/// and the foreground window title. Each metric is a delta against the previous sample,
/// so the collector is stateful and must be called on a single cadence.
/// </summary>
public sealed class SystemTelemetryCollector
{
    private readonly string _machineId;
    private readonly int _processorCount = Environment.ProcessorCount;

    // CPU deltas (whole machine).
    private ulong _prevIdle, _prevKernel, _prevUser;
    private bool _haveCpuBaseline;

    // Bandwidth deltas.
    private long _prevRx, _prevTx;
    private DateTimeOffset _prevNetAt;
    private bool _haveNetBaseline;

    // Per-process CPU deltas.
    private readonly Dictionary<int, (TimeSpan Cpu, DateTimeOffset At)> _procCpu = new();

    public SystemTelemetryCollector(string machineId) => _machineId = machineId;

    public TelemetrySnapshot Collect()
    {
        return new TelemetrySnapshot
        {
            MachineId = _machineId,
            SampledAt = DateTimeOffset.UtcNow,
            System = SampleCpuMemory(),
            Bandwidth = SampleBandwidth(),
            TopProcesses = SampleTopProcesses(),
            ForegroundWindowTitle = SafeForegroundTitle(),
        };
    }

    private CpuMemorySample SampleCpuMemory()
    {
        double cpuPercent = 0;
        if (NativeMethods.GetSystemTimes(out var idle, out var kernel, out var user))
        {
            var i = idle.ToUInt64();
            var k = kernel.ToUInt64();  // kernel time INCLUDES idle on Windows
            var u = user.ToUInt64();

            if (_haveCpuBaseline)
            {
                var idleDelta = i - _prevIdle;
                var totalDelta = (k - _prevKernel) + (u - _prevUser);
                if (totalDelta > 0)
                    cpuPercent = Math.Clamp((1.0 - (double)idleDelta / totalDelta) * 100.0, 0, 100);
            }

            _prevIdle = i; _prevKernel = k; _prevUser = u;
            _haveCpuBaseline = true;
        }

        long usedBytes = 0, totalBytes = 0;
        var mem = new NativeMethods.MEMORYSTATUSEX();
        if (NativeMethods.GlobalMemoryStatusEx(mem))
        {
            totalBytes = (long)mem.ullTotalPhys;
            usedBytes = (long)(mem.ullTotalPhys - mem.ullAvailPhys);
        }

        return new CpuMemorySample
        {
            CpuPercent = Math.Round(cpuPercent, 1),
            MemoryUsedBytes = usedBytes,
            MemoryTotalBytes = totalBytes,
        };
    }

    private BandwidthSample SampleBandwidth()
    {
        long rx = 0, tx = 0;
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up) continue;
            if (nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;
            try
            {
                var s = nic.GetIPStatistics();
                rx += s.BytesReceived;
                tx += s.BytesSent;
            }
            catch { /* some adapters refuse stats */ }
        }

        var now = DateTimeOffset.UtcNow;
        double down = 0, up = 0;
        if (_haveNetBaseline)
        {
            var secs = (now - _prevNetAt).TotalSeconds;
            if (secs > 0)
            {
                down = Math.Max(0, (rx - _prevRx) / secs);
                up = Math.Max(0, (tx - _prevTx) / secs);
            }
        }

        _prevRx = rx; _prevTx = tx; _prevNetAt = now; _haveNetBaseline = true;

        return new BandwidthSample
        {
            DownloadBytesPerSec = down,
            UploadBytesPerSec = up,
            TotalDownloadBytes = rx,
            TotalUploadBytes = tx,
        };
    }

    private IReadOnlyList<ProcessSample> SampleTopProcesses()
    {
        var now = DateTimeOffset.UtcNow;
        var samples = new List<(ProcessSample Sample, long Mem)>();
        var seen = new HashSet<int>();

        foreach (var p in Process.GetProcesses())
        {
            try
            {
                var pid = p.Id;
                seen.Add(pid);

                double cpuPercent = 0;
                TimeSpan cpu;
                try { cpu = p.TotalProcessorTime; }
                catch { cpu = TimeSpan.Zero; }

                if (_procCpu.TryGetValue(pid, out var prev))
                {
                    var wall = (now - prev.At).TotalSeconds;
                    if (wall > 0)
                    {
                        var used = (cpu - prev.Cpu).TotalSeconds;
                        cpuPercent = Math.Clamp(used / (wall * _processorCount) * 100.0, 0, 100);
                    }
                }
                _procCpu[pid] = (cpu, now);

                long mem = 0;
                try { mem = p.WorkingSet64; } catch { }

                samples.Add((new ProcessSample
                {
                    Pid = pid,
                    Name = SafeName(p),
                    CpuPercent = Math.Round(cpuPercent, 1),
                    MemoryBytes = mem,
                }, mem));
            }
            catch { /* process exited mid-enumeration */ }
            finally { p.Dispose(); }
        }

        // Drop CPU history for processes that have since exited.
        foreach (var dead in _procCpu.Keys.Where(k => !seen.Contains(k)).ToArray())
            _procCpu.Remove(dead);

        // Top 15 by CPU then memory, matching the contract's "top N" note.
        return samples
            .OrderByDescending(s => s.Sample.CpuPercent)
            .ThenByDescending(s => s.Mem)
            .Take(15)
            .Select(s => s.Sample)
            .ToArray();
    }

    private static string SafeName(Process p)
    {
        try { return p.ProcessName; }
        catch { return "?"; }
    }

    private static string? SafeForegroundTitle()
    {
        try { return NativeMethods.GetForegroundWindowTitle(); }
        catch { return null; }
    }
}
