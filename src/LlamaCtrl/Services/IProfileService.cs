using LlamaCtrl.Services.Dtos;

namespace LlamaCtrl.Services;

public interface IProfileService
{
    Task<List<ProfileDto>> GetAllAsync();
    Task<ProfileDto> GetByIdAsync(int id);
    Task<ProfileDto> CreateAsync(CreateProfileDto dto);
    Task<ProfileDto> UpdateAsync(int id, UpdateProfileDto dto);
    Task DeleteAsync(int id);
    Task<ProfileDto> CloneAsync(int id, string? newName);
    Task<InstanceDto> LaunchAsync(int id);
}
