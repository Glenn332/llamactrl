using System.Runtime.InteropServices;

namespace LlamaCtrl.Startup;

sealed record CliOptions(
    int Port,
    string DataDir,
    string ModelsDir,
    string? Binary,
    string LogLevel,
    bool NoBrowser)
{
    internal static CliOptions Parse(string[] args)
    {
        var port = int.TryParse(GetArg(args, "--port"), out var p) ? p : 3131;
        var dataDir = GetArg(args, "--data-dir") ?? (
            RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "llamactrl")
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "llamactrl")
        );
        var modelsDir = GetArg(args, "--models-dir");
        var binary = GetArg(args, "--binary");
        var logLevel = GetArg(args, "--log-level") ?? "Information";
        var noBrowser = args.Contains("--no-browser");

        dataDir = ExpandPath(dataDir);
        modelsDir = modelsDir != null
            ? ExpandPath(modelsDir)
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "models");

        Directory.CreateDirectory(dataDir);

        return new CliOptions(port, dataDir, modelsDir, binary, logLevel, noBrowser);
    }

    private static string? GetArg(string[] args, string key)
    {
        var idx = Array.IndexOf(args, key);
        return idx >= 0 && idx + 1 < args.Length ? args[idx + 1] : null;
    }

    private static string ExpandPath(string path)
    {
        if (path.StartsWith("~/") || path == "~")
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return path.Replace("~", home);
        }
        return path;
    }
}
