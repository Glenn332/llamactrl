using System.Reflection;
using LlamaCtrl.Data;
using Microsoft.Extensions.FileProviders;
using LlamaCtrl.Hubs;
using LlamaCtrl.Infrastructure;
using LlamaCtrl.Infrastructure.SystemInfo;
using LlamaCtrl.Services;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Serilog;

var cliArgs = Environment.GetCommandLineArgs().Skip(1).ToArray();

var version = typeof(Program).Assembly
    .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
    ?.InformationalVersion ?? "dev";

if (cliArgs.Contains("version") || cliArgs.Contains("--version"))
{
    Console.WriteLine($"llamactrl {version}");
    return 0;
}
if (cliArgs.Contains("--help") || cliArgs.Contains("-h"))
{
    Console.WriteLine("""
        llamactrl — llama.cpp Instance Manager

        Usage: llamactrl [options]

        Options:
          --port <port>           Port to listen on (default: 3131)
          --data-dir <path>       Data directory for DB and logs (default: ~/.config/llamactrl on macOS/Linux, %APPDATA%\llamactrl on Windows)
          --models-dir <path>     Directory to scan for .gguf models (default: ~/models)
          --binary <path>         Path to llama-server binary (default: llama-server)
          --no-browser            Don't open browser on startup
          --log-level <level>     Logging level: Verbose/Debug/Information/Warning/Error (default: Information)
          --help                  Show this help
          version                 Show version
          update                  Check for updates and install the latest release
        """);
    return 0;
}
if (cliArgs.Contains("update") || cliArgs.Contains("--update"))
{
    const string repo = "Glenn332/llamactrl";

    Console.WriteLine($"  Current version : {version}");
    Console.Write("  Checking latest ...");

    string latestTag;
    try
    {
        using var http = new HttpClient();
        http.DefaultRequestHeaders.Add("User-Agent", $"llamactrl/{version}");
        var json = await http.GetStringAsync($"https://api.github.com/repos/{repo}/releases/latest");
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        latestTag = doc.RootElement.GetProperty("tag_name").GetString() ?? "";
        Console.WriteLine($" {latestTag}");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"\nFailed to fetch release information: {ex.Message}");
        return 1;
    }

    var currentCore = version.TrimStart('v').Split('+')[0];
    var latestCore  = latestTag.TrimStart('v').Split('+')[0];

    if (version != "dev" && currentCore == latestCore)
    {
        Console.WriteLine("  llamactrl is already up to date.");
        return 0;
    }

    Console.WriteLine($"  Latest version  : {latestTag}");
    Console.WriteLine();

    var prompt = version == "dev"
        ? $"  Install release {latestTag} over local build? [y/N] "
        : $"  Update from {version} to {latestTag}? [y/N] ";
    Console.Write(prompt);
    var answer = (Console.ReadLine() ?? "").Trim();
    if (!answer.Equals("y", StringComparison.OrdinalIgnoreCase) &&
        !answer.Equals("yes", StringComparison.OrdinalIgnoreCase))
    {
        Console.WriteLine("  Update cancelled.");
        return 0;
    }

    var arch = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture;
    string rid;
    if (OperatingSystem.IsMacOS())
        rid = arch == System.Runtime.InteropServices.Architecture.Arm64 ? "osx-arm64" : "osx-x64";
    else if (OperatingSystem.IsWindows())
        rid = arch == System.Runtime.InteropServices.Architecture.Arm64 ? "win-arm64" : "win-x64";
    else
        rid = arch == System.Runtime.InteropServices.Architecture.Arm64 ? "linux-arm64" : "linux-x64";

    var isWindows   = OperatingSystem.IsWindows();
    var ext         = isWindows ? "zip" : "tar.gz";
    var assetName   = $"llamactrl-{rid}.{ext}";
    var downloadUrl = $"https://github.com/{repo}/releases/download/{latestTag}/{assetName}";

    Console.WriteLine($"  Downloading {assetName} ...");

    var tempDir = Path.Combine(Path.GetTempPath(), $"llamactrl-update-{Guid.NewGuid():N}");
    Directory.CreateDirectory(tempDir);
    var archivePath = Path.Combine(tempDir, assetName);

    try
    {
        using var http = new HttpClient();
        http.DefaultRequestHeaders.Add("User-Agent", $"llamactrl/{version}");
        var bytes = await http.GetByteArrayAsync(downloadUrl);
        await File.WriteAllBytesAsync(archivePath, bytes);

        Console.WriteLine("  Extracting ...");
        var extractDir = Path.Combine(tempDir, "extracted");
        Directory.CreateDirectory(extractDir);

        if (isWindows)
        {
            System.IO.Compression.ZipFile.ExtractToDirectory(archivePath, extractDir, overwriteFiles: true);
        }
        else
        {
            var tar = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("tar",
                $"-xzf \"{archivePath}\" -C \"{extractDir}\"") { UseShellExecute = false });
            tar?.WaitForExit();
            if (tar?.ExitCode != 0) throw new Exception("Archive extraction failed.");
        }

        var binaryName = isWindows ? "llamactrl.exe" : "llamactrl";
        var newBinary  = Path.Combine(extractDir, binaryName);
        if (!File.Exists(newBinary))
            throw new Exception($"Expected binary not found in archive: {binaryName}");

        var currentExe = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName
            ?? throw new Exception("Cannot determine path of the current executable.");

        Console.WriteLine($"  Installing to {currentExe} ...");

        if (isWindows)
        {
            var bat = Path.Combine(tempDir, "finish_update.bat");
            await File.WriteAllTextAsync(bat, $"""
                @echo off
                timeout /t 2 /nobreak >nul
                move /y "{newBinary}" "{currentExe}"
                del "%~f0"
                """);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd", $"/c \"{bat}\"")
                { CreateNoWindow = true, UseShellExecute = true });
        }
        else
        {
            var backup = currentExe + ".bak";
            File.Copy(currentExe, backup, overwrite: true);
            try
            {
                File.Copy(newBinary, currentExe, overwrite: true);
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("chmod",
                    $"+x \"{currentExe}\"") { UseShellExecute = false })?.WaitForExit();
                File.Delete(backup);
            }
            catch
            {
                try { File.Copy(backup, currentExe, overwrite: true); } catch { }
                throw;
            }
        }

        Console.WriteLine($"  Successfully updated to {latestTag}.");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"  Update failed: {ex.Message}");
        return 1;
    }
    finally
    {
        if (!isWindows)
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best-effort */ }
    }

    return 0;
}

