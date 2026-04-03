using LlamaCtrl.Infrastructure.SystemInfo;
using LlamaCtrl.Services;
using LlamaCtrl.Services.Dtos;
using Microsoft.AspNetCore.Mvc;

namespace LlamaCtrl.Controllers;

[Route("api/system")]
public class SystemController : ApiControllerBase
{
    private readonly IInstanceService _instanceSvc;
    private readonly ISettingsService _settingsSvc;
    private readonly SystemInfoService _sysInfo;

    public SystemController(IInstanceService instanceSvc, ISettingsService settingsSvc, SystemInfoService sysInfo)
    {
        _instanceSvc = instanceSvc;
        _settingsSvc = settingsSvc;
        _sysInfo = sysInfo;
    }

    [HttpGet("status")]
    public async Task<IActionResult> GetStatus()
    {
        var instances = await _instanceSvc.GetAllAsync();
        var active = instances.Count(i => i.Status == "Running");
        return OkResponse(_sysInfo.GetCachedStatus(active));
    }

    [HttpGet("models")]
    public async Task<IActionResult> GetModels()
        => OkResponse(await _settingsSvc.GetModelsAsync());

    [HttpGet("gpus")]
    public IActionResult GetGpus()
        => OkResponse(_sysInfo.GetGpus());
}
