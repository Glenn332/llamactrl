using LlamaCtrl.Services.Dtos;

namespace LlamaCtrl.Services;

public interface IInstanceService
{
    Task<List<InstanceDto>> GetAllAsync();
    Task<InstanceDto> GetByIdAsync(int id);
    Task<InstanceDto> CreateAsync(CreateInstanceDto dto);
    Task<InstanceDto> UpdateAsync(int id, UpdateInstanceDto dto);
    Task DeleteAsync(int id);
    Task StartAsync(int id);
    Task StopAsync(int id);
    Task<MetricsDto> GetMetricsAsync(int id);
}
