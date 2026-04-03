using System.Diagnostics;

namespace LlamaCtrl.Infrastructure;

public class ManagedProcess
{
    public int InstanceId { get; set; }
    public int Port { get; set; }
    public Process Process { get; set; } = null!;
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public List<string> RecentLines { get; } = new();
    private const int MaxRecentLines = 1000;

    public void AddLine(string line)
    {
        lock (RecentLines)
        {
            RecentLines.Add(line);
            if (RecentLines.Count > MaxRecentLines)
                RecentLines.RemoveAt(0);
        }
    }

    public List<string> GetRecentLines(int count = 200)
    {
        lock (RecentLines)
            return RecentLines.TakeLast(count).ToList();
    }
}
