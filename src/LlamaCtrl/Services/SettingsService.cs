using LlamaCtrl.Data;
using LlamaCtrl.Domain.Entities;
using LlamaCtrl.Services.Dtos;
using Microsoft.EntityFrameworkCore;

namespace LlamaCtrl.Services;

public class SettingsService : ISettingsService
{
    private readonly AppDbContext _db;
    private readonly IConfiguration _config;

    public SettingsService(AppDbContext db, IConfiguration config)
    {
        _db = db;
        _config = config;
    }

    public async Task<AppSettingsDto> GetAsync()
    {
        var dbSettings = await _db.AppSettings.ToDictionaryAsync(s => s.Key, s => s.Value);

        return new AppSettingsDto(
            Port: int.TryParse(GetSetting(dbSettings, "Port", _config["LlamaCtrl:Port"]), out var p) ? p : 5000,
            DataDir: GetSetting(dbSettings, "DataDir", _config["LlamaCtrl:DataDir"] ?? "data"),
            ModelsDir: GetSetting(dbSettings, "ModelsDir", _config["LlamaCtrl:ModelsDir"] ?? "models"),
            OpenBrowserOnStart: bool.TryParse(GetSetting(dbSettings, "OpenBrowserOnStart", _config["LlamaCtrl:OpenBrowserOnStart"]), out var ob) && ob,
            HealthPollIntervalSeconds: int.TryParse(GetSetting(dbSettings, "HealthPollIntervalSeconds", _config["LlamaCtrl:HealthPollIntervalSeconds"]), out var hp) ? hp : 10,
            RelaunchOnStartup: bool.TryParse(GetSetting(dbSettings, "RelaunchOnStartup", "false"), out var rl) && rl
        );
    }

    public async Task<string> GetLegacyBinaryPathAsync()
    {
        var dbSettings = await _db.AppSettings.ToDictionaryAsync(s => s.Key, s => s.Value);
        return GetSetting(dbSettings, "LlamaServerBinary", _config["LlamaCtrl:LlamaServerBinary"] ?? "llama-server");
    }

    public async Task<AppSettingsDto> UpdateAsync(UpdateSettingsDto dto)
    {
        if (dto.ModelsDir != null) await UpsertSettingAsync("ModelsDir", dto.ModelsDir);
        if (dto.OpenBrowserOnStart.HasValue) await UpsertSettingAsync("OpenBrowserOnStart", dto.OpenBrowserOnStart.Value.ToString());
        if (dto.HealthPollIntervalSeconds.HasValue) await UpsertSettingAsync("HealthPollIntervalSeconds", dto.HealthPollIntervalSeconds.Value.ToString());
        if (dto.RelaunchOnStartup.HasValue) await UpsertSettingAsync("RelaunchOnStartup", dto.RelaunchOnStartup.Value.ToString());

        await _db.SaveChangesAsync();
        return await GetAsync();
    }

    public Task<SystemStatusDto> GetSystemStatusAsync()
    {
        return Task.FromResult(new SystemStatusDto(0, 0, 0, 0, 0, 0));
    }

    public async Task<List<string>> GetModelsAsync()
    {
        var settings = await GetAsync();
        var modelsDir = settings.ModelsDir;

        if (!Directory.Exists(modelsDir))
            return new List<string>();

        return Directory.GetFiles(modelsDir, "*.gguf", SearchOption.AllDirectories)
            .Select(Path.GetFileName)
            .Where(f => f != null)
            .Cast<string>()
            .OrderBy(f => f)
            .ToList();
    }

    public Task<List<GpuInfoDto>> GetGpusAsync()
    {
        return Task.FromResult(new List<GpuInfoDto>());
    }

    public async Task ResetToDefaultsAsync()
    {
        _db.AppSettings.RemoveRange(_db.AppSettings);
        await _db.SaveChangesAsync();
    }

    public Task OpenDataDirAsync()
    {
        var dataDir = _config["LlamaCtrl:DataDir"] ?? "";
        try
        {
            if (OperatingSystem.IsWindows())
                System.Diagnostics.Process.Start(
                    new System.Diagnostics.ProcessStartInfo("explorer", dataDir) { UseShellExecute = true });
            else if (OperatingSystem.IsMacOS())
                System.Diagnostics.Process.Start("open", dataDir);
            else
                System.Diagnostics.Process.Start("xdg-open", dataDir);
        }
        catch { }
        return Task.CompletedTask;
    }

    private static string GetSetting(Dictionary<string, string> dbSettings, string key, string? fallback)
    {
        return dbSettings.TryGetValue(key, out var value) ? value : fallback ?? "";
    }

    private async Task UpsertSettingAsync(string key, string value)
    {
        var existing = await _db.AppSettings.FirstOrDefaultAsync(s => s.Key == key);
        if (existing != null)
        {
            existing.Value = value;
        }
        else
        {
            _db.AppSettings.Add(new AppSetting { Key = key, Value = value });
        }
    }
}
