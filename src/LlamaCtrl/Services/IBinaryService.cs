using LlamaCtrl.Services.Dtos;

namespace LlamaCtrl.Services;

public interface IBinaryService
{
    Task<List<LlamaServerBinaryDto>> GetAllAsync();
    Task<LlamaServerBinaryDto?> GetByIdAsync(int id);
    Task<LlamaServerBinaryDto> CreateAsync(CreateBinaryDto dto);
    Task<LlamaServerBinaryDto?> UpdateAsync(int id, UpdateBinaryDto dto);
    Task<bool> DeleteAsync(int id);
}
