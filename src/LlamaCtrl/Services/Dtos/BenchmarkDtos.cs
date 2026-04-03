namespace LlamaCtrl.Services.Dtos;

public record BenchmarkResultDto(
    int Id, int ProfileId, string ProfileName, int? InstanceId,
    DateTime RunAt, double GenerationSpeedTps, double PromptSpeedTps,
    double TimeToFirstTokenMs, double VramUsedMb, string? Notes,
    string? ChartDataJson,
    string BenchmarkType = "token-generation",
    List<AgentRoundDto>? Rounds = null
);

public record RunBenchmarkDto(int ProfileId, string? Notes = null, int NPredict = 2000)
{
    public string BenchmarkType { get; set; } = "token-generation";
    public int AgentRounds { get; set; } = 4;
    public int AgentOutputTokens { get; set; } = 512;
    public int AgentInputTokens { get; set; } = 512;
}

public record AgentRoundDto(int Round, int InputTokens, int OutputTokens, double TtftMs, double SpeedTps);

public record BenchmarkStreamEvent(string Type)
{
    public string? Phase    { get; init; }
    public int?    N        { get; init; }
    public double? Tps      { get; init; }
    public double? TimeS    { get; init; }
    public double? PromptTps { get; init; }
    public double? PromptMs  { get; init; }
    public string? Log      { get; init; }
    public BenchmarkResultDto? Result { get; init; }
    public string? Error    { get; init; }
    public double? PromptProgress { get; init; }
    public int?    Round        { get; init; }
    public int?    TotalRounds  { get; init; }
    public int?    InputTokens  { get; init; }
    public int?    OutputTokens { get; init; }
    public double? RoundTtftMs  { get; init; }
    public double? RoundSpeedTps{ get; init; }
}
