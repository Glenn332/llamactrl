namespace LlamaCtrl.Services.Dtos;

public record HfSearchResultDto(
    string ModelId,
    string Author,
    string ModelName,
    long Downloads,
    long Likes,
    List<string> Tags,
    string? Description,
    DateTime? LastModified
);

public record HfFileInfoDto(
    string Filename,
    long? SizeBytes,
    string DownloadUrl
);

public record HfModelInfoDto(
    string ModelId,
    string Author,
    string ModelName,
    long Downloads,
    long Likes,
    List<string> Tags,
    string? Description,
    DateTime? LastModified,
    List<HfFileInfoDto> Files
);

public record HfMetaDto(
    string ModelId,
    string Filename,
    string? Author,
    string? Description,
    List<string>? Tags,
    DateTime DownloadedAt,
    long? SizeBytes,
    long? HfDownloads,
    long? HfLikes
);

public record LocalModelDto(
    string Filename,
    string FullPath,
    long SizeBytes,
    DateTime ModifiedAt,
    HfMetaDto? HfMeta = null,
    string? DirectoryName = null
);

public record StartDownloadRequestDto(
    string ModelId,
    string Filename,
    long? KnownSizeBytes = null,
    string? Author = null,
    string? Description = null,
    List<string>? Tags = null,
    long? HfDownloads = null,
    long? HfLikes = null,
    string? TargetDirectory = null
);

public record DownloadProgressDto(
    string DownloadId,
    string Filename,
    string Phase,
    long BytesReceived,
    long? TotalBytes,
    double PercentComplete,
    string? Error,
    bool IsComplete
);
