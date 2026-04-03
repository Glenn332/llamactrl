using LlamaCtrl.Data;
using LlamaCtrl.Domain.Enums;
using LlamaCtrl.Hubs;
using LlamaCtrl.Infrastructure;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace LlamaCtrl.Services;

public class HealthPoller : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ProcessManager _processManager;
    private readonly IHubContext<MetricsHub> _metricsHub;
    private readonly IConfiguration _config;
    private readonly ILogger<HealthPoller> _logger;
    private readonly Dictionary<int, int> _failureCounts = new();
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(5) };

    public HealthPoller(IServiceScopeFactory scopeFactory, ProcessManager processManager,
        IHubContext<MetricsHub> metricsHub, IConfiguration config, ILogger<HealthPoller> logger)
    {
        _scopeFactory = scopeFactory;
        _processManager = processManager;
        _metricsHub = metricsHub;
        _config = config;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(
            _config.GetValue<int>("LlamaCtrl:HealthPollIntervalSeconds", 15));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(interval, stoppingToken);
                await PollAllAsync();
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "HealthPoller encountered an error; will retry next interval");
            }
        }
    }

    private async Task PollAllAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var runningInstances = await db.Instances
            .Where(i => i.Status == InstanceStatus.Running || i.Status == InstanceStatus.Starting)
            .ToListAsync();

        foreach (var instance in runningInstances)
        {
            try
            {
                var resp = await _http.GetAsync($"http://localhost:{instance.Port}/health");
                if (resp.IsSuccessStatusCode)
                {
                    _failureCounts[instance.Id] = 0;
                    if (instance.Status == InstanceStatus.Starting)
                    {
                        instance.Status = InstanceStatus.Running;
                        instance.UpdatedAt = DateTime.UtcNow;
                        await db.SaveChangesAsync();
                        await _metricsHub.Clients.Group("metrics")
                            .SendAsync("InstanceStatusChanged", instance.Id, "Running");
                    }
                }
                else
                {
                    await HandleFailureAsync(db, instance);
                }
            }
            catch
            {
                await HandleFailureAsync(db, instance);
            }
        }
    }

    private async Task HandleFailureAsync(AppDbContext db, Domain.Entities.Instance instance)
    {
        _failureCounts.TryGetValue(instance.Id, out var count);
        _failureCounts[instance.Id] = ++count;

        if (count >= 3 && instance.Status != InstanceStatus.Error)
        {
            _logger.LogWarning("Instance {Id} health check failed {Count} times, marking as Error", instance.Id, count);
            instance.Status = InstanceStatus.Error;
            instance.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
            await _metricsHub.Clients.Group("metrics")
                .SendAsync("InstanceStatusChanged", instance.Id, "Error");
        }
    }
}
