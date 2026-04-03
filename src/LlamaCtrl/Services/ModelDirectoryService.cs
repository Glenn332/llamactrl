using LlamaCtrl.Data;
using LlamaCtrl.Domain.Entities;
using LlamaCtrl.Services.Dtos;
using Microsoft.EntityFrameworkCore;

namespace LlamaCtrl.Services;

public class ModelDirectoryService : IModelDirectoryService
{
    private readonly AppDbContext _db;

    public ModelDirectoryService(AppDbContext db) => _db = db;

    public async Task<List<ModelDirectoryDto>> GetAllAsync()
    {
        var dirs = await _db.ModelDirectories.OrderBy(d => d.Name).ToListAsync();
        return dirs.Select(MapToDto).ToList();
    }

    public async Task<ModelDirectoryDto?> GetByIdAsync(int id)
    {
        var dir = await _db.ModelDirectories.FindAsync(id);
        return dir != null ? MapToDto(dir) : null;
    }

    public async Task<ModelDirectoryDto> CreateAsync(CreateModelDirectoryDto dto)
    {
        var entity = new ModelDirectory
        {
            Name = dto.Name,
            Path = dto.Path
        };
        _db.ModelDirectories.Add(entity);
        await _db.SaveChangesAsync();
        return MapToDto(entity);
    }

    public async Task<ModelDirectoryDto?> UpdateAsync(int id, UpdateModelDirectoryDto dto)
    {
        var entity = await _db.ModelDirectories.FindAsync(id);
        if (entity == null) return null;

        if (dto.Name != null) entity.Name = dto.Name;
        if (dto.Path != null) entity.Path = dto.Path;
        entity.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        return MapToDto(entity);
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var entity = await _db.ModelDirectories.FindAsync(id);
        if (entity == null) return false;

        _db.ModelDirectories.Remove(entity);
        await _db.SaveChangesAsync();
        return true;
    }

    private static ModelDirectoryDto MapToDto(ModelDirectory d) =>
        new(d.Id, d.Name, d.Path, d.CreatedAt, d.UpdatedAt);
}