static string? GetArg(string[] args, string key)
{
    var idx = Array.IndexOf(args, key);
    return idx >= 0 && idx + 1 < args.Length ? args[idx + 1] : null;
}

var port       = int.TryParse(GetArg(cliArgs, "--port"), out var p) ? p : 3131;
var dataDir    = GetArg(cliArgs, "--data-dir") ?? (
    System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows)
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "llamactrl")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "llamactrl")
);
var modelsDir  = GetArg(cliArgs, "--models-dir");
var binary     = GetArg(cliArgs, "--binary");
var logLevel   = GetArg(cliArgs, "--log-level")   ?? "Information";
var noBrowser  = cliArgs.Contains("--no-browser");

dataDir   = ExpandPath(dataDir);
modelsDir = modelsDir != null ? ExpandPath(modelsDir)
          : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "models");

Directory.CreateDirectory(dataDir);

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Is(Enum.Parse<Serilog.Events.LogEventLevel>(logLevel, true))
    .MinimumLevel.Override("Microsoft", Serilog.Events.LogEventLevel.Warning)
    .MinimumLevel.Override("System.Net.Http", Serilog.Events.LogEventLevel.Warning)
    .WriteTo.Console()
    .WriteTo.File(Path.Combine(dataDir, "llamactrl.log"), rollingInterval: RollingInterval.Day)
    .CreateLogger();

