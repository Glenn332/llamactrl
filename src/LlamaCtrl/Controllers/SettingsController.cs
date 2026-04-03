using LlamaCtrl.Data;
using LlamaCtrl.Services;
using LlamaCtrl.Services.Dtos;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace LlamaCtrl.Controllers;

[Route("api/settings")]
public class SettingsController : ApiControllerBase
{
    private readonly ISettingsService _svc;
    private readonly IConfiguration _config;

    public SettingsController(ISettingsService svc, IConfiguration config)
    {
        _svc = svc;
        _config = config;
    }

    [HttpGet] public async Task<IActionResult> Get() => OkResponse(await _svc.GetAsync());

    [HttpPut] public async Task<IActionResult> Update([FromBody] UpdateSettingsDto dto)
    {
        try { return OkResponse(await _svc.UpdateAsync(dto)); }
        catch (Exception e) { return ErrorResponse(e.Message); }
    }

    [HttpGet("export-db")]
    public IActionResult ExportDb()
    {
        var dataDir = _config["LlamaCtrl:DataDir"] ?? "";
        var dbPath = Path.Combine(dataDir, "llamactrl.db");
        if (!System.IO.File.Exists(dbPath))
            return ErrorResponse("Database file not found");

        var bytes = System.IO.File.ReadAllBytes(dbPath);
        return File(bytes, "application/octet-stream", "llamactrl.db");
    }

    [HttpPost("import-db")]
    public async Task<IActionResult> ImportDb(IFormFile file, [FromServices] AppDbContext db)
    {
        if (file == null || file.Length == 0)
            return ErrorResponse("No file provided");

        var tempPath = Path.GetTempFileName();
        try
        {
            using (var fs = new FileStream(tempPath, FileMode.Create))
                await file.CopyToAsync(fs);

            using var sourceConn = new SqliteConnection($"Data Source={tempPath}");
            sourceConn.Open();

            await db.Database.OpenConnectionAsync();
            var destConn = (SqliteConnection)db.Database.GetDbConnection();
            sourceConn.BackupDatabase(destConn);

            return OkResponse("Import successful");
        }
        catch (Exception e)
        {
            return ErrorResponse(e.Message);
        }
        finally
        {
            if (System.IO.File.Exists(tempPath))
                System.IO.File.Delete(tempPath);
        }
    }

    [HttpPost("reset")]
    public async Task<IActionResult> Reset()
    {
        try
        {
            await _svc.ResetToDefaultsAsync();
            return OkResponse(await _svc.GetAsync());
        }
        catch (Exception e) { return ErrorResponse(e.Message); }
    }

    [HttpPost("open-data-dir")]
    public async Task<IActionResult> OpenDataDir()
    {
        try
        {
            await _svc.OpenDataDirAsync();
            return OkResponse("Opened");
        }
        catch (Exception e) { return ErrorResponse(e.Message); }
    }
}
