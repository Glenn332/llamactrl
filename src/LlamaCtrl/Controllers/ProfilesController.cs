using LlamaCtrl.Services;
using LlamaCtrl.Services.Dtos;
using Microsoft.AspNetCore.Mvc;

namespace LlamaCtrl.Controllers;

[Route("api/profiles")]
public class ProfilesController : ApiControllerBase
{
    private readonly IProfileService _svc;
    public ProfilesController(IProfileService svc) => _svc = svc;

    [HttpGet] public async Task<IActionResult> GetAll() => OkResponse(await _svc.GetAllAsync());

    [HttpGet("{id}")] public async Task<IActionResult> GetById(int id)
    {
        try { return OkResponse(await _svc.GetByIdAsync(id)); }
        catch (KeyNotFoundException e) { return NotFoundResponse(e.Message); }
    }

    [HttpPost] public async Task<IActionResult> Create([FromBody] CreateProfileDto dto)
    {
        try { return CreatedResponse(await _svc.CreateAsync(dto)); }
        catch (InvalidOperationException e) { return ErrorResponse(e.Message); }
        catch (Exception e) { return ErrorResponse(e.Message); }
    }

    [HttpPut("{id}")] public async Task<IActionResult> Update(int id, [FromBody] UpdateProfileDto dto)
    {
        try { return OkResponse(await _svc.UpdateAsync(id, dto)); }
        catch (KeyNotFoundException e) { return NotFoundResponse(e.Message); }
        catch (InvalidOperationException e) { return ErrorResponse(e.Message); }
    }

    [HttpDelete("{id}")] public async Task<IActionResult> Delete(int id)
    {
        try { await _svc.DeleteAsync(id); return NoContent(); }
        catch (KeyNotFoundException e) { return NotFoundResponse(e.Message); }
    }

    [HttpPost("{id}/clone")] public async Task<IActionResult> Clone(int id)
    {
        try { return CreatedResponse(await _svc.CloneAsync(id)); }
        catch (KeyNotFoundException e) { return NotFoundResponse(e.Message); }
    }

    [HttpPost("{id}/launch")] public async Task<IActionResult> Launch(int id)
    {
        try { return CreatedResponse(await _svc.LaunchAsync(id)); }
        catch (KeyNotFoundException e) { return NotFoundResponse(e.Message); }
        catch (Exception e) { return ErrorResponse(e.Message); }
    }

}
