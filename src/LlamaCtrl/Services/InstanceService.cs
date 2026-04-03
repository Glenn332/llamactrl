using LlamaCtrl.Data;
using LlamaCtrl.Domain.Entities;
using LlamaCtrl.Domain.Enums;
using LlamaCtrl.Infrastructure;
using LlamaCtrl.Services.Dtos;
using Microsoft.EntityFrameworkCore;

namespace LlamaCtrl.Services;

public class InstanceService : IInstanceService
{
    private readonly AppDbContext _db;
    private readonly ProcessManager _processManager;
    private readonly MetricsCollector _metricsCollector;
    private readonly IConfiguration _config;
    private readonly ISettingsService _settings;

    public InstanceService(AppDbContext db, ProcessManager processManager,
        MetricsCollector metricsCollector, IConfiguration config, ISettingsService settings)
    {
        _db = db;
        _processManager = processManager;
        _metricsCollector = metricsCollector;
        _config = config;
        _settings = settings;
    }

    public async Task<List<InstanceDto>> GetAllAsync()
    {
        var instances = await _db.Instances.Include(i => i.Profile).ToListAsync();
        return instances.Select(MapToDto).ToList();
    }

    public async Task<InstanceDto> GetByIdAsync(int id)
    {
        var instance = await _db.Instances.Include(i => i.Profile)
            .FirstOrDefaultAsync(i => i.Id == id)
            ?? throw new KeyNotFoundException($"Instance {id} not found");
        return MapToDto(instance);
    }

    public async Task<InstanceDto> CreateAsync(CreateInstanceDto dto)
    {
        var profile = await _db.Profiles.FindAsync(dto.ProfileId)
            ?? throw new KeyNotFoundException($"Profile {dto.ProfileId} not found");

        var instance = new Instance
        {
            Name = dto.Name,
            ProfileId = dto.ProfileId,
            Port = dto.Port,
            Status = InstanceStatus.Stopped
        };
        _db.Instances.Add(instance);
        await _db.SaveChangesAsync();
        instance.Profile = profile;
        return MapToDto(instance);
    }

    public async Task<InstanceDto> UpdateAsync(int id, UpdateInstanceDto dto)
    {
        var instance = await _db.Instances.Include(i => i.Profile)
            .FirstOrDefaultAsync(i => i.Id == id)
            ?? throw new KeyNotFoundException($"Instance {id} not found");

        if (dto.Name != null) instance.Name = dto.Name;
        if (dto.Port.HasValue) instance.Port = dto.Port.Value;
        instance.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        return MapToDto(instance);
    }

    public async Task DeleteAsync(int id)
    {
        var instance = await _db.Instances.FindAsync(id)
            ?? throw new KeyNotFoundException($"Instance {id} not found");

        if (instance.Status == InstanceStatus.Running)
            await StopAsync(id);

        _db.Instances.Remove(instance);
        await _db.SaveChangesAsync();
    }

    public async Task StartAsync(int id)
    {
        var instance = await _db.Instances.Include(i => i.Profile)
            .FirstOrDefaultAsync(i => i.Id == id)
            ?? throw new KeyNotFoundException($"Instance {id} not found");

        var binary = await ResolveBinaryAsync(instance.Profile);
        var args = _processManager.BuildArgs(instance.Profile, instance.Port);
        var pid = await _processManager.StartProcessAsync(instance.Id, binary, args);

        instance.Pid = pid;
        instance.Status = InstanceStatus.Starting;
        instance.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
    }

    public async Task StopAsync(int id)
    {
        var instance = await _db.Instances.FindAsync(id)
            ?? throw new KeyNotFoundException($"Instance {id} not found");

        await _processManager.StopProcessAsync(instance.Id);

        instance.Pid = null;
        instance.Status = InstanceStatus.Stopped;
        instance.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
    }

    public async Task<MetricsDto> GetMetricsAsync(int id)
    {
        var instance = await _db.Instances.FindAsync(id)
            ?? throw new KeyNotFoundException($"Instance {id} not found");

        var snapshot = _metricsCollector.GetMetrics(instance.Id);
        return new MetricsDto(snapshot.TokensPerSec, snapshot.AvgLatencyMs, snapshot.TotalRequests, snapshot.VramUsedMb);
    }

    private async Task<string> ResolveBinaryAsync(Profile profile)
    {
        if (profile.SelectedBinaryId.HasValue)
        {
            var binary = await _db.LlamaServerBinaries.FindAsync(profile.SelectedBinaryId.Value);
            if (binary != null)
                return binary.Path;
        }

        return await _settings.GetLegacyBinaryPathAsync();
    }

    private static InstanceDto MapToDto(Instance i)
    {
        string? uptime = null;
        if (i.Status == InstanceStatus.Running)
        {
            var elapsed = DateTime.UtcNow - i.UpdatedAt;
            uptime = $"{(int)elapsed.TotalHours}h {elapsed.Minutes}m";
        }
        return new InstanceDto(i.Id, i.Name, i.ProfileId, i.Profile?.Name ?? "",
            i.Port, i.Pid, i.Status.ToString(), i.CreatedAt, i.UpdatedAt,
            null, uptime);
    }
}
