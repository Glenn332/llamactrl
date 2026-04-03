namespace LlamaCtrl.Services;

public class BenchmarkProgressStore
{
    private BenchmarkProgress? _current;
    private CancellationTokenSource? _cts;
    private readonly object _lock = new();

    public CancellationToken Start(int nPredict)
    {
        lock (_lock)
        {
            _cts?.Cancel();
            _cts = new CancellationTokenSource();
            _current = new BenchmarkProgress { NPredict = nPredict };
            return _cts.Token;
        }
    }

    public void Update(Action<BenchmarkProgress> mutate)
    {
        lock (_lock) { if (_current != null) mutate(_current); }
    }

    public void Cancel()
    {
        lock (_lock) _cts?.Cancel();
    }

    public BenchmarkProgressSnapshot? GetSnapshot()
    {
        lock (_lock)
        {
            if (_current == null) return null;
            return new BenchmarkProgressSnapshot
            {
                IsRunning  = _current.IsRunning,
                Phase      = _current.Phase,
                TokenCount = _current.TokenCount,
                NPredict   = _current.NPredict,
                GenTps     = _current.GenTps,
                PromptTps  = _current.PromptTps,
                PromptMs   = _current.PromptMs,
                Error      = _current.Error,
                ResultId   = _current.ResultId,
                Points     = [.. _current.Points],
            };
        }
    }
}

public class BenchmarkProgress
{
    public bool    IsRunning  { get; set; } = true;
    public string  Phase      { get; set; } = "starting";
    public int     TokenCount { get; set; }
    public int     NPredict   { get; set; }
    public double  GenTps     { get; set; }
    public double  PromptTps  { get; set; }
    public double  PromptMs   { get; set; }
    public string? Error      { get; set; }
    public int?    ResultId   { get; set; }
    public List<BenchmarkPoint> Points { get; } = [];
}

public record BenchmarkProgressSnapshot
{
    public bool    IsRunning  { get; init; }
    public string  Phase      { get; init; } = "idle";
    public int     TokenCount { get; init; }
    public int     NPredict   { get; init; }
    public double  GenTps     { get; init; }
    public double  PromptTps  { get; init; }
    public double  PromptMs   { get; init; }
    public string? Error      { get; init; }
    public int?    ResultId   { get; init; }
    public List<BenchmarkPoint> Points { get; init; } = [];
}

public record BenchmarkPoint(double TimeS, double Tps, int Tokens);
