namespace LlamaCtrl.Services.Dtos;

public record LlamaServerBinaryDto(int Id, string Name, string Path, bool IsDefault, DateTime CreatedAt, DateTime UpdatedAt);
public record CreateBinaryDto(string Name, string Path, bool IsDefault = false);
public record UpdateBinaryDto(string? Name, string? Path, bool? IsDefault);