var builder = WebApplication.CreateBuilder(cliArgs);
builder.Host.UseSerilog();

IFileProvider? uiFileProvider = null;
var embedded = new EmbeddedFileProvider(typeof(Program).Assembly, "LlamaCtrl.wwwroot");
if (embedded.GetFileInfo("index.html").Exists)
    uiFileProvider = embedded;

if (uiFileProvider == null)
{
    var exeDir = Path.GetDirectoryName(
        System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName
        ?? AppContext.BaseDirectory) ?? AppContext.BaseDirectory;
    var physicalPath = new[]
    {
        Path.Combine(exeDir, "wwwroot"),
        Path.Combine(AppContext.BaseDirectory, "wwwroot"),
        Path.Combine(builder.Environment.ContentRootPath, "wwwroot"),
    }.FirstOrDefault(Directory.Exists);
    if (physicalPath != null)
        uiFileProvider = new PhysicalFileProvider(physicalPath);
}

builder.Configuration["LlamaCtrl:Port"]    = port.ToString();
builder.Configuration["LlamaCtrl:DataDir"] = dataDir;
builder.Configuration["LlamaCtrl:ModelsDir"] = modelsDir;
if (binary != null) builder.Configuration["LlamaCtrl:LlamaServerBinary"] = binary;

var dbPath = Path.Combine(dataDir, "llamactrl.db");
builder.Services.AddDbContext<AppDbContext>(opt =>
    opt.UseSqlite($"Data Source={dbPath}"));

builder.Services.AddSignalR()
    .AddJsonProtocol(options =>
    {
        options.PayloadSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
    });

builder.Services.AddCors(o => o.AddDefaultPolicy(pol =>
    pol.SetIsOriginAllowed(_ => true).AllowAnyMethod().AllowAnyHeader().AllowCredentials()));

builder.Services.AddSingleton<ProcessManager>();
builder.Services.AddSingleton<MetricsCollector>();
builder.Services.AddSingleton<SystemInfoService>();
builder.Services.AddHostedService<SystemMetricsCollector>();
builder.Services.AddSingleton<BenchmarkProgressStore>();
builder.Services.AddSingleton<DownloadProgressStore>();

builder.Services.AddScoped<IInstanceService,  InstanceService>();
builder.Services.AddScoped<IProfileService,   ProfileService>();
builder.Services.AddScoped<IBenchmarkService, BenchmarkService>();
builder.Services.AddScoped<ISettingsService,  SettingsService>();
builder.Services.AddScoped<IModelService,     ModelService>();
builder.Services.AddScoped<IBinaryService, BinaryService>();
builder.Services.AddScoped<IModelDirectoryService, ModelDirectoryService>();

builder.Services.AddHttpClient("HuggingFace", client =>
{
    client.BaseAddress = new Uri("https://huggingface.co");
    client.DefaultRequestHeaders.Add("User-Agent", $"llamactrl/{version}");
    client.Timeout = TimeSpan.FromMinutes(30);
});

builder.Services.AddHostedService<HealthPoller>();

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
    c.SwaggerDoc("v1", new() { Title = "LlamaCtrl API", Version = "v1" }));

var app = builder.Build();

