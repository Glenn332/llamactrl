using Microsoft.AspNetCore.Mvc;

namespace LlamaCtrl.Controllers;

[ApiController]
[Route("api/[controller]")]
public abstract class ApiControllerBase : ControllerBase
{
    protected IActionResult OkResponse<T>(T data) => Ok(ApiResponse<T>.Ok(data));
    protected IActionResult CreatedResponse<T>(T data) => StatusCode(201, ApiResponse<T>.Ok(data));
    protected IActionResult NotFoundResponse(string message) => NotFound(ApiResponse<object>.Fail(message));
    protected IActionResult ErrorResponse(string message) => StatusCode(500, ApiResponse<object>.Fail(message));
}
