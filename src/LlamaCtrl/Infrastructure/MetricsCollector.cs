using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace LlamaCtrl.Infrastructure;

public class MetricsSnapshot
{
    public double TokensPerSec { get; set; }
    public double AvgLatencyMs { get; set; }
    public long TotalRequests { get; set; }
    public double VramUsedMb { get; set; }
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
}

public class MetricsCollector
{
    private readonly ConcurrentDictionary<int, MetricsSnapshot> _snapshots = new();
    private readonly ConcurrentDictionary<int, long> _requestCounts = new();

    public void UpdateFromLogLine(int instanceId, string line)
    {
        var snapshot = _snapshots.GetOrAdd(instanceId, _ => new MetricsSnapshot());

        var evalMatch = Regex.Match(line, @"eval time\s*=\s*[\d.]+ ms\s*/\s*\d+\s*tokens.*?([\d.]+)\s*tokens per second");
        if (evalMatch.Success && double.TryParse(evalMatch.Groups[1].Value, out var tps))
        {
            snapshot.TokensPerSec = tps;
            snapshot.LastUpdated = DateTime.UtcNow;
        }

        var latMatch = Regex.Match(line, @"prompt eval time\s*=\s*([\d.]+)\s*ms");
        if (latMatch.Success && double.TryParse(latMatch.Groups[1].Value, out var lat))
        {
            snapshot.AvgLatencyMs = lat;
        }

        snapshot.LastUpdated = DateTime.UtcNow;
    }

    public void IncrementRequests(int instanceId)
        => _requestCounts.AddOrUpdate(instanceId, 1, (_, c) => c + 1);

    public MetricsSnapshot GetMetrics(int instanceId)
    {
        var snapshot = _snapshots.GetOrAdd(instanceId, _ => new MetricsSnapshot());
        snapshot.TotalRequests = _requestCounts.GetValueOrDefault(instanceId, 0);
        return snapshot;
    }

    public void RemoveInstance(int instanceId)
    {
        _snapshots.TryRemove(instanceId, out _);
        _requestCounts.TryRemove(instanceId, out _);
    }
}
