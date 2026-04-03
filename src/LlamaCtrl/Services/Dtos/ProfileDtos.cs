namespace LlamaCtrl.Services.Dtos;

public record ProfileDto(
    int Id,
    string Name,
    string ModelPath,
    string? ParametersJson,
    string? CustomArgsJson,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    int? SelectedBinaryId = null
);

public record CreateProfileDto(
    string Name,
    string ModelPath,
    string? ParametersJson = null,
    string? CustomArgsJson = null,
    int? SelectedBinaryId = null
);

public record UpdateProfileDto(
    string? Name,
    string? ModelPath,
    string? ParametersJson,
    string? CustomArgsJson,
    int? SelectedBinaryId = null
);

public record CloneProfileDto(string? Name);
