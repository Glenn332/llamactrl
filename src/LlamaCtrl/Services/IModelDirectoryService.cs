using LlamaCtrl.Services.Dtos;

namespace LlamaCtrl.Services;

public interface IModelDirectoryService
{
    Task<List<ModelDirectoryDto>> GetAllAsync();
    Task<ModelDirectoryDto?> GetByIdAsync(int id);
    Task<ModelDirectoryDto> CreateAsync(CreateModelDirectoryDto dto);
    Task<ModelDirectoryDto?> UpdateAsync(int id, UpdateModelDirectoryDto dto);
    Task<bool> DeleteAsync(int id);
}
