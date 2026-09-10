using ImageProcessor.Domain.Enums;

namespace ImageProcessor.Domain.Entities;

public record ProcessedImage
{
    public long Id { get; init; }
    public string FileName { get; init; } = string.Empty;
    public string FilePath { get; init; } = string.Empty;
    public long FileSize { get; init; }
    public int? Width { get; init; }
    public int? Height { get; init; }
    public string? Format { get; init; }
    public string? SHA256 { get; init; }
    public string? ThumbnailPath { get; init; }
    public ProcessingStatus Status { get; init; } = ProcessingStatus.Success;
    public string? ErrorMessage { get; init; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime ProcessedAt { get; init; } = DateTime.UtcNow;
}
