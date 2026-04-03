using System.Collections.Concurrent;
using LlamaCtrl.Services.Dtos;

namespace LlamaCtrl.Services;

public class DownloadProgressStore
{
    internal class DownloadState
    {
        public string Filename { get; set; } = "";
        public string Phase { get; set; } = "starting";
        public long BytesReceived { get; set; }
        public long? TotalBytes { get; set; }
        public double PercentComplete { get; set; }
        public string? Error { get; set; }
        public bool IsComplete { get; set; }
    }

    private record Entry(DownloadState State, CancellationTokenSource Cts, object Lock);

    private readonly ConcurrentDictionary<string, Entry> _downloads = new();

    public CancellationToken Start(string downloadId, string filename)
    {
        var cts = new CancellationTokenSource();
        var state = new DownloadState { Filename = filename, Phase = "starting" };
        var entry = new Entry(state, cts, new object());
        _downloads[downloadId] = entry;
        return cts.Token;
    }

    internal void UpdateState(string downloadId, Action<DownloadState> mutate)
    {
        if (!_downloads.TryGetValue(downloadId, out var entry)) return;
        lock (entry.Lock) { mutate(entry.State); }
    }

    public void Cancel(string downloadId)
    {
        if (_downloads.TryGetValue(downloadId, out var entry))
            entry.Cts.Cancel();
    }

    public List<DownloadProgressDto> GetActiveDownloads()
    {
        var result = new List<DownloadProgressDto>();
        foreach (var (downloadId, entry) in _downloads)
        {
            lock (entry.Lock)
            {
                if (!entry.State.IsComplete)
                {
                    var s = entry.State;
                    result.Add(new DownloadProgressDto(downloadId, s.Filename, s.Phase, s.BytesReceived, s.TotalBytes, s.PercentComplete, s.Error, s.IsComplete));
                }
            }
        }
        return result;
    }

    public DownloadProgressDto? GetSnapshot(string downloadId)
    {
        if (!_downloads.TryGetValue(downloadId, out var entry)) return null;
        lock (entry.Lock)
        {
            var s = entry.State;
            return new DownloadProgressDto(
                downloadId, s.Filename, s.Phase,
                s.BytesReceived, s.TotalBytes, s.PercentComplete,
                s.Error, s.IsComplete
            );
        }
    }
}
