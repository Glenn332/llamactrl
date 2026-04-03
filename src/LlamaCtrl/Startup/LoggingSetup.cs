using Serilog;

namespace LlamaCtrl.Startup;

static class LoggingSetup
{
    internal static void ConfigureSerilog(CliOptions options)
    {
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Is(Enum.Parse<Serilog.Events.LogEventLevel>(options.LogLevel, true))
            .MinimumLevel.Override("Microsoft", Serilog.Events.LogEventLevel.Warning)
            .MinimumLevel.Override("System.Net.Http", Serilog.Events.LogEventLevel.Warning)
            .WriteTo.Console()
            .WriteTo.File(Path.Combine(options.DataDir, "llamactrl.log"), rollingInterval: RollingInterval.Day)
            .CreateLogger();
    }
}
