namespace LlamaCtrl.Domain.Entities;

public class Profile
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string ModelPath { get; set; } = string.Empty;
    public string? ParametersJson { get; set; }
    public string? CustomArgsJson { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public int? SelectedBinaryId { get; set; }

    public ICollection<Instance> Instances { get; set; } = new List<Instance>();
    public ICollection<BenchmarkResult> BenchmarkResults { get; set; } = new List<BenchmarkResult>();
}
