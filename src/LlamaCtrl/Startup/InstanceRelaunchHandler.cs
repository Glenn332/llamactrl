using LlamaCtrl.Data;
using LlamaCtrl.Domain.Enums;
using LlamaCtrl.Services;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace LlamaCtrl.Startup;

static class InstanceRelaunchHandler
{
    internal static async Task HandleStaleInstancesAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var instSvc = scope.ServiceProvider.GetRequiredService<IInstanceService>();

        var relaunchSetting = await db.AppSettings.FirstOrDefaultAsync(s => s.Key == "RelaunchOnStartup");
        var relaunch = bool.TryParse(relaunchSetting?.Value, out var r) && r;

        var staleInstances = await db.Instances
            .Where(i => i.Status == InstanceStatus.Running
                     || i.Status == InstanceStatus.Starting
                     || i.Status == InstanceStatus.Error)
            .ToListAsync();

        if (!relaunch)
        {
            foreach (var inst in staleInstances)
            {
                inst.Status = InstanceStatus.Stopped;
                inst.Pid = null;
            }
            await db.SaveChangesAsync();
            Log.Information("Reset {Count} stale instance(s) to Stopped", staleInstances.Count);
        }
        else
        {
            foreach (var inst in staleInstances)
            {
                try
                {
                    Log.Information("Relaunching instance {Id} ({Name})...", inst.Id, inst.Name);
                    await instSvc.StartAsync(inst.Id);
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Failed to relaunch instance {Id}", inst.Id);
                    inst.Status = InstanceStatus.Error;
                }
            }
            await db.SaveChangesAsync();
        }
    }
}
