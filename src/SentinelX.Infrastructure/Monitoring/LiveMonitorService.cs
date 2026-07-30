using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace SentinelX.Infrastructure.Monitoring;

public sealed class AdapterInfo
{
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    public bool IsWifi { get; init; }
    public long LinkSpeedMbps { get; init; }
}

public sealed class ProcRow
{
    public string Name { get; init; } = "";
    public bool IsRunning { get; init; }
    public int Count { get; init; }
    public double MemMb { get; init; }
    public double CpuPct { get; init; }
    public string StatusText => IsRunning ? $"{Count} process(es), {MemMb:0} MB RAM, {CpuPct:0.0}% CPU" : "not running";
}

/// <summary>
/// Reusable process/network sampling used by the OBS and SplitCam engines to watch for
/// their target process (obs64/obs32, SplitCam) and the active network adapter's throughput.
/// </summary>
public sealed class LiveMonitorService
{
    private static readonly string[] VirtualKeywords =
        { "Virtual", "VMware", "Hyper-V", "Loopback", "TAP", "VPN", "Bluetooth" };

    private string? _adapterId;
    private long _prevSent, _prevRecv;
    private Stopwatch? _throughputSw;

    private readonly Dictionary<int, (TimeSpan Cpu, long Ticks)> _procCpu = new();

    public NetworkInterface? GetActiveAdapter()
    {
        try
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .Where(ni => ni.OperationalStatus == OperationalStatus.Up
                             && ni.NetworkInterfaceType != NetworkInterfaceType.Loopback
                             && !VirtualKeywords.Any(k => ni.Description.Contains(k, StringComparison.OrdinalIgnoreCase)))
                .FirstOrDefault(ni => ni.GetIPProperties().GatewayAddresses
                    .Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork));
        }
        catch
        {
            return null;
        }
    }

    public AdapterInfo? GetAdapterInfo()
    {
        var ni = GetActiveAdapter();
        if (ni is null) return null;
        return new AdapterInfo
        {
            Name = ni.Name,
            Description = ni.Description,
            IsWifi = ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211,
            LinkSpeedMbps = ni.Speed > 0 ? ni.Speed / 1_000_000 : 0
        };
    }

    /// <summary>Returns Mbps up/down since the previous call (first call returns 0/0).</summary>
    public (double UpMbps, double DownMbps)? SampleThroughput()
    {
        var ni = GetActiveAdapter();
        if (ni is null) return null;
        var st = ni.GetIPStatistics();
        if (_throughputSw is null || _adapterId != ni.Id)
        {
            _adapterId = ni.Id;
            _prevSent = st.BytesSent;
            _prevRecv = st.BytesReceived;
            _throughputSw = Stopwatch.StartNew();
            return (0, 0);
        }
        var secs = _throughputSw.Elapsed.TotalSeconds;
        if (secs <= 0.05) return null;
        var up = (st.BytesSent - _prevSent) * 8.0 / secs / 1_000_000.0;
        var down = (st.BytesReceived - _prevRecv) * 8.0 / secs / 1_000_000.0;
        _prevSent = st.BytesSent;
        _prevRecv = st.BytesReceived;
        _throughputSw.Restart();
        return (Math.Max(0, up), Math.Max(0, down));
    }

    public List<ProcRow> GetProcessSnapshot(IEnumerable<string> watchlist)
    {
        var rows = new List<ProcRow>();
        Process[] all;
        try { all = Process.GetProcesses(); }
        catch { return rows; }

        var nowTicks = Stopwatch.GetTimestamp();
        var seenPids = new HashSet<int>();

        foreach (var watch in watchlist)
        {
            var matches = all.Where(p =>
                p.ProcessName.StartsWith(watch, StringComparison.OrdinalIgnoreCase)).ToList();
            if (matches.Count == 0)
            {
                rows.Add(new ProcRow { Name = watch, IsRunning = false });
                continue;
            }

            double memMb = 0, cpuPct = 0;
            foreach (var p in matches)
            {
                try
                {
                    seenPids.Add(p.Id);
                    memMb += p.WorkingSet64 / 1024.0 / 1024.0;
                    var cpu = p.TotalProcessorTime;
                    if (_procCpu.TryGetValue(p.Id, out var prev))
                    {
                        var wallSec = (nowTicks - prev.Ticks) / (double)Stopwatch.Frequency;
                        if (wallSec > 0.2)
                            cpuPct += (cpu - prev.Cpu).TotalSeconds / wallSec / Environment.ProcessorCount * 100.0;
                    }
                    _procCpu[p.Id] = (cpu, nowTicks);
                }
                catch
                {
                    // process exited or access denied — skip
                }
            }
            rows.Add(new ProcRow
            {
                Name = watch,
                IsRunning = true,
                Count = matches.Count,
                MemMb = memMb,
                CpuPct = Math.Max(0, cpuPct)
            });
        }

        foreach (var stale in _procCpu.Keys.Where(pid => !seenPids.Contains(pid)).ToList())
            _procCpu.Remove(stale);
        foreach (var p in all)
        {
            try { p.Dispose(); } catch { }
        }
        return rows;
    }
}
