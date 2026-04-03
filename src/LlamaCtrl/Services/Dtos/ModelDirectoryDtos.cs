namespace LlamaCtrl.Services.Dtos;

public record ModelDirectoryDto(int Id, string Name, string Path, DateTime CreatedAt, DateTime UpdatedAt);
public record CreateModelDirectoryDto(string Name, string Path);
public record UpdateModelDirectoryDto(string? Name, string? Path);
