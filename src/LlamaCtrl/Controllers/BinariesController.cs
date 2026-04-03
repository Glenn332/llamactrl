using LlamaCtrl.Services;
using LlamaCtrl.Services.Dtos;
using Microsoft.AspNetCore.Mvc;

namespace LlamaCtrl.Controllers;

[Route("api/binaries")]
public class BinariesController : ApiControllerBase
{
    private readonly IBinaryService _svc;
    public BinariesController(IBinaryService svc) => _svc = svc;

    [HttpGet]
    public async Task<IActionResult> GetAll() => OkResponse(await _svc.GetAllAsync());

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateBinaryDto dto)
    {
        try { return CreatedResponse(await _svc.CreateAsync(dto)); }
        catch (Exception e) { return ErrorResponse(e.Message); }
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateBinaryDto dto)
    {
        var result = await _svc.UpdateAsync(id, dto);
        return result != null ? OkResponse(result) : NotFoundResponse($"Binary {id} not found");
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var deleted = await _svc.DeleteAsync(id);
        return deleted ? NoContent() : NotFound();
    }
}
