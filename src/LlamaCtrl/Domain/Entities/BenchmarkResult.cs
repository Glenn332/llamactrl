namespace LlamaCtrl.Domain.Entities;

public class BenchmarkResult
{
    public int Id { get; set; }
    public int ProfileId { get; set; }
    public int? InstanceId { get; set; }
    public DateTime RunAt { get; set; } = DateTime.UtcNow;
    public double GenerationSpeedTps { get; set; }
    public double PromptSpeedTps { get; set; }
    public double TimeToFirstTokenMs { get; set; }
    public double VramUsedMb { get; set; }
    public string? Notes { get; set; }
    public string? ChartDataJson { get; set; }
    public string? BenchmarkType { get; set; } = "token-generation";
    public string? RoundsJson { get; set; }

    public Profile Profile { get; set; } = null!;
}
