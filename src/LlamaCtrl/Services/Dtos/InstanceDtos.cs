namespace LlamaCtrl.Services.Dtos;

public record InstanceDto(
    int Id, string Name, int ProfileId, string ProfileName,
    int Port, int? Pid, string Status,
    DateTime CreatedAt, DateTime UpdatedAt,
    MetricsDto? Metrics = null,
    string? Uptime = null
);

public record CreateInstanceDto(string Name, int ProfileId, int Port);
public record UpdateInstanceDto(string? Name, int? Port);

public record MetricsDto(
    double TokensPerSec,
    double AvgLatencyMs,
    long TotalRequests,
    double VramUsedMb
);
