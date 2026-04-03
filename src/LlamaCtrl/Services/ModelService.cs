using System.Diagnostics;
using System.Text.Json;
using LlamaCtrl.Hubs;
using LlamaCtrl.Services.Dtos;
using Microsoft.AspNetCore.SignalR;

namespace LlamaCtrl.Services;

public class ModelService : IModelService
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly IHubContext<DownloadHub> _downloadHub;
    private readonly DownloadProgressStore _store;
    private readonly ISettingsService _settings;
    private readonly IModelDirectoryService _modelDirService;
    private readonly ILogger<ModelService> _logger;
    private readonly IServiceScopeFactory _scopeFactory;

    private string? _hfCliBinary;

    public ModelService(
        IHttpClientFactory httpFactory,
        IHubContext<DownloadHub> downloadHub,
        DownloadProgressStore store,
        ISettingsService settings,
        IModelDirectoryService modelDirService,
        ILogger<ModelService> logger,
        IServiceScopeFactory scopeFactory)
    {
        _httpFactory = httpFactory;
        _downloadHub = downloadHub;
        _store = store;
        _settings = settings;
        _modelDirService = modelDirService;
        _logger = logger;
        _scopeFactory = scopeFactory;
    }

    public async Task<bool> IsHfCliAvailableAsync()
    {
        foreach (var binary in new[] { "huggingface-cli", "hf" })
        {
            try
            {
                var psi = new ProcessStartInfo(binary, "--version")
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                };
                using var proc = Process.Start(psi);
                if (proc != null)
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                    try
                    {
                        await proc.WaitForExitAsync(cts.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        _logger.LogWarning("Timeout waiting for {Binary} --version; assuming not available", binary);
                        try { proc.Kill(); } catch {  }
                        continue;
                    }
                    if (proc.ExitCode == 0)
                    {
                        _hfCliBinary = binary;
                        return true;
                    }
                }
            }
            catch {  }
        }
        return false;
    }

    public async Task<List<HfSearchResultDto>> SearchHuggingFaceAsync(string query, int limit = 20, CancellationToken ct = default)
    {
        var http = _httpFactory.CreateClient("HuggingFace");
        var url = $"/api/models?search={Uri.EscapeDataString(query)}&filter=gguf&sort=downloads&direction=-1&limit={limit}&full=false";
        var response = await http.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();

        using var stream = await response.Content.ReadAsStreamAsync(ct);
        var json = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

        var results = new List<HfSearchResultDto>();
        foreach (var item in json.RootElement.EnumerateArray())
        {
            var modelId = item.GetProperty("modelId").GetString() ?? "";
            var parts = modelId.Split('/', 2);
            var author = parts.Length > 1 ? parts[0] : "";
            var modelName = parts.Length > 1 ? parts[1] : modelId;
            var downloads = item.TryGetProperty("downloads", out var dl) ? dl.GetInt64() : 0;
            var likes = item.TryGetProperty("likes", out var lk) ? lk.GetInt64() : 0;
            var tags = item.TryGetProperty("tags", out var tg)
                ? tg.EnumerateArray().Select(t => t.GetString() ?? "").ToList()
                : new List<string>();

            string? description = null;
            if (item.TryGetProperty("cardData", out var card) && card.TryGetProperty("description", out var desc))
                description = desc.GetString();

            DateTime? lastModified = null;
            if (item.TryGetProperty("lastModified", out var lm) && lm.ValueKind != JsonValueKind.Null)
            {
                if (DateTime.TryParse(lm.GetString(), out var parsedDate))
                    lastModified = parsedDate;
            }

            results.Add(new HfSearchResultDto(modelId, author, modelName, downloads, likes, tags, description, lastModified));
        }
        return results;
    }

    public async Task<HfModelInfoDto> GetHuggingFaceModelInfoAsync(string modelId, CancellationToken ct = default)
    {
        var http = _httpFactory.CreateClient("HuggingFace");
        var response = await http.GetAsync($"/api/models/{modelId}?blobs=true", ct);
        response.EnsureSuccessStatusCode();

        using var stream = await response.Content.ReadAsStreamAsync(ct);
        var json = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        var root = json.RootElement;

        var parts = modelId.Split('/', 2);
        var author = parts.Length > 1 ? parts[0] : "";
        var modelName = parts.Length > 1 ? parts[1] : modelId;
        var downloads = root.TryGetProperty("downloads", out var dl) ? dl.GetInt64() : 0;
        var likes = root.TryGetProperty("likes", out var lk) ? lk.GetInt64() : 0;
        var tags = root.TryGetProperty("tags", out var tg)
            ? tg.EnumerateArray().Select(t => t.GetString() ?? "").ToList()
            : new List<string>();

        string? description = null;
        if (root.TryGetProperty("cardData", out var card) && card.TryGetProperty("description", out var desc))
            description = desc.GetString();

        DateTime? lastModified = null;
        if (root.TryGetProperty("lastModified", out var lm) && lm.ValueKind != JsonValueKind.Null)
        {
            if (DateTime.TryParse(lm.GetString(), out var parsedDate))
                lastModified = parsedDate;
        }

        var files = new List<HfFileInfoDto>();
        if (root.TryGetProperty("siblings", out var siblings))
        {
            foreach (var sibling in siblings.EnumerateArray())
            {
                var fname = sibling.TryGetProperty("rfilename", out var fn) ? fn.GetString() ?? "" : "";
                if (!fname.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase)) continue;
                long? size = null;
                if (sibling.TryGetProperty("lfs", out var lfs) && lfs.TryGetProperty("size", out var lfsSize) && lfsSize.ValueKind == JsonValueKind.Number)
                    size = lfsSize.GetInt64();
                else if (sibling.TryGetProperty("size", out var sz) && sz.ValueKind == JsonValueKind.Number)
                    size = sz.GetInt64();
                var downloadUrl = $"https://huggingface.co/{modelId}/resolve/main/{fname}";
                files.Add(new HfFileInfoDto(fname, size, downloadUrl));
            }
        }

        return new HfModelInfoDto(modelId, author, modelName, downloads, likes, tags, description, lastModified, files);
    }

    public async Task<List<LocalModelDto>> GetLocalModelsAsync()
    {
        var dirNameMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var modelDirs = await _modelDirService.GetAllAsync();
        foreach (var dir in modelDirs)
        {
            if (!string.IsNullOrEmpty(dir.Path) && Directory.Exists(dir.Path))
                dirNameMap[dir.Path] = dir.Name;
        }

        if (dirNameMap.Count == 0)
        {
            var settings = await _settings.GetAsync();
            if (!string.IsNullOrEmpty(settings.ModelsDir) && Directory.Exists(settings.ModelsDir))
                dirNameMap[settings.ModelsDir] = Path.GetFileName(settings.ModelsDir) ?? settings.ModelsDir;
        }

        if (dirNameMap.Count == 0)
            return new List<LocalModelDto>();

        var allFiles = dirNameMap.Keys
            .SelectMany(dir => Directory.GetFiles(dir, "*.gguf", SearchOption.AllDirectories)
                .Select(f => (File: new FileInfo(f), DirName: dirNameMap[dir])))
            .GroupBy(x => x.File.FullName)
            .Select(g => g.First())
            .OrderBy(x => x.File.Name)
            .ToList();

        var results = new List<LocalModelDto>(allFiles.Count);
        foreach (var (fi, dirName) in allFiles)
        {
            HfMetaDto? hfMeta = null;
            var sidecarPath = fi.FullName + ".meta.json";
            if (File.Exists(sidecarPath))
            {
                try
                {
                    var json = await File.ReadAllTextAsync(sidecarPath);
                    hfMeta = JsonSerializer.Deserialize<HfMetaDto>(json,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                }
                catch (Exception ex) { _logger.LogWarning(ex, "Failed to read sidecar for {File}", fi.FullName); }
            }
            results.Add(new LocalModelDto(fi.Name, fi.FullName, fi.Length, fi.LastWriteTimeUtc, hfMeta, dirName));
        }
        return results;
    }

    public string StartDownload(StartDownloadRequestDto dto)
    {
        var downloadId = $"{dto.ModelId}/{dto.Filename}";
        var ct = _store.Start(downloadId, dto.Filename);

        _logger.LogInformation("Download started: {DownloadId}", downloadId);

        _ = Task.Run(async () =>
        {
            using var scope = _scopeFactory.CreateScope();
            var hubContext = scope.ServiceProvider.GetRequiredService<IHubContext<DownloadHub>>();
            var settings = scope.ServiceProvider.GetRequiredService<ISettingsService>();
            _logger.LogInformation("Background download task started for {DownloadId}", downloadId);
            try
            {
                var hfToken = await GetHfTokenAsync();
                _logger.LogInformation("Using HTTP download for {DownloadId}, token present: {HasToken}", downloadId, !string.IsNullOrEmpty(hfToken));
                await RunHttpDownloadAsync(dto, downloadId, ct, hubContext, settings, hfToken);

                _store.UpdateState(downloadId, s => { s.Phase = "done"; s.IsComplete = true; s.PercentComplete = 100; });
            }
            catch (OperationCanceledException)
            {
                _store.UpdateState(downloadId, s => { s.Phase = "cancelled"; s.IsComplete = true; });
                try
                {
                    var dirs = await scope.ServiceProvider.GetRequiredService<IModelDirectoryService>().GetAllAsync();
                    var dir = !string.IsNullOrEmpty(dto.TargetDirectory) ? dto.TargetDirectory
                        : dirs.FirstOrDefault()?.Path ?? (await scope.ServiceProvider.GetRequiredService<ISettingsService>().GetAsync()).ModelsDir;
                    var partial = Path.Combine(dir, dto.Filename);
                    if (File.Exists(partial)) File.Delete(partial);
                }
                catch (Exception ex) { _logger.LogWarning(ex, "Failed to delete partial file for {DownloadId}", downloadId); }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Download failed for {DownloadId}", downloadId);
                _store.UpdateState(downloadId, s => { s.Phase = "error"; s.Error = ex.Message; s.IsComplete = true; });
            }
            finally
            {
                var snap = _store.GetSnapshot(downloadId);
                if (snap != null)
                    await hubContext.Clients.Group(DownloadHub.ToGroupName(downloadId)).SendAsync("DownloadProgress", snap);
            }
        });

        return downloadId;
    }

    private async Task RunHttpDownloadAsync(StartDownloadRequestDto dto, string downloadId, CancellationToken ct, IHubContext<DownloadHub> hubContext, ISettingsService settings, string? hfToken = null)
    {
        string modelsDir;
        if (!string.IsNullOrEmpty(dto.TargetDirectory))
        {
            modelsDir = dto.TargetDirectory;
        }
        else
        {
            var modelDirService = _scopeFactory.CreateScope().ServiceProvider.GetRequiredService<IModelDirectoryService>();
            var dirs = await modelDirService.GetAllAsync();
            var firstDir = dirs.FirstOrDefault();
            if (firstDir != null && !string.IsNullOrEmpty(firstDir.Path))
                modelsDir = firstDir.Path;
            else
                modelsDir = (await settings.GetAsync()).ModelsDir;
        }
        var destPath = Path.Combine(modelsDir, dto.Filename);
        var downloadUrl = $"https://huggingface.co/{dto.ModelId}/resolve/main/{dto.Filename}";

        _logger.LogInformation("HTTP download starting: {Url}, token present: {HasToken}", downloadUrl, !string.IsNullOrEmpty(hfToken));

        var http = _httpFactory.CreateClient("HuggingFace");
        var request = new HttpRequestMessage(HttpMethod.Get, downloadUrl);
        if (!string.IsNullOrEmpty(hfToken))
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", hfToken);
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength ?? dto.KnownSizeBytes;
        _logger.LogInformation("HTTP download beginning for {DownloadId}, totalBytes={TotalBytes}", downloadId, totalBytes);
        _store.UpdateState(downloadId, s =>
        {
            s.Phase = "downloading";
            s.TotalBytes = totalBytes;
            s.BytesReceived = 0;
            s.PercentComplete = 0;
        });
        var initialSnap = _store.GetSnapshot(downloadId);
        if (initialSnap != null)
        {
            try { await hubContext.Clients.Group(DownloadHub.ToGroupName(downloadId)).SendAsync("DownloadProgress", initialSnap); }
            catch (Exception ex) { _logger.LogWarning(ex, "SignalR send failed (initial)"); }
        }

        await using var fileStream = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None);
        using var contentStream = await response.Content.ReadAsStreamAsync(ct);

        var buffer = new byte[81920];
        long received = 0;
        var lastNotify = DateTime.UtcNow;
        var lastLogTime = DateTime.UtcNow;
        bool firstByteLogged = false;
        int read;

        try
        {
            while ((read = await contentStream.ReadAsync(buffer, ct)) > 0)
            {
                if (!firstByteLogged)
                {
                    _logger.LogInformation("First bytes received for {DownloadId}", downloadId);
                    firstByteLogged = true;
                }

                await fileStream.WriteAsync(buffer.AsMemory(0, read), ct);
                received += read;

                if ((DateTime.UtcNow - lastNotify).TotalMilliseconds >= 150)
                {
                    var pct = totalBytes.HasValue ? (double)received / totalBytes.Value * 100 : 0;
                    _store.UpdateState(downloadId, s => { s.BytesReceived = received; s.PercentComplete = pct; s.TotalBytes = totalBytes; });
                    var snap = _store.GetSnapshot(downloadId);
                    if (snap != null)
                    {
                        if ((DateTime.UtcNow - lastLogTime).TotalSeconds >= 5)
                        {
                            _logger.LogDebug("Progress: {DownloadId} {Pct:F1}% ({Received}/{Total} bytes)", downloadId, pct, received, totalBytes);
                            lastLogTime = DateTime.UtcNow;
                        }
                        try { await hubContext.Clients.Group(DownloadHub.ToGroupName(downloadId)).SendAsync("DownloadProgress", snap); }
                        catch (Exception ex) { _logger.LogWarning(ex, "SignalR send failed for {DownloadId}", downloadId); }
                    }
                    lastNotify = DateTime.UtcNow;
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw; // fileStream disposed by await using; StartDownload catch deletes partial file
        }

        _logger.LogInformation("HTTP download completed for {DownloadId}, total bytes: {Received}", downloadId, received);

        try
        {
            var sidecarPath = destPath + ".meta.json";
            var meta = new HfMetaDto(
                dto.ModelId, dto.Filename, dto.Author, dto.Description,
                dto.Tags, DateTime.UtcNow, received,
                dto.HfDownloads, dto.HfLikes);
            await File.WriteAllTextAsync(sidecarPath,
                JsonSerializer.Serialize(meta, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to write sidecar metadata for {DownloadId}", downloadId);
        }
    }

    private async Task<string?> GetHfTokenAsync()
    {
        try
        {
            var tokenPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".cache", "huggingface", "token");
            if (File.Exists(tokenPath))
            {
                var token = (await File.ReadAllTextAsync(tokenPath)).Trim();
                if (!string.IsNullOrEmpty(token))
                {
                    _logger.LogDebug("HuggingFace token loaded from {Path}", tokenPath);
                    return token;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read HuggingFace token file");
        }
        return null;
    }

    public void CancelDownload(string downloadId) => _store.Cancel(downloadId);

    public DownloadProgressDto? GetDownloadProgress(string downloadId) => _store.GetSnapshot(downloadId);
}
