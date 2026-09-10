using ImageProcessor.Domain.Enums;

namespace ImageProcessor.Domain.Entities;

public record ProcessingJob
{
    public string JobId { get; init; } = Guid.NewGuid().ToString("N");
    public int TotalDiscovered { get; init; }
    public int ProcessedCount { get; init; }
    public int SuccessCount { get; init; }
    public int FailedCount { get; init; }
    public JobStatus Status { get; init; } = JobStatus.Idle;
    public DateTime StartedAt { get; init; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; init; }
}
