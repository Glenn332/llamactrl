using LlamaCtrl.Data;
using LlamaCtrl.Domain.Entities;
using LlamaCtrl.Infrastructure;
using LlamaCtrl.Services.Dtos;
using Microsoft.EntityFrameworkCore;

namespace LlamaCtrl.Services;

public class ProfileService : IProfileService
{
    private readonly AppDbContext _db;
    private readonly IInstanceService _instanceService;
    private readonly ProcessManager _processManager;
    private readonly IConfiguration _config;

    public ProfileService(AppDbContext db, IInstanceService instanceService,
        ProcessManager processManager, IConfiguration config)
    {
        _db = db;
        _instanceService = instanceService;
        _processManager = processManager;
        _config = config;
    }

    public async Task<List<ProfileDto>> GetAllAsync()
    {
        var profiles = await _db.Profiles.ToListAsync();
        return profiles.Select(MapToDto).ToList();
    }

    public async Task<ProfileDto> GetByIdAsync(int id)
    {
        var profile = await _db.Profiles.FindAsync(id)
            ?? throw new KeyNotFoundException($"Profile {id} not found");
        return MapToDto(profile);
    }

    public async Task<ProfileDto> CreateAsync(CreateProfileDto dto)
    {
        var profile = new Profile
        {
            Name = dto.Name,
            ModelPath = dto.ModelPath,
            ParametersJson = dto.ParametersJson,
            CustomArgsJson = dto.CustomArgsJson,
            SelectedBinaryId = dto.SelectedBinaryId
        };
        _db.Profiles.Add(profile);
        await _db.SaveChangesAsync();
        return MapToDto(profile);
    }

    public async Task<ProfileDto> UpdateAsync(int id, UpdateProfileDto dto)
    {
        var profile = await _db.Profiles.FindAsync(id)
            ?? throw new KeyNotFoundException($"Profile {id} not found");

        if (dto.Name != null) profile.Name = dto.Name;
        if (dto.ModelPath != null) profile.ModelPath = dto.ModelPath;
        if (dto.ParametersJson != null) profile.ParametersJson = dto.ParametersJson;
        if (dto.CustomArgsJson != null) profile.CustomArgsJson = dto.CustomArgsJson;
        if (dto.SelectedBinaryId != null) profile.SelectedBinaryId = dto.SelectedBinaryId;
        profile.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        return MapToDto(profile);
    }

    public async Task DeleteAsync(int id)
    {
        var profile = await _db.Profiles.FindAsync(id)
            ?? throw new KeyNotFoundException($"Profile {id} not found");

        var hasInstances = await _db.Instances.AnyAsync(i => i.ProfileId == id);
        if (hasInstances)
            throw new InvalidOperationException($"Profile {id} has active instances and cannot be deleted");

        _db.Profiles.Remove(profile);
        await _db.SaveChangesAsync();
    }

    public async Task<ProfileDto> CloneAsync(int id, string? newName)
    {
        var source = await _db.Profiles.FindAsync(id)
            ?? throw new KeyNotFoundException($"Profile {id} not found");

        var clone = new Profile
        {
            Name = string.IsNullOrWhiteSpace(newName) ? $"{source.Name}-copy" : newName,
            ModelPath = source.ModelPath,
            ParametersJson = source.ParametersJson,
            CustomArgsJson = source.CustomArgsJson,
            SelectedBinaryId = source.SelectedBinaryId
        };
        _db.Profiles.Add(clone);
        await _db.SaveChangesAsync();
        return MapToDto(clone);
    }

    public async Task<InstanceDto> LaunchAsync(int id)
    {
        var profile = await _db.Profiles.FindAsync(id)
            ?? throw new KeyNotFoundException($"Profile {id} not found");

        var usedPorts = await _db.Instances.Select(i => i.Port).ToListAsync();
        var port = 8080;
        while (usedPorts.Contains(port))
            port++;

        var instance = await _instanceService.CreateAsync(
            new CreateInstanceDto(profile.Name, id, port));
        await _instanceService.StartAsync(instance.Id);
        return await _instanceService.GetByIdAsync(instance.Id);
    }


    private static ProfileDto MapToDto(Profile p) =>
        new(p.Id, p.Name, p.ModelPath, p.ParametersJson, p.CustomArgsJson,
            p.CreatedAt, p.UpdatedAt, p.SelectedBinaryId);
}
