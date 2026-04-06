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

        var genMatch = Regex.Match(line, @"generation eval time\s*=\s*[\d.]+ ms\s*/\s*(\d+)\s*.*?([\d.]+)\s*tokens per second");
        if (!genMatch.Success)
            genMatch = Regex.Match(line, @"(?<!prompt )eval time\s*=\s*[\d.]+ ms\s*/\s*(\d+)\s*.*?([\d.]+)\s*tokens per second");
        if (genMatch.Success && double.TryParse(genMatch.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture, out var tps))
        {
            snapshot.TokensPerSec = tps;
        }

        var ttftMatch = Regex.Match(line, @"prompt eval time\s*=\s*([\d.]+)\s*ms\s*/\s*(\d+)\s*tokens");
        if (ttftMatch.Success
            && double.TryParse(ttftMatch.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture, out var totalMs)
            && int.TryParse(ttftMatch.Groups[2].Value, out var tokenCount)
            && tokenCount > 0)
        {
            snapshot.AvgLatencyMs = totalMs / tokenCount;
        }

        if (line.Contains("POST /completion") || line.Contains("POST /v1/chat") || line.Contains("POST /v1/completions"))
            IncrementRequests(instanceId);

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
