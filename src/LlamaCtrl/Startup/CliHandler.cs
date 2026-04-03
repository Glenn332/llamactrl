using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace LlamaCtrl.Startup;

static class CliHandler
{
    internal static async Task<int?> HandleAsync(string[] args, string version)
    {
        if (args.Contains("version") || args.Contains("--version"))
        {
            Console.WriteLine($"llamactrl {version}");
            return 0;
        }

        if (args.Contains("--help") || args.Contains("-h"))
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

        if (args.Contains("update") || args.Contains("--update"))
            return await RunUpdateAsync(version);

        return null;
    }

    private static async Task<int> RunUpdateAsync(string version)
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
            using var doc = JsonDocument.Parse(json);
            latestTag = doc.RootElement.GetProperty("tag_name").GetString() ?? "";
            Console.WriteLine($" {latestTag}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\nFailed to fetch release information: {ex.Message}");
            return 1;
        }

        var currentCore = version.TrimStart('v').Split('+')[0];
        var latestCore = latestTag.TrimStart('v').Split('+')[0];

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

        var rid = ResolveRuntimeId();
        var isWindows = OperatingSystem.IsWindows();
        var ext = isWindows ? "zip" : "tar.gz";
        var assetName = $"llamactrl-{rid}.{ext}";
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
                ZipFile.ExtractToDirectory(archivePath, extractDir, overwriteFiles: true);
            }
            else
            {
                var tar = Process.Start(new ProcessStartInfo("tar",
                    $"-xzf \"{archivePath}\" -C \"{extractDir}\"") { UseShellExecute = false });
                tar?.WaitForExit();
                if (tar?.ExitCode != 0) throw new Exception("Archive extraction failed.");
            }

            var binaryName = isWindows ? "llamactrl.exe" : "llamactrl";
            var newBinary = Path.Combine(extractDir, binaryName);
            if (!File.Exists(newBinary))
                throw new Exception($"Expected binary not found in archive: {binaryName}");

            var currentExe = Process.GetCurrentProcess().MainModule?.FileName
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
                Process.Start(new ProcessStartInfo("cmd", $"/c \"{bat}\"")
                    { CreateNoWindow = true, UseShellExecute = true });
            }
            else
            {
                var backup = currentExe + ".bak";
                File.Copy(currentExe, backup, overwrite: true);
                try
                {
                    File.Copy(newBinary, currentExe, overwrite: true);
                    Process.Start(new ProcessStartInfo("chmod",
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
                try { Directory.Delete(tempDir, recursive: true); } catch { }
        }

        return 0;
    }

    private static string ResolveRuntimeId()
    {
        var arch = RuntimeInformation.ProcessArchitecture;
        if (OperatingSystem.IsMacOS())
            return arch == Architecture.Arm64 ? "osx-arm64" : "osx-x64";
        if (OperatingSystem.IsWindows())
            return arch == Architecture.Arm64 ? "win-arm64" : "win-x64";
        return arch == Architecture.Arm64 ? "linux-arm64" : "linux-x64";
    }
}
