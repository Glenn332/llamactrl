using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using LlamaCtrl.Services.Dtos;

namespace LlamaCtrl.Infrastructure.SystemInfo;

public class SystemInfoService
{
    private sealed record Snapshot(double Cpu, double RamUsed, double RamTotal);
    private volatile Snapshot? _latest;

    public SystemStatusDto GetCachedStatus(int activeInstances)
    {
        var v = _latest;
        return v is not null
            ? new SystemStatusDto(v.Cpu, v.RamUsed, v.RamTotal, 0, 0, activeInstances)
            : new SystemStatusDto(0, 0, 0, 0, 0, activeInstances);
    }

    public async Task RefreshAsync()
    {
        try
        {
            var (cpu, ramUsed, ramTotal) = await GetCpuAndRamAsync();
            _latest = new Snapshot(cpu, ramUsed, ramTotal);
        }
        catch {  }
    }

    public List<GpuInfoDto> GetGpus()
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                return GetGpusMac();
        }
        catch { }
        return [];
    }

    private static async Task<(double cpu, double ramUsed, double ramTotal)> GetCpuAndRamAsync()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return await GetCpuAndRamMacAsync();
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return GetCpuAndRamLinux();
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return GetCpuAndRamWindows();
        return (0, 0, 0);
    }

    private static async Task<(double cpu, double ramUsed, double ramTotal)> GetCpuAndRamMacAsync()
    {
        var topTask = RunAsync("top", "-l 1 -n 0");
        var memSizeTask = RunAsync("sysctl", "-n hw.memsize");
        var vmStatTask = RunAsync("vm_stat", "");
        await Task.WhenAll(topTask, memSizeTask, vmStatTask);

        var topOut = await topTask;
        var memSizeOut = await memSizeTask;
        var vmOut = await vmStatTask;

        var idleMatch = Regex.Match(topOut, @"CPU usage:.*?([\d.]+)%\s*idle",
            RegexOptions.IgnoreCase);
        double cpu = idleMatch.Success
            ? Math.Round(100.0 - double.Parse(idleMatch.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture), 1)
            : 0;

        double totalBytes = double.TryParse(memSizeOut.Trim(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var tb) ? tb : 0;
        double totalGb = totalBytes / 1024 / 1024 / 1024;

        double pageSize = 16384;
        var pageSizeMatch = Regex.Match(vmOut, @"page size of (\d+) bytes");
        if (pageSizeMatch.Success) pageSize = double.Parse(pageSizeMatch.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);

        double GetPages(string key)
        {
            var m = Regex.Match(vmOut, key + @"[^0-9]*(\d+)");
            return m.Success ? double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) : 0;
        }

        var active   = GetPages("Pages active");
        var wired    = GetPages("Pages wired down");
        var occupied = GetPages("Pages occupied by compressor");
        var usedGb   = (active + wired + occupied) * pageSize / 1024 / 1024 / 1024;

        return (cpu, Math.Min(usedGb, totalGb), totalGb);
    }

    private static (double cpu, double ramUsed, double ramTotal) GetCpuAndRamLinux()
    {
        static long[] ReadCpuStat()
        {
            var line = File.ReadLines("/proc/stat").First(l => l.StartsWith("cpu "));
            return line.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                       .Skip(1).Take(7).Select(long.Parse).ToArray();
        }

        var s1 = ReadCpuStat();
        Thread.Sleep(200);
        var s2 = ReadCpuStat();

        var idle1  = s1[3] + s1[4];
        var total1 = s1.Sum();
        var idle2  = s2[3] + s2[4];
        var total2 = s2.Sum();
        var cpu = total2 == total1 ? 0 : (1.0 - (double)(idle2 - idle1) / (total2 - total1)) * 100;

        var memLines = File.ReadAllLines("/proc/meminfo")
            .Select(l => l.Split(':'))
            .Where(p => p.Length == 2)
            .ToDictionary(p => p[0].Trim(), p => double.Parse(Regex.Match(p[1], @"\d+").Value));

        var totalGb  = memLines.GetValueOrDefault("MemTotal")  / 1024 / 1024;
        var freeGb   = memLines.GetValueOrDefault("MemAvailable") / 1024 / 1024;

        return (cpu, totalGb - freeGb, totalGb);
    }

    private static (double cpu, double ramUsed, double ramTotal) GetCpuAndRamWindows()
    {
        var cpuOut = Run("wmic", "cpu get LoadPercentage /value");
        var cpuMatch = Regex.Match(cpuOut, @"LoadPercentage=(\d+)");
        double cpu = cpuMatch.Success ? double.Parse(cpuMatch.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) : 0;

        var memOut = Run("wmic", "OS get FreePhysicalMemory,TotalVisibleMemorySize /value");
        double total = 0, free = 0;
        var t = Regex.Match(memOut, @"TotalVisibleMemorySize=(\d+)");
        var f = Regex.Match(memOut, @"FreePhysicalMemory=(\d+)");
        if (t.Success) total = double.Parse(t.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) / 1024 / 1024;
        if (f.Success) free  = double.Parse(f.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) / 1024 / 1024;

        return (cpu, total - free, total);
    }

    private static List<GpuInfoDto> GetGpusMac()
    {

        var json = Run("system_profiler", "SPDisplaysDataType -json", timeoutMs: 5000);
        var nameMatches  = Regex.Matches(json, @"""spdisplays_device-id""[^,]*,\s*""_name""\s*:\s*""([^""]+)""");
        var vramMatches  = Regex.Matches(json, @"""spdisplays_vram""\s*:\s*""([^""]+)""");

        var results = new List<GpuInfoDto>();
        for (int i = 0; i < nameMatches.Count; i++)
        {
            var name = nameMatches[i].Groups[1].Value;
            double vramGb = 0;
            if (i < vramMatches.Count)
            {
                var vramStr = vramMatches[i].Groups[1].Value;
                var vramNum = Regex.Match(vramStr, @"[\d.]+");
                if (vramNum.Success)
                {
                    vramGb = double.Parse(vramNum.Value, System.Globalization.CultureInfo.InvariantCulture);
                    if (vramStr.Contains("MB")) vramGb /= 1024;
                }
            }
            results.Add(new GpuInfoDto(name, vramGb, 0));
        }
        return results;
    }

    private static string Run(string cmd, string args, int timeoutMs = 4000)
    {
        var psi = new ProcessStartInfo
        {
            FileName = cmd,
            Arguments = args,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        using var proc = Process.Start(psi) ?? throw new Exception($"Failed to start {cmd}");
        var output = proc.StandardOutput.ReadToEnd();
        proc.WaitForExit(timeoutMs);
        return output;
    }

    private static async Task<string> RunAsync(string cmd, string args, int timeoutMs = 4000)
    {
        var psi = new ProcessStartInfo
        {
            FileName = cmd,
            Arguments = args,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        using var proc = Process.Start(psi) ?? throw new Exception($"Failed to start {cmd}");
        var output = await proc.StandardOutput.ReadToEndAsync();
        await proc.WaitForExitAsync();
        return output;
    }
}