var processManager = app.Services.GetRequiredService<ProcessManager>();
var logHub         = app.Services.GetRequiredService<IHubContext<LogHub>>();
processManager.SetLogHub(logHub);

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.EnsureCreatedAsync();

    var existingColumns = db.Database
        .SqlQueryRaw<string>("SELECT name FROM pragma_table_info('BenchmarkResults')")
        .AsEnumerable().ToHashSet();
    if (!existingColumns.Contains("ChartDataJson"))
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE BenchmarkResults ADD COLUMN ChartDataJson TEXT");

    if (!existingColumns.Contains("BenchmarkType"))
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE BenchmarkResults ADD COLUMN BenchmarkType TEXT NOT NULL DEFAULT 'token-generation'");

    if (!existingColumns.Contains("RoundsJson"))
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE BenchmarkResults ADD COLUMN RoundsJson TEXT NULL");

    var profileColumns = await db.Database
        .SqlQueryRaw<string>("SELECT name FROM pragma_table_info('Profiles')")
        .ToListAsync();

    if (!profileColumns.Contains("ParametersJson"))
        await db.Database.ExecuteSqlRawAsync(
            "ALTER TABLE Profiles ADD COLUMN ParametersJson TEXT");

    if (!profileColumns.Contains("CustomArgsJson"))
        await db.Database.ExecuteSqlRawAsync(
            "ALTER TABLE Profiles ADD COLUMN CustomArgsJson TEXT");

    if (profileColumns.Contains("ContextSize"))
    {
        await db.Database.ExecuteSqlRawAsync("""
            UPDATE Profiles
            SET ParametersJson = json_object(
                '-c',      CAST(ContextSize AS TEXT),
                '-b',      CAST(BatchSize AS TEXT),
                '-ngl',    CAST(GpuLayers AS TEXT),
                '-t',      CAST(Threads AS TEXT),
                '-fa',     CASE WHEN UseFlashAttn = 1 THEN 'on' ELSE 'off' END,
                '--temp',  CAST(Temperature AS TEXT),
                '--top-p', CAST(TopP AS TEXT),
                '--top-k', CAST(TopK AS TEXT)
            )
            WHERE ParametersJson IS NULL
            """);

        await db.Database.ExecuteSqlRawAsync("""
            UPDATE Profiles
            SET ParametersJson = json_insert(ParametersJson, '$."--no-mmap"', '')
            WHERE UseMmap = 0
            """);

        await db.Database.ExecuteSqlRawAsync("""
            UPDATE Profiles
            SET ParametersJson = json_insert(ParametersJson, '$."--mlock"', '')
            WHERE UseMlock = 1
            """);

        await db.Database.ExecuteSqlRawAsync("""
            UPDATE Profiles
            SET ParametersJson = json_insert(ParametersJson, '$."-sp"', SystemPrompt)
            WHERE SystemPrompt IS NOT NULL AND SystemPrompt != ''
            """);

        await db.Database.ExecuteSqlRawAsync(
            "UPDATE Profiles SET SystemPrompt = NULL WHERE SystemPrompt IS NOT NULL");
        await db.Database.ExecuteSqlRawAsync(
            "UPDATE Profiles SET UseMmap = 1 WHERE UseMmap = 0");
        await db.Database.ExecuteSqlRawAsync(
            "UPDATE Profiles SET UseMlock = 0 WHERE UseMlock = 1");
    }

    await db.Database.ExecuteSqlRawAsync("""
        CREATE TABLE IF NOT EXISTS LlamaServerBinaries (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            Name TEXT NOT NULL,
            Path TEXT NOT NULL,
            IsDefault INTEGER NOT NULL DEFAULT 0,
            CreatedAt TEXT NOT NULL,
            UpdatedAt TEXT NOT NULL
        )
        """);

    await db.Database.ExecuteSqlRawAsync("""
        CREATE TABLE IF NOT EXISTS ModelDirectories (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            Name TEXT NOT NULL,
            Path TEXT NOT NULL,
            CreatedAt TEXT NOT NULL,
            UpdatedAt TEXT NOT NULL
        )
        """);

    var profileCols = await db.Database.SqlQueryRaw<string>("SELECT name FROM pragma_table_info('Profiles')").ToListAsync();
    if (!profileCols.Contains("SelectedBinaryId"))
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE Profiles ADD COLUMN SelectedBinaryId INTEGER");

    var binaryCount = await db.LlamaServerBinaries.CountAsync();
    if (binaryCount == 0)
    {
        var seedBinary = binary ?? "llama-server";
        await db.Database.ExecuteSqlRawAsync(
            "INSERT INTO LlamaServerBinaries (Name, Path, IsDefault, CreatedAt, UpdatedAt) VALUES ('Default', {0}, 1, {1}, {2})",
            seedBinary, DateTime.UtcNow.ToString("o"), DateTime.UtcNow.ToString("o"));
    }

    var dirCount = await db.ModelDirectories.CountAsync();
    if (dirCount == 0)
    {
        await db.Database.ExecuteSqlRawAsync(
            "INSERT INTO ModelDirectories (Name, Path, CreatedAt, UpdatedAt) VALUES ('Models', {0}, {1}, {2})",
            modelsDir, DateTime.UtcNow.ToString("o"), DateTime.UtcNow.ToString("o"));
    }
}

