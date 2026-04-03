namespace LlamaCtrl.Infrastructure.SystemInfo;

public class SystemMetricsCollector : BackgroundService
{
    private readonly SystemInfoService _sysInfo;
    private readonly ILogger<SystemMetricsCollector> _logger;

    public SystemMetricsCollector(SystemInfoService sysInfo, ILogger<SystemMetricsCollector> logger)
    {
        _sysInfo = sysInfo;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await _sysInfo.RefreshAsync();

        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);
            try { await _sysInfo.RefreshAsync(); }
            catch (Exception ex) { _logger.LogDebug(ex, "SystemMetricsCollector refresh failed"); }
        }
    }
}
