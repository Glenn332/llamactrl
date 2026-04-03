using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using LlamaCtrl.Domain.Entities;
using LlamaCtrl.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace LlamaCtrl.Infrastructure;

public class ProcessManager
{
    private readonly ConcurrentDictionary<int, ManagedProcess> _processes = new();
    private readonly ILogger<ProcessManager> _logger;
    private IHubContext<LogHub>? _logHub;
    private readonly MetricsCollector _metricsCollector;

    public ProcessManager(ILogger<ProcessManager> logger, MetricsCollector metricsCollector)
    {
        _logger = logger;
        _metricsCollector = metricsCollector;
    }

    public void SetLogHub(IHubContext<LogHub> logHub) => _logHub = logHub;

    public string BuildArgs(Profile profile, int port)
    {
        var args = new List<string>
        {
            "-m", $"\"{profile.ModelPath}\"",
            "--port", port.ToString()
        };

        if (!string.IsNullOrWhiteSpace(profile.ParametersJson))
        {
            var parameters = JsonSerializer.Deserialize<Dictionary<string, string>>(
                profile.ParametersJson) ?? new Dictionary<string, string>();
            foreach (var (flag, value) in parameters)
            {
                args.Add(flag);
                if (!string.IsNullOrEmpty(value))
                    args.Add(QuoteIfNeeded(value));
            }
        }

        if (!string.IsNullOrWhiteSpace(profile.CustomArgsJson))
        {
            var customArgs = JsonSerializer.Deserialize<List<CustomArg>>(
                profile.CustomArgsJson) ?? new List<CustomArg>();
            foreach (var arg in customArgs)
            {
                if (!string.IsNullOrWhiteSpace(arg.Flag))
                {
                    args.Add(arg.Flag);
                    if (!string.IsNullOrEmpty(arg.Value))
                        args.Add(QuoteIfNeeded(arg.Value));
                }
            }
        }

        return string.Join(" ", args);
    }

    private static string QuoteIfNeeded(string value)
    {
        if (value.Contains(' ') || value.Contains('"'))
            return $"\"{value.Replace("\"", "\\\"")}\"";
        return value;
    }

    private record CustomArg(
        [property: JsonPropertyName("flag")] string Flag,
        [property: JsonPropertyName("value")] string Value
    );

    public Task<int> StartProcessAsync(int instanceId, string binary, string args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = binary,
            Arguments = args,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        var managed = new ManagedProcess { InstanceId = instanceId, Process = process };

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data == null) return;
            managed.AddLine(e.Data);
            _metricsCollector.UpdateFromLogLine(instanceId, e.Data);
            if (e.Data.Contains("POST /completion") || e.Data.Contains("POST /v1/chat"))
                _metricsCollector.IncrementRequests(instanceId);
            _ = BroadcastLogAsync(instanceId, e.Data);
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data == null) return;
            managed.AddLine(e.Data);
            _metricsCollector.UpdateFromLogLine(instanceId, e.Data);
            _ = BroadcastLogAsync(instanceId, e.Data);
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        _processes[instanceId] = managed;
        _logger.LogInformation("Started llama-server for instance {Id} PID {Pid}", instanceId, process.Id);

        return Task.FromResult(process.Id);
    }

    public Task StopProcessAsync(int instanceId)
    {
        if (!_processes.TryRemove(instanceId, out var managed)) return Task.CompletedTask;

        try
        {
            if (!managed.Process.HasExited)
            {
                managed.Process.Kill(entireProcessTree: true);
                managed.Process.WaitForExit(5000);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error stopping process for instance {Id}", instanceId);
        }
        finally
        {
            managed.Process.Dispose();
            _metricsCollector.RemoveInstance(instanceId);
        }
        return Task.CompletedTask;
    }

    public ManagedProcess? GetProcess(int instanceId)
        => _processes.TryGetValue(instanceId, out var mp) ? mp : null;

    public bool IsRunning(int instanceId)
        => _processes.TryGetValue(instanceId, out var mp) && !mp.Process.HasExited;

    public IEnumerable<(int instanceId, int port)> GetRunningInstances()
        => _processes.Select(kv => (kv.Key, kv.Value.Port));

    public async Task StopAllAsync()
    {
        var ids = _processes.Keys.ToList();
        foreach (var id in ids)
            await StopProcessAsync(id);
    }

    private async Task BroadcastLogAsync(int instanceId, string line)
    {
        if (_logHub != null)
            await _logHub.Clients.Group(instanceId.ToString())
                .SendAsync("ReceiveLog", instanceId, line);
    }
}
