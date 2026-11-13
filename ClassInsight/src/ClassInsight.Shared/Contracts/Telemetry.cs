namespace ClassInsight.Shared.Contracts;

/// <summary>
/// A real-time telemetry sample pushed by the student agent on a fixed cadence
/// (independent of the video stream). This is what powers the processes and
/// bandwidth views in the teacher navbar.
/// </summary>
public sealed record TelemetrySnapshot
{
    public string MachineId { get; init; } = string.Empty;

    public DateTimeOffset SampledAt { get; init; } = DateTimeOffset.UtcNow;

    public CpuMemorySample System { get; init; } = new();

    public BandwidthSample Bandwidth { get; init; } = new();

    /// <summary>Top processes by resource use. The agent caps this list (e.g. top 15).</summary>
    public IReadOnlyList<ProcessSample> TopProcesses { get; init; } = Array.Empty<ProcessSample>();

    /// <summary>Title of the window currently in the foreground, if resolvable.</summary>
    public string? ForegroundWindowTitle { get; init; }

    /// <summary>Active browser tab URL/host if the agent surfaces it (optional).</summary>
    public string? ActiveUrl { get; init; }
}

public sealed record CpuMemorySample
{
    /// <summary>Total CPU utilisation across all cores, 0–100.</summary>
    public double CpuPercent { get; init; }

    public long MemoryUsedBytes { get; init; }
    public long MemoryTotalBytes { get; init; }

    public double MemoryPercent =>
        MemoryTotalBytes > 0 ? MemoryUsedBytes * 100.0 / MemoryTotalBytes : 0;
}

/// <summary>
/// Instantaneous network throughput, measured as the delta since the previous
/// sample divided by the elapsed interval — i.e. live bandwidth, not a counter.
/// </summary>
public sealed record BandwidthSample
{
    public double DownloadBytesPerSec { get; init; }
    public double UploadBytesPerSec { get; init; }

    /// <summary>Cumulative bytes since the agent started, for session totals.</summary>
    public long TotalDownloadBytes { get; init; }
    public long TotalUploadBytes { get; init; }
}

public sealed record ProcessSample
{
    public int Pid { get; init; }
    public string Name { get; init; } = string.Empty;
    public double CpuPercent { get; init; }
    public long MemoryBytes { get; init; }
}
