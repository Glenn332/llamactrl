using System.Diagnostics;
using LlamaCtrl.Data;
using LlamaCtrl.Domain.Enums;
using LlamaCtrl.Hubs;
using LlamaCtrl.Infrastructure;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Serilog;

namespace LlamaCtrl.Startup;

static class MiddlewarePipeline
{
    internal static void Configure(WebApplication app, IFileProvider? uiFileProvider)
    {
        app.UseCors();
        app.UseSwagger();
        app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "LlamaCtrl API v1"));

        if (uiFileProvider != null)
        {
            app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = uiFileProvider });
            app.UseStaticFiles(new StaticFileOptions { FileProvider = uiFileProvider });
        }
        else
        {
            Log.Warning("No UI assets found — UI will not be served. Run 'npm run build' in the src/frontend/ directory first.");
        }

        app.MapControllers();
        app.MapHub<LogHub>("/hubs/logs");
        app.MapHub<MetricsHub>("/hubs/metrics");
        app.MapHub<DownloadHub>("/hubs/downloads");

        app.MapFallback(async ctx =>
        {
            var indexFile = uiFileProvider?.GetFileInfo("index.html");
            if (indexFile?.Exists == true)
            {
                ctx.Response.ContentType = "text/html; charset=utf-8";
                using var stream = indexFile.CreateReadStream();
                await stream.CopyToAsync(ctx.Response.Body);
            }
            else
            {
                ctx.Response.ContentType = "text/plain";
                ctx.Response.StatusCode = 503;
                await ctx.Response.WriteAsync(
                    "UI not available — run 'npm run build' in the src/frontend/ directory first.");
            }
        });
    }

    internal static async Task RegisterLifetimeHooksAsync(WebApplication app, CliOptions options)
    {
        var lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
        var processManager = app.Services.GetRequiredService<ProcessManager>();

        lifetime.ApplicationStopping.Register(() =>
        {
            Log.Information("Stopping all llama-server processes...");
            processManager.StopAllAsync().GetAwaiter().GetResult();
            Log.CloseAndFlush();
        });

        var scopeFactory = app.Services.GetRequiredService<IServiceScopeFactory>();
        var metricsHub = app.Services.GetRequiredService<IHubContext<MetricsHub>>();
        processManager.OnInstanceReady += instanceId =>
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    using var scope = scopeFactory.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                    var instance = await db.Instances.FindAsync(instanceId);
                    if (instance is { Status: InstanceStatus.Starting })
                    {
                        instance.Status = InstanceStatus.Running;
                        instance.UpdatedAt = DateTime.UtcNow;
                        await db.SaveChangesAsync();
                        await metricsHub.Clients.Group("metrics")
                            .SendAsync("InstanceStatusChanged", instanceId, "Running");
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Failed to mark instance {Id} as running", instanceId);
                }
            });
        };

        var shouldOpenBrowser = !options.NoBrowser;
        if (shouldOpenBrowser)
        {
            using var settingsScope = app.Services.CreateScope();
            var settingsDb = settingsScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var openBrowserSetting = await settingsDb.AppSettings
                .Where(s => s.Key == "OpenBrowserOnStart")
                .Select(s => s.Value)
                .FirstOrDefaultAsync();
            if (openBrowserSetting != null && bool.TryParse(openBrowserSetting, out var dbValue))
                shouldOpenBrowser = dbValue;
        }

        if (shouldOpenBrowser)
        {
            lifetime.ApplicationStarted.Register(() =>
            {
                var url = $"http://localhost:{options.Port}";
                Log.Information("Opening browser at {Url}", url);
                OpenBrowser(url);
            });
        }
    }

    private static void OpenBrowser(string url)
    {
        try
        {
            if (OperatingSystem.IsWindows())
                Process.Start(
                    new ProcessStartInfo("cmd", $"/c start {url}") { CreateNoWindow = true });
            else if (OperatingSystem.IsMacOS())
                Process.Start("open", url);
            else
                Process.Start("xdg-open", url);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Could not open browser: {ex.Message}");
        }
    }
}
