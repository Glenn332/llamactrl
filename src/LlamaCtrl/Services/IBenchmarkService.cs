using LlamaCtrl.Services.Dtos;

namespace LlamaCtrl.Services;

public interface IBenchmarkService
{
    Task<List<BenchmarkResultDto>> GetAllAsync();
    Task<BenchmarkResultDto> GetByIdAsync(int id);
    Task<BenchmarkResultDto> RunAsync(RunBenchmarkDto dto);
    Task<List<BenchmarkResultDto>> GetCompareAsync(int[] ids);
    Task<byte[]> ExportCsvAsync(int[] ids);
    IAsyncEnumerable<BenchmarkStreamEvent> RunStreamAsync(RunBenchmarkDto dto, CancellationToken ct);
    void StartBackgroundRun(RunBenchmarkDto dto);
    Task DeleteAsync(int[] ids);
}
