using LlamaCtrl.Services.Dtos;

namespace LlamaCtrl.Services;

public interface ISettingsService
{
    Task<AppSettingsDto> GetAsync();
    Task<AppSettingsDto> UpdateAsync(UpdateSettingsDto dto);
    Task<string> GetLegacyBinaryPathAsync();
    Task<SystemStatusDto> GetSystemStatusAsync();
    Task<List<string>> GetModelsAsync();
    Task<List<GpuInfoDto>> GetGpusAsync();
    Task ResetToDefaultsAsync();
    Task OpenDataDirAsync();
}
