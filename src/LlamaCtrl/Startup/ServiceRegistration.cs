using System.Diagnostics;
using System.Text.Json;
using LlamaCtrl.Data;
using LlamaCtrl.Infrastructure;
using LlamaCtrl.Infrastructure.SystemInfo;
using LlamaCtrl.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;

namespace LlamaCtrl.Startup;

static class ServiceRegistration
{
    internal static IServiceCollection AddLlamaCtrlServices(
        this IServiceCollection services,
        WebApplicationBuilder builder,
        CliOptions options,
        string version)
    {
        builder.Configuration["LlamaCtrl:Port"] = options.Port.ToString();
        builder.Configuration["LlamaCtrl:DataDir"] = options.DataDir;
        builder.Configuration["LlamaCtrl:ModelsDir"] = options.ModelsDir;
        if (options.Binary != null) builder.Configuration["LlamaCtrl:LlamaServerBinary"] = options.Binary;

        var dbPath = Path.Combine(options.DataDir, "llamactrl.db");
        services.AddDbContext<AppDbContext>(opt =>
            opt.UseSqlite($"Data Source={dbPath}"));

        services.AddSignalR()
            .AddJsonProtocol(o =>
            {
                o.PayloadSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
            });

        services.AddCors(o => o.AddDefaultPolicy(pol =>
            pol.SetIsOriginAllowed(_ => true).AllowAnyMethod().AllowAnyHeader().AllowCredentials()));

        services.AddSingleton<ProcessManager>();
        services.AddSingleton<MetricsCollector>();
        services.AddSingleton<SystemInfoService>();
        services.AddHostedService<SystemMetricsCollector>();
        services.AddSingleton<BenchmarkProgressStore>();
        services.AddSingleton<DownloadProgressStore>();

        services.AddScoped<IInstanceService, InstanceService>();
        services.AddScoped<IProfileService, ProfileService>();
        services.AddScoped<IBenchmarkService, BenchmarkService>();
        services.AddScoped<ISettingsService, SettingsService>();
        services.AddScoped<IModelService, ModelService>();
        services.AddScoped<IBinaryService, BinaryService>();
        services.AddScoped<IModelDirectoryService, ModelDirectoryService>();

        services.AddHttpClient("HuggingFace", client =>
        {
            client.BaseAddress = new Uri("https://huggingface.co");
            client.DefaultRequestHeaders.Add("User-Agent", $"llamactrl/{version}");
            client.Timeout = TimeSpan.FromMinutes(30);
        });

        services.AddHostedService<HealthPoller>();

        services.AddControllers();
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(c =>
            c.SwaggerDoc("v1", new() { Title = "LlamaCtrl API", Version = "v1" }));

        return services;
    }

    internal static IFileProvider? ResolveUiFileProvider(WebApplicationBuilder builder)
    {
        var embedded = new EmbeddedFileProvider(typeof(Program).Assembly, "LlamaCtrl.wwwroot");
        if (embedded.GetFileInfo("index.html").Exists)
            return embedded;

        var exeDir = Path.GetDirectoryName(
            Process.GetCurrentProcess().MainModule?.FileName
            ?? AppContext.BaseDirectory) ?? AppContext.BaseDirectory;
        var physicalPath = new[]
        {
            Path.Combine(exeDir, "wwwroot"),
            Path.Combine(AppContext.BaseDirectory, "wwwroot"),
            Path.Combine(builder.Environment.ContentRootPath, "wwwroot"),
        }.FirstOrDefault(Directory.Exists);

        return physicalPath != null ? new PhysicalFileProvider(physicalPath) : null;
    }
}
