using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using LlamaCtrl.Data;
using LlamaCtrl.Domain.Entities;
using LlamaCtrl.Infrastructure;
using LlamaCtrl.Services.Dtos;
using Microsoft.EntityFrameworkCore;

namespace LlamaCtrl.Services;

public class BenchmarkService : IBenchmarkService
{
    private static readonly Regex PromptProgressRegex = new(
        @"(?<![.\d])0\.\d+(?![.\d])|(?<![.\d])1\.0+(?![.\d])",
        RegexOptions.Compiled
    );
    private readonly AppDbContext _db;
    private readonly IConfiguration _config;
    private readonly ILogger<BenchmarkService> _logger;
    private readonly BenchmarkProgressStore _store;
    private readonly IServiceScopeFactory _scopeFactory;
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(600) };

    public BenchmarkService(
        AppDbContext db, IConfiguration config, ILogger<BenchmarkService> logger,
        BenchmarkProgressStore store, IServiceScopeFactory scopeFactory)
    {
        _db = db;
        _config = config;
        _logger = logger;
        _store = store;
        _scopeFactory = scopeFactory;
    }

    public void StartBackgroundRun(RunBenchmarkDto dto)
    {
        if (_store.GetSnapshot() is { IsRunning: true })
            throw new InvalidOperationException("A benchmark is already running.");

        var ct = _store.Start(dto.NPredict);

        _ = Task.Run(async () =>
        {
            using var scope = _scopeFactory.CreateScope();
            var db     = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();

            try   { await RunWithProgressStoreAsync(dto, db, config, ct); }
            catch (OperationCanceledException)
            {
                _store.Update(p => { p.IsRunning = false; p.Phase = "idle"; });
            }
            catch (Exception ex)
            {
                _store.Update(p => { p.IsRunning = false; p.Phase = "error"; p.Error = ex.Message; });
            }
        });
    }

    private async Task RunWithProgressStoreAsync(
        RunBenchmarkDto dto, AppDbContext db, IConfiguration config, CancellationToken ct)
    {
        var profile = await db.Profiles.FindAsync([dto.ProfileId], ct)
            ?? throw new KeyNotFoundException($"Profile {dto.ProfileId} not found");

        var port   = FindFreePort();
        var binary = config["LlamaCtrl:LlamaServerBinary"] ?? "llama-server";
        var args   = BuildArgs(profile, port, dto.NPredict);

        _store.Update(p => p.Phase = "loading");

        var psi = new ProcessStartInfo
        {
            FileName = binary, Arguments = args,
            UseShellExecute = false,
            RedirectStandardOutput = true, RedirectStandardError = true,
            CreateNoWindow = true,
        };

        using var proc = new Process { StartInfo = psi };
        proc.Start();
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();

        try
        {
            var baseUrl  = $"http://127.0.0.1:{port}";
            var deadline = DateTime.UtcNow.AddSeconds(300);

            while (DateTime.UtcNow < deadline && !ct.IsCancellationRequested)
            {
                if (proc.HasExited) throw new Exception($"llama-server exited (code {proc.ExitCode})");
                try
                {
                    var r = await _http.GetAsync($"{baseUrl}/health", ct);
                    if (r.IsSuccessStatusCode && (await r.Content.ReadAsStringAsync(ct)).Contains("\"ok\"")) break;
                }
                catch { }
                await Task.Delay(500, ct);
            }
            if (DateTime.UtcNow >= deadline) throw new TimeoutException("llama-server did not become healthy within 300s");

            var vramMb = await TryGetVramMbAsync();

            _store.Update(p => p.Phase = "generating");

            var payload = new
            {
                prompt = "The quick brown fox jumps over the lazy dog. Once upon a time in a land far away,",
                n_predict = dto.NPredict,
                stream    = true,
                temperature = 0.0,
            };
            var completionReq = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/completion")
            {
                Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
            };
            using var httpResp = await _http.SendAsync(completionReq, HttpCompletionOption.ResponseHeadersRead, ct);
            httpResp.EnsureSuccessStatusCode();

            using var stream = await httpResp.Content.ReadAsStreamAsync(ct);
            using var reader = new StreamReader(stream);

            double finalGenTps = 0, finalPromptTps = 0, finalPromptMs = 0;
            int    tokenCount  = 0;
            Stopwatch? genSw   = null;
            double currentTps  = 0;
            var    chartPoints = new List<object>();

            _logger.LogInformation("Benchmark generation starting, posting to {Url}/completion", baseUrl);

            while (!ct.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(ct);
                if (line == null) break;
                string json;
                if (line.StartsWith("data: "))
                    json = line["data: ".Length..].Trim();
                else if (line.StartsWith("{"))
                    json = line.Trim();
                else
                    continue;
                if (json is "[DONE]" or "") continue;

                JsonDocument doc;
                try { doc = JsonDocument.Parse(json); }
                catch { continue; } // skip malformed lines
                using (doc)
                {
                    var root = doc.RootElement;
                    bool stop = root.TryGetProperty("stop", out var stopEl) && stopEl.GetBoolean();

                    if (!stop)
                    {
                        tokenCount++;
                        genSw ??= Stopwatch.StartNew();
                        if (tokenCount == 1) _logger.LogInformation("First token received from llama-server");
                    }
                    else if (root.TryGetProperty("tokens_predicted", out var nEl))
                        tokenCount = nEl.GetInt32();

                    var elapsed = genSw?.Elapsed.TotalSeconds ?? 0;
                    if (elapsed >= 0.1)
                        currentTps = Math.Round(tokenCount / elapsed, 1);

                    if (root.TryGetProperty("timings", out var timings))
                    {
                        if (timings.TryGetProperty("predicted_per_second", out var g))
                        {
                            var v = g.GetDouble();
                            if (v > 0) { currentTps = Math.Round(v, 1); finalGenTps = currentTps; }
                        }
                        if (timings.TryGetProperty("prompt_per_second", out var p) && p.GetDouble() > 0)
                            finalPromptTps = Math.Round(p.GetDouble(), 1);
                        if (timings.TryGetProperty("prompt_ms", out var pm) && pm.GetDouble() > 0)
                            finalPromptMs = Math.Round(pm.GetDouble(), 1);
                    }
                    if (stop && finalGenTps > 0) currentTps = finalGenTps;

                    var timeS = Math.Round(elapsed, 1);

                    if (tokenCount % 10 == 1 || stop) // token 1, 11, 21, ... and final stop
                    {
                        chartPoints.Add(new { n = tokenCount, tps = currentTps });
                    }

                    _store.Update(p =>
                    {
                        p.TokenCount = tokenCount;
                        p.GenTps     = currentTps;
                        p.PromptTps  = finalPromptTps;
                        p.PromptMs   = finalPromptMs;
                        if (tokenCount % 10 == 1 || stop)
                            p.Points.Add(new BenchmarkPoint(timeS, currentTps, tokenCount));
                    });

                    if (stop) break;
                }
            }
            _logger.LogInformation("Benchmark generation done: {N} tokens, {Tps} t/s", tokenCount, finalGenTps);

            var result = new BenchmarkResult
            {
                ProfileId            = dto.ProfileId,
                RunAt                = DateTime.UtcNow,
                GenerationSpeedTps   = finalGenTps,
                PromptSpeedTps       = finalPromptTps,
                TimeToFirstTokenMs   = finalPromptMs,
                VramUsedMb           = vramMb,
                Notes                = dto.Notes,
                BenchmarkType        = dto.BenchmarkType,
                ChartDataJson        = JsonSerializer.Serialize(chartPoints),
            };
            db.BenchmarkResults.Add(result);
            await db.SaveChangesAsync(ct);
            result.Profile = profile;

            _store.Update(p =>
            {
                p.IsRunning  = false;
                p.Phase      = "done";
                p.TokenCount = tokenCount;
                p.GenTps     = finalGenTps;
                p.PromptTps  = finalPromptTps;
                p.PromptMs   = finalPromptMs;
                p.ResultId   = result.Id;
            });
        }
        finally
        {
            try { if (!proc.HasExited) { proc.Kill(entireProcessTree: true); proc.WaitForExit(5000); } }
            catch {  }
        }
    }

    public async Task<List<BenchmarkResultDto>> GetAllAsync()
    {
        var results = await _db.BenchmarkResults
            .Include(b => b.Profile)
            .OrderByDescending(b => b.RunAt)
            .ToListAsync();

        return results.Select(MapToDto).ToList();
    }

    public async Task<BenchmarkResultDto> GetByIdAsync(int id)
    {
        var result = await _db.BenchmarkResults
            .Include(b => b.Profile)
            .FirstOrDefaultAsync(b => b.Id == id)
            ?? throw new KeyNotFoundException($"Benchmark {id} not found");

        return MapToDto(result);
    }

    public async Task<BenchmarkResultDto> RunAsync(RunBenchmarkDto dto)
    {
        var profile = await _db.Profiles.FindAsync(dto.ProfileId)
            ?? throw new KeyNotFoundException($"Profile {dto.ProfileId} not found");

        var port = FindFreePort();
        var binary = _config["LlamaCtrl:LlamaServerBinary"] ?? "llama-server";
        var args = BuildArgs(profile, port);

        _logger.LogInformation("Starting benchmark llama-server on port {Port} with args: {Args}", port, args);

        var psi = new ProcessStartInfo
        {
            FileName = binary,
            Arguments = args,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        using var proc = new Process { StartInfo = psi };
        proc.Start();
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();

        try
        {
            var baseUrl = $"http://127.0.0.1:{port}";
            var deadline = DateTime.UtcNow.AddSeconds(300);
            while (DateTime.UtcNow < deadline)
            {
                if (proc.HasExited) throw new Exception($"llama-server exited unexpectedly (code {proc.ExitCode})");
                try
                {
                    var r = await _http.GetAsync($"{baseUrl}/health");
                    if (r.IsSuccessStatusCode && (await r.Content.ReadAsStringAsync()).Contains("\"ok\"")) break;
                }
                catch { }
                await Task.Delay(500);
            }
            if (DateTime.UtcNow >= deadline) throw new TimeoutException("llama-server did not become healthy within 300s");

            var (genTps, promptTps, ttftMs) = await RunCompletionAsync(baseUrl);

            var result = new BenchmarkResult
            {
                ProfileId = dto.ProfileId,
                RunAt = DateTime.UtcNow,
                GenerationSpeedTps = genTps,
                PromptSpeedTps = promptTps,
                TimeToFirstTokenMs = ttftMs,
                VramUsedMb = 0, // llama-server doesn't expose VRAM via /completion timings
                Notes = dto.Notes,
                BenchmarkType = dto.BenchmarkType,
            };

            _db.BenchmarkResults.Add(result);
            await _db.SaveChangesAsync();

            result.Profile = profile;
            return MapToDto(result);
        }
        finally
        {
            try
            {
                if (!proc.HasExited)
                {
                    proc.Kill(entireProcessTree: true);
                    proc.WaitForExit(5000);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error stopping benchmark llama-server");
            }
        }
    }

    private static async Task<(double genTps, double promptTps, double ttftMs)> RunCompletionAsync(string baseUrl)
    {
        var payload = new
        {
            prompt = "The quick brown fox jumps over the lazy dog. Once upon a time in a land far away,",
            n_predict = 128,
            stream = false,
            temperature = 0.0,  // deterministic for benchmarking
        };

        var json = JsonSerializer.Serialize(payload);
        var completionReq = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/completion")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

        var response = await _http.SendAsync(completionReq, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        var responseJson = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(responseJson);
        var root = doc.RootElement;

        double genTps = 0, promptTps = 0, ttftMs = 0;

        if (root.TryGetProperty("timings", out var timings))
        {
            if (timings.TryGetProperty("predicted_per_second", out var genEl))
                genTps = Math.Round(genEl.GetDouble(), 2);

            if (timings.TryGetProperty("prompt_per_second", out var promptEl))
                promptTps = Math.Round(promptEl.GetDouble(), 2);

            if (timings.TryGetProperty("prompt_ms", out var ttftEl))
                ttftMs = Math.Round(ttftEl.GetDouble(), 2);
        }

        return (genTps, promptTps, ttftMs);
    }

    private static string BuildArgs(Profile profile, int port, int nPredict = 0)
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
                if (flag == "-c" && nPredict > 0)
                {
                    var baseCtx = int.TryParse(value, out var cv) ? cv : 0;
                    var effectiveCtx = Math.Max(baseCtx, nPredict + 512);
                    args.Add(flag);
                    args.Add(effectiveCtx.ToString());
                }
                else
                {
                    args.Add(flag);
                    if (!string.IsNullOrEmpty(value)) args.Add(QuoteIfNeeded(value));
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(profile.CustomArgsJson))
        {
            var customArgs = JsonSerializer.Deserialize<List<BenchmarkCustomArg>>(
                profile.CustomArgsJson) ?? new List<BenchmarkCustomArg>();
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

    private record BenchmarkCustomArg(
        [property: JsonPropertyName("flag")] string Flag,
        [property: JsonPropertyName("value")] string Value
    );

    private static async Task<double> TryGetVramMbAsync()
    {
        try
        {
            var psi = new ProcessStartInfo("nvidia-smi",
                "--query-gpu=memory.used --format=csv,noheader,nounits")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true,
            };
            using var proc = Process.Start(psi);
            if (proc != null)
            {
                var output = await proc.StandardOutput.ReadToEndAsync();
                await proc.WaitForExitAsync();
                if (proc.ExitCode == 0)
                {
                    var line = output.Trim().Split('\n')[0].Trim();
                    if (double.TryParse(line, System.Globalization.NumberStyles.Any,
                            System.Globalization.CultureInfo.InvariantCulture, out var mb))
                        return mb;
                }
            }
        }
        catch {  }

        return 0;
    }

    private static int FindFreePort()
    {
        using var listener = new TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    public async IAsyncEnumerable<BenchmarkStreamEvent> RunStreamAsync(
        RunBenchmarkDto dto,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        var profile = await _db.Profiles.FindAsync([dto.ProfileId], ct)
            ?? throw new KeyNotFoundException($"Profile {dto.ProfileId} not found");

        var port = FindFreePort();
        var binary = _config["LlamaCtrl:LlamaServerBinary"] ?? "llama-server";
        var args = BuildArgs(profile, port, dto.NPredict);

        yield return new BenchmarkStreamEvent("phase") { Phase = "starting" };

        var logQueue = new System.Collections.Concurrent.ConcurrentQueue<string>();

        var psi = new ProcessStartInfo
        {
            FileName = binary,
            Arguments = args,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        using var proc = new Process { StartInfo = psi };
        proc.OutputDataReceived += (_, e) => { if (e.Data != null) logQueue.Enqueue(e.Data); };
        proc.ErrorDataReceived  += (_, e) => { if (e.Data != null) logQueue.Enqueue(e.Data); };

        proc.Start();
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();

        try
        {
            yield return new BenchmarkStreamEvent("phase") { Phase = "loading" };

            var baseUrl = $"http://127.0.0.1:{port}";

            var deadline = DateTime.UtcNow.AddSeconds(300);
            while (DateTime.UtcNow < deadline && !ct.IsCancellationRequested)
            {
                while (logQueue.TryDequeue(out var line))
                    yield return new BenchmarkStreamEvent("log") { Log = line };

                if (proc.HasExited)
                {
                    while (logQueue.TryDequeue(out var line))
                        yield return new BenchmarkStreamEvent("log") { Log = line };
                    throw new Exception($"llama-server exited unexpectedly (code {proc.ExitCode}). Check the log lines above for details.");
                }

                try
                {
                    var response = await _http.GetAsync($"{baseUrl}/health", ct);
                    if (response.IsSuccessStatusCode)
                    {
                        var body = await response.Content.ReadAsStringAsync(ct);
                        if (body.Contains("\"ok\"")) break;
                    }
                }
                catch {  }

                await Task.Delay(500, ct);
            }

            if (DateTime.UtcNow >= deadline)
                throw new TimeoutException("llama-server did not become healthy within 300s");

            yield return new BenchmarkStreamEvent("phase") { Phase = "generating" };

            if (dto.BenchmarkType == "agentic")
            {
                await foreach (var evt in RunAgenticBenchmarkStreamAsync(dto, baseUrl, ct))
                    yield return evt;
                yield break;
            }

            var payload = new
            {
                prompt = "The quick brown fox jumps over the lazy dog. Once upon a time in a land far away,",
                n_predict = dto.NPredict,
                stream = true,
                temperature = 0.0,
            };
            var streamReq = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/completion")
            {
                Content = new StringContent(
                    System.Text.Json.JsonSerializer.Serialize(payload),
                    System.Text.Encoding.UTF8, "application/json")
            };

            var requestSw = Stopwatch.StartNew();
            using var httpResp = await _http.SendAsync(streamReq, HttpCompletionOption.ResponseHeadersRead, ct);
            httpResp.EnsureSuccessStatusCode();

            using var stream = await httpResp.Content.ReadAsStreamAsync(ct);
            using var reader = new System.IO.StreamReader(stream);

            double finalGenTps = 0, finalPromptTps = 0, finalPromptMs = 0;
            int tokenCount = 0;
            var chartPoints = new List<object>();
            System.Diagnostics.Stopwatch? genSw = null;
            double currentTps = 0;
            var lastEmitMs = 0L;
            bool firstTokenSeen = false;

            var promptProgressQueue = new System.Collections.Concurrent.ConcurrentQueue<BenchmarkStreamEvent>();
            bool generationStarted = false;

            var stderrParser = Task.Run(async () =>
            {
                while (!ct.IsCancellationRequested && !generationStarted)
                {
                    while (logQueue.TryDequeue(out var logLine))
                    {
                        var m = PromptProgressRegex.Match(logLine);
                        if (m.Success && double.TryParse(m.Value,
                                System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture,
                                out var frac))
                        {
                            var pct = Math.Min(100.0, Math.Round(frac * 100, 1));
                            promptProgressQueue.Enqueue(new BenchmarkStreamEvent("prompt_progress")
                            {
                                PromptProgress = pct
                            });
                        }
                    }
                    await Task.Delay(50, ct).ConfigureAwait(false);
                }
            }, ct);

            while (!ct.IsCancellationRequested)
            {
                while (promptProgressQueue.TryDequeue(out var ppEvent))
                    yield return ppEvent;

                var line = await reader.ReadLineAsync(ct);
                if (line == null) break; // null = end of stream
                if (!line.StartsWith("data: ")) continue;
                var json = line["data: ".Length..].Trim();
                if (json == "[DONE]") break;

                using var doc = System.Text.Json.JsonDocument.Parse(json);
                var root = doc.RootElement;

                bool stop = root.TryGetProperty("stop", out var stopEl) && stopEl.GetBoolean();

                if (!stop)
                {
                    tokenCount++;
                    genSw ??= System.Diagnostics.Stopwatch.StartNew(); // start on first token

                    if (!firstTokenSeen)
                    {
                        firstTokenSeen = true;
                        generationStarted = true; // signal background parser to stop
                        var ttftMs = Math.Round(requestSw.Elapsed.TotalMilliseconds, 1);
                        yield return new BenchmarkStreamEvent("token")
                        {
                            N        = tokenCount,
                            PromptMs = ttftMs,
                        };
                    }
                }
                else if (root.TryGetProperty("tokens_predicted", out var nEl))
                {
                    tokenCount = nEl.GetInt32(); // use accurate server count on stop
                }

                if (root.TryGetProperty("timings", out var timings))
                {
                    if (timings.TryGetProperty("predicted_per_second", out var g))
                    {
                        var v = g.GetDouble();
                        if (v > 0) finalGenTps = Math.Round(v, 1);
                    }
                    if (timings.TryGetProperty("prompt_per_second", out var p) && p.GetDouble() > 0)
                        finalPromptTps = Math.Round(p.GetDouble(), 1);
                    if (timings.TryGetProperty("prompt_ms", out var pm) && pm.GetDouble() > 0)
                        finalPromptMs = Math.Round(pm.GetDouble(), 1);
                }

                if (finalGenTps > 0)
                {
                    currentTps = finalGenTps;
                }
                else
                {
                    var elapsed = genSw?.Elapsed.TotalSeconds ?? 0;
                    if (elapsed >= 0.5)
                        currentTps = Math.Round(tokenCount / elapsed, 1);
                }

                var nowMs = genSw?.ElapsedMilliseconds ?? 0;
                chartPoints.Add(new { n = tokenCount, tps = currentTps });

                if (stop || nowMs - lastEmitMs >= 200)
                {
                    yield return new BenchmarkStreamEvent("token")
                    {
                        N     = tokenCount,
                        Tps   = currentTps > 0 ? currentTps : null,
                        TimeS = genSw != null ? Math.Round(genSw.Elapsed.TotalSeconds, 1) : null,
                    };
                    lastEmitMs = nowMs;
                }

                if (stop) break;
            }

            generationStarted = true;
            try { await stderrParser.ConfigureAwait(false); } catch (OperationCanceledException) {  }

            var result = new BenchmarkResult
            {
                ProfileId = dto.ProfileId,
                RunAt = DateTime.UtcNow,
                GenerationSpeedTps = finalGenTps,
                PromptSpeedTps = finalPromptTps,
                TimeToFirstTokenMs = finalPromptMs,
                VramUsedMb = 0,
                Notes = dto.Notes,
                BenchmarkType = dto.BenchmarkType,
                ChartDataJson = System.Text.Json.JsonSerializer.Serialize(chartPoints)
            };
            _db.BenchmarkResults.Add(result);
            await _db.SaveChangesAsync(ct);
            result.Profile = profile;

            yield return new BenchmarkStreamEvent("done")
            {
                Result    = MapToDto(result),
                PromptTps = finalPromptTps > 0 ? finalPromptTps : null,
                PromptMs  = finalPromptMs  > 0 ? finalPromptMs  : null,
            };
        }
        finally
        {
            try { if (!proc.HasExited) { proc.Kill(entireProcessTree: true); proc.WaitForExit(5000); } }
            catch {  }
        }
    }

    public async Task<List<BenchmarkResultDto>> GetCompareAsync(int[] ids)
    {
        var results = await _db.BenchmarkResults
            .Include(b => b.Profile)
            .Where(b => ids.Contains(b.Id))
            .OrderBy(b => b.RunAt)
            .ToListAsync();

        return results.Select(MapToDto).ToList();
    }

    public async Task DeleteAsync(int[] ids)
    {
        var rows = await _db.BenchmarkResults.Where(b => ids.Contains(b.Id)).ToListAsync();
        _db.BenchmarkResults.RemoveRange(rows);
        await _db.SaveChangesAsync();
    }

    public async Task<byte[]> ExportCsvAsync(int[] ids)
    {
        var results = await _db.BenchmarkResults
            .Include(b => b.Profile)
            .Where(b => ids.Contains(b.Id))
            .OrderBy(b => b.RunAt)
            .ToListAsync();

        var sb = new StringBuilder();
        sb.AppendLine("Id,ProfileId,ProfileName,RunAt,GenerationSpeedTps,PromptSpeedTps,TimeToFirstTokenMs,VramUsedMb,Notes");

        foreach (var r in results)
        {
            sb.AppendLine(string.Join(",",
                r.Id,
                r.ProfileId,
                CsvEscape(r.Profile?.Name ?? ""),
                r.RunAt.ToString("o", CultureInfo.InvariantCulture),
                r.GenerationSpeedTps.ToString(CultureInfo.InvariantCulture),
                r.PromptSpeedTps.ToString(CultureInfo.InvariantCulture),
                r.TimeToFirstTokenMs.ToString(CultureInfo.InvariantCulture),
                r.VramUsedMb.ToString(CultureInfo.InvariantCulture),
                CsvEscape(r.Notes ?? "")
            ));
        }

        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    private static string CsvEscape(string value) =>
        value.Contains(',') || value.Contains('"') || value.Contains('\n')
            ? $"\"{value.Replace("\"", "\"\"")}\""
            : value;

    private static BenchmarkResultDto MapToDto(BenchmarkResult b)
    {
        List<AgentRoundDto>? rounds = null;
        if (!string.IsNullOrEmpty(b.RoundsJson))
        {
            rounds = JsonSerializer.Deserialize<List<AgentRoundDto>>(b.RoundsJson,
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        }
        return new BenchmarkResultDto(b.Id, b.ProfileId, b.Profile?.Name ?? "", b.InstanceId,
            b.RunAt, b.GenerationSpeedTps, b.PromptSpeedTps,
            b.TimeToFirstTokenMs, b.VramUsedMb, b.Notes, b.ChartDataJson,
            b.BenchmarkType ?? "token-generation", rounds);
    }

    private static readonly string SyntheticCodeBlock = """
        public class DataProcessor {
            private readonly ILogger<DataProcessor> _logger;
            private readonly IDbConnection _connection;
            private readonly ConcurrentDictionary<string, CacheEntry> _cache = new();

            public DataProcessor(ILogger<DataProcessor> logger, IDbConnection connection) {
                _logger = logger;
                _connection = connection;
            }

            public async Task<ProcessResult> ProcessBatchAsync(IEnumerable<DataItem> items, CancellationToken ct) {
                var results = new List<ItemResult>();
                var errors = new List<string>();

                foreach (var batch in items.Chunk(100)) {
                    using var transaction = _connection.BeginTransaction();
                    try {
                        foreach (var item in batch) {
                            if (_cache.TryGetValue(item.Key, out var cached) && !cached.IsExpired) {
                                results.Add(cached.Result);
                                continue;
                            }
                            var result = await TransformItemAsync(item, ct);
                            _cache[item.Key] = new CacheEntry(result, DateTime.UtcNow.AddMinutes(5));
                            results.Add(result);
                        }
                        transaction.Commit();
                    } catch (Exception ex) {
                        transaction.Rollback();
                        _logger.LogError(ex, "Batch processing failed");
                        errors.Add(ex.Message);
                    }
                }
                return new ProcessResult(results, errors);
            }

            private async Task<ItemResult> TransformItemAsync(DataItem item, CancellationToken ct) {
                var normalized = item.Value.Trim().ToLowerInvariant();
                var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));
                var existing = await _connection.QuerySingleOrDefaultAsync<ItemResult>(
                    "SELECT * FROM Results WHERE Hash = @Hash", new { Hash = hash });
                if (existing != null) return existing;
                return new ItemResult(item.Key, normalized, hash, DateTime.UtcNow);
            }
        }
        """;

    private static string BuildInitialPrompt(int targetInputTokens)
    {
        var header = "You are a coding assistant. Analyze code and suggest improvements.\n\nUser: Analyze this code and suggest improvements:\n\n```csharp\n";
        var footer = "\n```\n\nAssistant:";
        var targetChars = targetInputTokens * 4;
        var availableChars = Math.Max(targetChars - header.Length - footer.Length, SyntheticCodeBlock.Length);
        var code = SyntheticCodeBlock;
        while (code.Length < availableChars)
            code += "\n\n" + SyntheticCodeBlock;
        if (code.Length > availableChars)
            code = code[..availableChars];
        return header + code + footer;
    }

    private static string BuildSyntheticToolResult(int round) =>
        $"Tool result: The analysis found {round * 3 + 2} issues. Here are the details: " +
        $"The code review for round {round} identified several areas for improvement including " +
        "error handling patterns that could mask exceptions, potential race conditions in the " +
        "concurrent dictionary access, missing input validation on the batch processing method, " +
        "and suboptimal query patterns that could lead to N+1 database queries. Additionally, " +
        "the cache eviction strategy relies solely on TTL without considering memory pressure, " +
        "and the transaction scope could be narrowed to reduce lock contention under high load.";

    private async IAsyncEnumerable<BenchmarkStreamEvent> RunAgenticBenchmarkStreamAsync(
        RunBenchmarkDto dto, string baseUrl,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        var currentPrompt = BuildInitialPrompt(dto.AgentInputTokens);

        var rounds = new List<AgentRoundDto>();
        var chartPoints = new List<object>();
        int totalTokenCount = 0;
        Stopwatch? totalGenSw = null;
        double currentTps = 0;
        var promptTpsValues = new List<double>();
        var promptMsValues = new List<double>();

        for (int round = 1; round <= dto.AgentRounds; round++)
        {
            if (ct.IsCancellationRequested) break;

            var approxInputTokens = currentPrompt.Length / 4;

            var payload = new
            {
                prompt = currentPrompt,
                n_predict = dto.AgentOutputTokens,
                stream = true,
                temperature = 0.0,
            };

            var req = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/completion")
            {
                Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
            };

            var requestSw = Stopwatch.StartNew();
            using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!resp.IsSuccessStatusCode)
            {
                var errBody = await resp.Content.ReadAsStringAsync(ct);
                var approxTokens = approxInputTokens + dto.AgentOutputTokens;
                if ((int)resp.StatusCode == 400)
                    throw new InvalidOperationException(
                        $"Round {round} failed: context window too small for ~{approxTokens} tokens. " +
                        $"Increase the -c (context size) parameter in your profile. Server: {errBody}");
                throw new InvalidOperationException($"Round {round} server error {(int)resp.StatusCode}: {errBody}");
            }

            using var stream = await resp.Content.ReadAsStreamAsync(ct);
            using var reader = new StreamReader(stream);

            double ttftMs = 0;
            int roundTokenCount = 0;
            var assistantContent = new StringBuilder();
            Stopwatch? genSw = null;
            double roundServerTps = 0;
            double roundPromptTps = 0;
            double roundPromptMs = 0;
            var lastEmitMs = 0L;
            bool emitTtftEvent = false;

            while (!ct.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(ct);
                if (line == null) break;

                string json;
                if (line.StartsWith("data: "))
                    json = line["data: ".Length..].Trim();
                else if (line.StartsWith("{"))
                    json = line.Trim();
                else
                    continue;
                if (json is "[DONE]" or "") continue;

                JsonDocument doc;
                try { doc = JsonDocument.Parse(json); }
                catch { continue; }
                using (doc)
                {
                    var root = doc.RootElement;

                    if (!root.TryGetProperty("stop", out var stopEl)) continue;

                    var isStop = stopEl.ValueKind == JsonValueKind.True;
                    if (!isStop)
                    {
                        roundTokenCount++;
                        genSw ??= Stopwatch.StartNew();
                        totalGenSw ??= Stopwatch.StartNew();

                        if (roundTokenCount == 1)
                        {
                            ttftMs = Math.Round(requestSw.Elapsed.TotalMilliseconds, 1);
                            emitTtftEvent = true;
                        }

                        if (root.TryGetProperty("content", out var nativeContent))
                            assistantContent.Append(nativeContent.GetString() ?? "");

                        if (root.TryGetProperty("timings", out var t1) &&
                            t1.TryGetProperty("predicted_per_second", out var tps1) && tps1.GetDouble() > 0)
                        {
                            currentTps = Math.Round(tps1.GetDouble(), 1);
                        }
                        else
                        {
                            var elapsed = genSw.Elapsed.TotalSeconds;
                            if (elapsed >= 0.5)
                                currentTps = Math.Round(roundTokenCount / elapsed, 1);
                        }

                        chartPoints.Add(new { n = totalTokenCount + roundTokenCount, tps = currentTps });

                        var nowMs = totalGenSw.ElapsedMilliseconds;
                        if (nowMs - lastEmitMs >= 200)
                        {
                            yield return new BenchmarkStreamEvent("token")
                            {
                                N = totalTokenCount + roundTokenCount,
                                Tps = currentTps > 0 ? currentTps : null,
                                TimeS = Math.Round(totalGenSw.Elapsed.TotalSeconds, 1),
                            };
                            lastEmitMs = nowMs;
                        }
                    }
                    else
                    {
                        if (root.TryGetProperty("timings", out var timings))
                        {
                            if (timings.TryGetProperty("predicted_per_second", out var tpsEl) && tpsEl.GetDouble() > 0)
                                roundServerTps = tpsEl.GetDouble();
                            if (timings.TryGetProperty("prompt_per_second", out var ptpsEl) && ptpsEl.GetDouble() > 0)
                                roundPromptTps = ptpsEl.GetDouble();
                            if (timings.TryGetProperty("prompt_ms", out var pmsEl) && pmsEl.GetDouble() > 0)
                                roundPromptMs = pmsEl.GetDouble();
                        }
                        if (root.TryGetProperty("tokens_predicted", out var tp))
                            roundTokenCount = tp.GetInt32();
                        break;
                    }
                }

                if (emitTtftEvent)
                {
                    emitTtftEvent = false;
                    yield return new BenchmarkStreamEvent("token")
                    {
                        N        = totalTokenCount + roundTokenCount,
                        PromptMs = ttftMs,
                    };
                }
            }

            var elapsedGenS = genSw?.Elapsed.TotalSeconds ?? 1.0;
            var roundSpeedTps = roundServerTps > 0
                ? Math.Round(roundServerTps, 1)
                : elapsedGenS > 0.01 ? Math.Round(roundTokenCount / elapsedGenS, 1) : 0;

            totalTokenCount += roundTokenCount;

            if (totalGenSw != null)
            {
                yield return new BenchmarkStreamEvent("token")
                {
                    N = totalTokenCount,
                    Tps = currentTps > 0 ? currentTps : null,
                    TimeS = Math.Round(totalGenSw.Elapsed.TotalSeconds, 1),
                };
            }

            if (roundPromptTps > 0) promptTpsValues.Add(roundPromptTps);
            if (roundPromptMs > 0) promptMsValues.Add(roundPromptMs);

            var roundDto = new AgentRoundDto(round, approxInputTokens, roundTokenCount, ttftMs, roundSpeedTps);
            rounds.Add(roundDto);

            yield return new BenchmarkStreamEvent("round")
            {
                Round        = round,
                TotalRounds  = dto.AgentRounds,
                InputTokens  = approxInputTokens,
                OutputTokens = roundTokenCount,
                RoundTtftMs  = ttftMs,
                RoundSpeedTps= roundSpeedTps,
            };

            currentPrompt += assistantContent.ToString() + $"\n\nUser: {BuildSyntheticToolResult(round)}\n\nAssistant:";
        }

        var avgTps = rounds.Count > 0 ? Math.Round(rounds.Average(r => r.SpeedTps), 1) : 0;
        var avgTtft = rounds.Count > 0 ? Math.Round(rounds.Average(r => r.TtftMs), 1) : 0;
        var avgPromptTps = promptTpsValues.Count > 0 ? Math.Round(promptTpsValues.Average(), 1) : 0;
        var avgPromptMs  = promptMsValues.Count  > 0 ? Math.Round(promptMsValues.Average(),  1) : 0;

        var result = new BenchmarkResult
        {
            ProfileId = dto.ProfileId,
            RunAt = DateTime.UtcNow,
            GenerationSpeedTps = avgTps,
            PromptSpeedTps = avgPromptTps,
            TimeToFirstTokenMs = avgTtft,
            VramUsedMb = 0,
            Notes = dto.Notes,
            BenchmarkType = "agentic",
            ChartDataJson = JsonSerializer.Serialize(chartPoints),
            RoundsJson = JsonSerializer.Serialize(rounds,
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }),
        };
        _db.BenchmarkResults.Add(result);
        await _db.SaveChangesAsync(ct);

        var profile = await _db.Profiles.FindAsync([dto.ProfileId], ct);
        result.Profile = profile!;

        yield return new BenchmarkStreamEvent("done")
        {
            Result    = MapToDto(result),
            PromptTps = avgPromptTps > 0 ? avgPromptTps : null,
            PromptMs  = avgPromptMs  > 0 ? avgPromptMs  : avgTtft > 0 ? avgTtft : null,
        };
    }
}
