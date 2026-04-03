using LlamaCtrl.Hubs;
using LlamaCtrl.Services;
using LlamaCtrl.Services.Dtos;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;

namespace LlamaCtrl.Controllers;

[Route("api/models")]
public class ModelsController : ApiControllerBase
{
    private readonly IModelService _svc;
    private readonly DownloadProgressStore _store;
    private readonly ILogger<ModelsController> _logger;

    public ModelsController(IModelService svc, DownloadProgressStore store, ILogger<ModelsController> logger)
    {
        _svc = svc;
        _store = store;
        _logger = logger;
    }

    [HttpGet("downloads/active")]
    public IActionResult GetActiveDownloads() => OkResponse(_store.GetActiveDownloads());

    [HttpGet("local")]
    public async Task<IActionResult> GetLocal()
    {
        var models = await _svc.GetLocalModelsAsync();
        return OkResponse(models);
    }

    [HttpGet("search")]
    public async Task<IActionResult> Search([FromQuery] string q, [FromQuery] int limit = 20, CancellationToken ct = default)
    {
        try
        {
            var results = await _svc.SearchHuggingFaceAsync(q, limit, ct);
            return OkResponse(results);
        }
        catch (Exception e)
        {
            return ErrorResponse(e.Message);
        }
    }

    [HttpGet("hf")]
    public async Task<IActionResult> GetHfModelInfo([FromQuery] string modelId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(modelId))
            return ErrorResponse("modelId query parameter is required");

        try
        {
            var info = await _svc.GetHuggingFaceModelInfoAsync(modelId, ct);
            return OkResponse(info);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to fetch HuggingFace model info for {ModelId}", modelId);
            return ErrorResponse(e.Message);
        }
    }

    [HttpGet("hf-cli-available")]
    public async Task<IActionResult> IsHfCliAvailable()
    {
        var available = await _svc.IsHfCliAvailableAsync();
        return OkResponse(new { available });
    }

    [HttpPost("download")]
    public IActionResult StartDownload([FromBody] StartDownloadRequestDto dto)
    {
        try
        {
            var downloadId = _svc.StartDownload(dto);
            return OkResponse(new { downloadId });
        }
        catch (Exception e)
        {
            return ErrorResponse(e.Message);
        }
    }

    public record PingDownloadDto(string DownloadId);

    [HttpPost("download/ping")]
    public async Task<IActionResult> PingDownload(
        [FromBody] PingDownloadDto dto,
        [FromServices] IHubContext<DownloadHub> hubContext)
    {
        var groupName = DownloadHub.ToGroupName(dto.DownloadId);
        await hubContext.Clients.Group(groupName).SendAsync("DownloadProgress", new
        {
            downloadId = dto.DownloadId,
            filename = "ping-test",
            phase = "downloading",
            bytesReceived = 12345L,
            totalBytes = (long?)1000000,
            percentComplete = 12.3,
            error = (string?)null,
            isComplete = false
        });
        return OkResponse(new { pinged = groupName });
    }

    [HttpDelete("download")]
    public IActionResult CancelDownload([FromQuery] string downloadId)
    {
        _svc.CancelDownload(downloadId);
        return OkResponse(new { cancelled = true });
    }

    [HttpGet("download/progress/{*downloadId}")]
    public IActionResult GetDownloadProgress(string downloadId)
    {
        return OkResponse(_svc.GetDownloadProgress(downloadId));
    }
}
