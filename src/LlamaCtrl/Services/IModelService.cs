using LlamaCtrl.Services.Dtos;

namespace LlamaCtrl.Services;

public interface IModelService
{
    Task<List<HfSearchResultDto>> SearchHuggingFaceAsync(string query, int limit = 20, CancellationToken ct = default);
    Task<HfModelInfoDto> GetHuggingFaceModelInfoAsync(string modelId, CancellationToken ct = default);
    Task<List<LocalModelDto>> GetLocalModelsAsync();
    Task<bool> IsHfCliAvailableAsync();
    string StartDownload(StartDownloadRequestDto dto);
    void CancelDownload(string downloadId);
    DownloadProgressDto? GetDownloadProgress(string downloadId);
}
