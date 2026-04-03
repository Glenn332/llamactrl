using LlamaCtrl.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace LlamaCtrl.Controllers;

[Route("api/logs")]
public class LogsController : ApiControllerBase
{
    private readonly ProcessManager _processManager;
    public LogsController(ProcessManager processManager) => _processManager = processManager;

    [HttpGet("{instanceId}")]
    public IActionResult GetLogs(int instanceId, [FromQuery] int limit = 500,
        [FromQuery] string level = "all", [FromQuery] string? search = null)
    {
        var process = _processManager.GetProcess(instanceId);
        var lines = process?.GetRecentLines(limit) ?? new List<string>();

        if (!string.IsNullOrEmpty(search))
            lines = lines.Where(l => l.Contains(search, StringComparison.OrdinalIgnoreCase)).ToList();

        if (level != "all")
        {
            var levelUpper = level.ToUpper();
            lines = lines.Where(l => l.Contains($"] {levelUpper}")).ToList();
        }

        return OkResponse(new { instanceId, lines, total = lines.Count });
    }
}
