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
        var normalInterval = TimeSpan.FromSeconds(
            _config.GetValue<int>("LlamaCtrl:HealthPollIntervalSeconds", 15));
        var startingInterval = TimeSpan.FromSeconds(2);

        await Task.Delay(startingInterval, stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var hasStarting = await PollAllAsync();
                var delay = hasStarting ? startingInterval : normalInterval;
                await Task.Delay(delay, stoppingToken);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "HealthPoller encountered an error; will retry next interval");
                await Task.Delay(normalInterval, stoppingToken);
            }
        }
    }

    private async Task<bool> PollAllAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var instances = await db.Instances
            .Where(i => i.Status == InstanceStatus.Running || i.Status == InstanceStatus.Starting)
            .ToListAsync();

        foreach (var instance in instances)
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

        return instances.Any(i => i.Status == InstanceStatus.Starting);
    }

    private async Task HandleFailureAsync(AppDbContext db, Domain.Entities.Instance instance)
    {
        if (instance.Status == InstanceStatus.Starting)
        {
            _failureCounts[instance.Id] = 0;
            return;
        }

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