using (var scope = app.Services.CreateScope())
{
    var db      = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var instSvc = scope.ServiceProvider.GetRequiredService<IInstanceService>();

    var relaunchSetting = await db.AppSettings.FirstOrDefaultAsync(s => s.Key == "RelaunchOnStartup");
    var relaunch = bool.TryParse(relaunchSetting?.Value, out var r) && r;

    var staleInstances = await db.Instances
        .Where(i => i.Status == LlamaCtrl.Domain.Enums.InstanceStatus.Running
                 || i.Status == LlamaCtrl.Domain.Enums.InstanceStatus.Starting
                 || i.Status == LlamaCtrl.Domain.Enums.InstanceStatus.Error)
        .ToListAsync();

    if (!relaunch)
    {
        foreach (var inst in staleInstances)
        {
            inst.Status = LlamaCtrl.Domain.Enums.InstanceStatus.Stopped;
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
                inst.Status = LlamaCtrl.Domain.Enums.InstanceStatus.Error;
            }
        }
        await db.SaveChangesAsync();
    }
}

app.UseCors();
app.UseSwagger();
app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "LlamaCtrl API v1"));

if (uiFileProvider != null)
{
    app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = uiFileProvider });
    app.UseStaticFiles(new StaticFileOptions   { FileProvider = uiFileProvider });
}
else
{
    Log.Warning("No UI assets found — UI will not be served. Run 'npm run build' in the frontend/ directory first.");
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
            "UI not available — run 'npm run build' in the frontend/ directory first.");
    }
});

var lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
lifetime.ApplicationStopping.Register(() =>
{
    Log.Information("Stopping all llama-server processes...");
    processManager.StopAllAsync().GetAwaiter().GetResult();
    Log.CloseAndFlush();
});

var shouldOpenBrowser = !noBrowser;
if (shouldOpenBrowser)
{
    using var settingsScope = app.Services.CreateScope();
    var settingsDb = settingsScope.ServiceProvider.GetRequiredService<LlamaCtrl.Data.AppDbContext>();
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
        var url = $"http://localhost:{port}";
        Log.Information("Opening browser at {Url}", url);
        OpenBrowser(url);
    });
}

Log.Information("LlamaCtrl starting on http://localhost:{Port}", port);
Log.Information("Data directory: {DataDir}", dataDir);
Log.Information("Swagger UI: http://localhost:{Port}/swagger", port);

await app.RunAsync($"http://localhost:{port}");
return 0;

static string ExpandPath(string path)
{
    if (path.StartsWith("~/") || path == "~")
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return path.Replace("~", home);
    }
    return path;
}

static void OpenBrowser(string url)
{
    try
    {
        if (OperatingSystem.IsWindows())
            System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo("cmd", $"/c start {url}") { CreateNoWindow = true });
        else if (OperatingSystem.IsMacOS())
            System.Diagnostics.Process.Start("open", url);
        else
            System.Diagnostics.Process.Start("xdg-open", url);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Could not open browser: {ex.Message}");
    }
}
