using LlamaCtrl.Services;
using LlamaCtrl.Services.Dtos;
using Microsoft.AspNetCore.Mvc;

namespace LlamaCtrl.Controllers;

[Route("api/instances")]
public class InstancesController : ApiControllerBase
{
    private readonly IInstanceService _svc;
    public InstancesController(IInstanceService svc) => _svc = svc;

    [HttpGet] public async Task<IActionResult> GetAll() => OkResponse(await _svc.GetAllAsync());
    [HttpGet("{id}")] public async Task<IActionResult> GetById(int id)
    {
        try { return OkResponse(await _svc.GetByIdAsync(id)); }
        catch (KeyNotFoundException e) { return NotFoundResponse(e.Message); }
    }
    [HttpPost] public async Task<IActionResult> Create([FromBody] CreateInstanceDto dto)
    {
        try { return CreatedResponse(await _svc.CreateAsync(dto)); }
        catch (Exception e) { return ErrorResponse(e.Message); }
    }
    [HttpPut("{id}")] public async Task<IActionResult> Update(int id, [FromBody] UpdateInstanceDto dto)
    {
        try { return OkResponse(await _svc.UpdateAsync(id, dto)); }
        catch (KeyNotFoundException e) { return NotFoundResponse(e.Message); }
    }
    [HttpDelete("{id}")] public async Task<IActionResult> Delete(int id)
    {
        try { await _svc.DeleteAsync(id); return NoContent(); }
        catch (KeyNotFoundException e) { return NotFoundResponse(e.Message); }
    }
    [HttpPost("{id}/start")] public async Task<IActionResult> Start(int id)
    {
        try { await _svc.StartAsync(id); return OkResponse<object>(new { message = "Started" }); }
        catch (KeyNotFoundException e) { return NotFoundResponse(e.Message); }
        catch (Exception e) { return ErrorResponse(e.Message); }
    }
    [HttpPost("{id}/stop")] public async Task<IActionResult> Stop(int id)
    {
        try { await _svc.StopAsync(id); return OkResponse<object>(new { message = "Stopped" }); }
        catch (KeyNotFoundException e) { return NotFoundResponse(e.Message); }
    }
    [HttpGet("{id}/metrics")] public async Task<IActionResult> GetMetrics(int id)
    {
        try { return OkResponse(await _svc.GetMetricsAsync(id)); }
        catch (KeyNotFoundException e) { return NotFoundResponse(e.Message); }
    }
}
