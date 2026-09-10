using ImageProcessor.Domain.Enums;

namespace ImageProcessor.Domain.Models;

public record ImageSearchFilter
{
    public string? Name { get; init; }
    public string? Format { get; init; }
    public ProcessingStatus? Status { get; init; }
}
