namespace LlamaCtrl.Services.Dtos;

public record SystemStatusDto(
    double CpuPercent,
    double RamUsedGb,
    double RamTotalGb,
    double VramUsedGb,
    double VramTotalGb,
    int ActiveInstances
);

public record GpuInfoDto(string Name, double VramTotalGb, double VramUsedGb);

public record AppSettingsDto(
    int Port, string DataDir, string ModelsDir,
    bool OpenBrowserOnStart, int HealthPollIntervalSeconds,
    bool RelaunchOnStartup
);

public record UpdateSettingsDto(
    string? ModelsDir,
    bool? OpenBrowserOnStart, int? HealthPollIntervalSeconds, bool? RelaunchOnStartup
);
