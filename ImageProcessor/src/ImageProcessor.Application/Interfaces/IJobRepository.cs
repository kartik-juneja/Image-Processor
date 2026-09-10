using ImageProcessor.Domain.Entities;
using ImageProcessor.Domain.Enums;

namespace ImageProcessor.Application.Interfaces;

public interface IJobRepository
{
    Task<ProcessingJob?> GetLatestJobAsync(CancellationToken cancellationToken = default);
    Task CreateJobAsync(ProcessingJob job, CancellationToken cancellationToken = default);
    Task UpdateJobProgressAsync(string jobId, int totalDiscovered, int processedCount, int successCount, int failedCount, CancellationToken cancellationToken = default);
    Task SetJobStatusAsync(string jobId, JobStatus status, CancellationToken cancellationToken = default);
    Task RequestCancellationAsync(CancellationToken cancellationToken = default);
    Task<bool> IsCancellationRequestedAsync(CancellationToken cancellationToken = default);
}
