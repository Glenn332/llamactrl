using System.Text.Json;
using LlamaCtrl.Services;
using LlamaCtrl.Services.Dtos;
using Microsoft.AspNetCore.Mvc;

namespace LlamaCtrl.Controllers;

[Route("api/benchmarks")]
public class BenchmarksController : ApiControllerBase
{
    private readonly IBenchmarkService _svc;
    private readonly BenchmarkProgressStore _store;

    public BenchmarksController(IBenchmarkService svc, BenchmarkProgressStore store)
    {
        _svc   = svc;
        _store = store;
    }

    [HttpGet] public async Task<IActionResult> GetAll() => OkResponse(await _svc.GetAllAsync());

    [HttpGet("{id}")] public async Task<IActionResult> GetById(int id)
    {
        try { return OkResponse(await _svc.GetByIdAsync(id)); }
        catch (KeyNotFoundException e) { return NotFoundResponse(e.Message); }
    }

    [HttpPost("start")]
    public IActionResult Start([FromBody] RunBenchmarkDto dto)
    {
        try   { _svc.StartBackgroundRun(dto); return OkResponse(new { started = true }); }
        catch (InvalidOperationException e) { return ErrorResponse(e.Message); }
        catch (Exception e)                 { return ErrorResponse(e.Message); }
    }

    [HttpGet("progress")]
    public IActionResult GetProgress()
    {
        var snap = _store.GetSnapshot();
        return OkResponse(snap);
    }

    [HttpPost("cancel")]
    public IActionResult Cancel()
    {
        _store.Cancel();
        return OkResponse(new { cancelled = true });
    }

    [HttpPost("run")] public async Task<IActionResult> Run([FromBody] RunBenchmarkDto dto)
    {
        try { return CreatedResponse(await _svc.RunAsync(dto)); }
        catch (KeyNotFoundException e) { return NotFoundResponse(e.Message); }
        catch (Exception e) { return ErrorResponse(e.Message); }
    }

    [HttpPost("run-stream")]
    public async Task RunStream([FromBody] RunBenchmarkDto dto, CancellationToken ct)
    {
        Response.ContentType = "text/event-stream";
        Response.Headers["Cache-Control"] = "no-cache";
        Response.Headers["X-Accel-Buffering"] = "no";
        Response.Headers["Content-Encoding"] = "identity";

        var bodyFeature = HttpContext.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpResponseBodyFeature>();
        bodyFeature?.DisableBuffering();

        await Response.StartAsync(ct);

        var opts = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        };

        try
        {
            await foreach (var evt in _svc.RunStreamAsync(dto, ct))
            {
                var json = JsonSerializer.Serialize(evt, opts);
                await Response.WriteAsync($"data: {json}\n\n", ct);
                await Response.Body.FlushAsync(ct);
                await Task.Yield();
            }
        }
        catch (OperationCanceledException) {  }
        catch (Exception ex)
        {
            var err = JsonSerializer.Serialize(new { type = "error", error = ex.Message });
            await Response.WriteAsync($"data: {err}\n\n", ct);
            await Response.Body.FlushAsync(ct);
        }
    }

    [HttpDelete]
    public async Task<IActionResult> Delete([FromQuery] string ids)
    {
        var parsed = ids.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => int.Parse(s.Trim()))
            .ToArray();
        await _svc.DeleteAsync(parsed);
        return OkResponse(new { deleted = parsed.Length });
    }

    [HttpGet("compare")] public async Task<IActionResult> Compare([FromQuery] string ids)
    {
        var parsed = ids.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => int.Parse(s.Trim()))
            .ToArray();
        return OkResponse(await _svc.GetCompareAsync(parsed));
    }

    [HttpGet("export")] public async Task<IActionResult> Export([FromQuery] string fmt = "csv")
    {
        var all = await _svc.GetAllAsync();
        var allIds = all.Select(b => b.Id).ToArray();
        var bytes = await _svc.ExportCsvAsync(allIds);
        return File(bytes, "text/csv", "benchmarks.csv");
    }
}
