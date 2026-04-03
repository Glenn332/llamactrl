using System.Reflection;
using LlamaCtrl.Hubs;
using LlamaCtrl.Infrastructure;
using LlamaCtrl.Startup;
using Microsoft.AspNetCore.SignalR;
using Serilog;

var cliArgs = Environment.GetCommandLineArgs().Skip(1).ToArray();

var version = typeof(Program).Assembly
    .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
    ?.InformationalVersion ?? "dev";

var cliResult = await CliHandler.HandleAsync(cliArgs, version);
if (cliResult.HasValue)
    return cliResult.Value;

var options = CliOptions.Parse(cliArgs);

LoggingSetup.ConfigureSerilog(options);

var builder = WebApplication.CreateBuilder(cliArgs);
builder.Host.UseSerilog();

var uiFileProvider = ServiceRegistration.ResolveUiFileProvider(builder);
builder.Services.AddLlamaCtrlServices(builder, options, version);

var app = builder.Build();

var processManager = app.Services.GetRequiredService<ProcessManager>();
processManager.SetLogHub(app.Services.GetRequiredService<IHubContext<LogHub>>());

await DatabaseMigrator.MigrateAsync(app.Services, options.ModelsDir, options.Binary);
await InstanceRelaunchHandler.HandleStaleInstancesAsync(app.Services);

MiddlewarePipeline.Configure(app, uiFileProvider);
await MiddlewarePipeline.RegisterLifetimeHooksAsync(app, options);

Log.Information("LlamaCtrl starting on http://localhost:{Port}", options.Port);
Log.Information("Data directory: {DataDir}", options.DataDir);
Log.Information("Swagger UI: http://localhost:{Port}/swagger", options.Port);

await app.RunAsync($"http://localhost:{options.Port}");
return 0;
