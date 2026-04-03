using LlamaCtrl.Data;
using LlamaCtrl.Domain.Entities;
using LlamaCtrl.Services.Dtos;
using Microsoft.EntityFrameworkCore;

namespace LlamaCtrl.Services;

public class BinaryService : IBinaryService
{
    private readonly AppDbContext _db;

    public BinaryService(AppDbContext db) => _db = db;

    public async Task<List<LlamaServerBinaryDto>> GetAllAsync()
    {
        var binaries = await _db.LlamaServerBinaries.OrderBy(b => b.Name).ToListAsync();
        return binaries.Select(MapToDto).ToList();
    }

    public async Task<LlamaServerBinaryDto?> GetByIdAsync(int id)
    {
        var binary = await _db.LlamaServerBinaries.FindAsync(id);
        return binary != null ? MapToDto(binary) : null;
    }

    public async Task<LlamaServerBinaryDto> CreateAsync(CreateBinaryDto dto)
    {
        if (dto.IsDefault)
            await ClearDefaultAsync();

        var entity = new LlamaServerBinary
        {
            Name = dto.Name,
            Path = dto.Path,
            IsDefault = dto.IsDefault
        };
        _db.LlamaServerBinaries.Add(entity);
        await _db.SaveChangesAsync();
        return MapToDto(entity);
    }

    public async Task<LlamaServerBinaryDto?> UpdateAsync(int id, UpdateBinaryDto dto)
    {
        var entity = await _db.LlamaServerBinaries.FindAsync(id);
        if (entity == null) return null;

        if (dto.IsDefault == true)
            await ClearDefaultAsync();

        if (dto.Name != null) entity.Name = dto.Name;
        if (dto.Path != null) entity.Path = dto.Path;
        if (dto.IsDefault.HasValue) entity.IsDefault = dto.IsDefault.Value;
        entity.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        return MapToDto(entity);
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var entity = await _db.LlamaServerBinaries.FindAsync(id);
        if (entity == null) return false;

        _db.LlamaServerBinaries.Remove(entity);
        await _db.SaveChangesAsync();
        return true;
    }

    private async Task ClearDefaultAsync()
    {
        var defaults = await _db.LlamaServerBinaries.Where(b => b.IsDefault).ToListAsync();
        foreach (var b in defaults)
            b.IsDefault = false;
    }

    private static LlamaServerBinaryDto MapToDto(LlamaServerBinary b) =>
        new(b.Id, b.Name, b.Path, b.IsDefault, b.CreatedAt, b.UpdatedAt);
}
