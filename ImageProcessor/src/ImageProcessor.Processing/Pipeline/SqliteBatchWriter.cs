using System.Threading.Channels;
using ImageProcessor.Application.Interfaces;
using ImageProcessor.Domain.Entities;
using ImageProcessor.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace ImageProcessor.Processing.Pipeline;

public class SqliteBatchWriter
{
    private readonly IImageRepository _imageRepository;
    private readonly IJobRepository _jobRepository;
    private readonly int _batchSize;
    private readonly ILogger<SqliteBatchWriter> _logger;

    public SqliteBatchWriter(
        IImageRepository imageRepository,
        IJobRepository jobRepository,
        int batchSize,
        ILogger<SqliteBatchWriter> logger)
    {
        _imageRepository = imageRepository;
        _jobRepository = jobRepository;
        _batchSize = Math.Max(1, batchSize);
        _logger = logger;
    }

    public async Task WriteResultsAsync(
        ChannelReader<ProcessedImage> reader,
        string jobId,
        Func<int> getTotalDiscovered,
        Action<int, int, int> onProgressUpdated,
        CancellationToken cancellationToken)
    {
        var buffer = new List<ProcessedImage>(_batchSize);
        int totalProcessed = 0;
        int totalSuccess = 0;
        int totalFailed = 0;

        await foreach (var item in reader.ReadAllAsync(cancellationToken))
        {
            buffer.Add(item);
            totalProcessed++;
            if (item.Status == ProcessingStatus.Success) totalSuccess++;
            else totalFailed++;

            if (buffer.Count >= _batchSize)
            {
                await FlushBufferAsync(buffer, jobId, getTotalDiscovered(), totalProcessed, totalSuccess, totalFailed, cancellationToken);
                onProgressUpdated(totalProcessed, totalSuccess, totalFailed);
            }
        }

        if (buffer.Count > 0)
        {
            await FlushBufferAsync(buffer, jobId, getTotalDiscovered(), totalProcessed, totalSuccess, totalFailed, cancellationToken);
            onProgressUpdated(totalProcessed, totalSuccess, totalFailed);
        }
    }

    private async Task FlushBufferAsync(
        List<ProcessedImage> buffer,
        string jobId,
        int totalDiscovered,
        int processedCount,
        int successCount,
        int failedCount,
        CancellationToken cancellationToken)
    {
        try
        {
            await _imageRepository.AddBatchAsync(buffer, cancellationToken);
            await _jobRepository.UpdateJobProgressAsync(jobId, totalDiscovered, processedCount, successCount, failedCount, cancellationToken);
            _logger.LogDebug("Persisted batch of {Count} images to SQLite. Total Processed: {TotalProcessed}", buffer.Count, processedCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist batch of {Count} images to SQLite.", buffer.Count);
            throw;
        }
        finally
        {
            buffer.Clear();
        }
    }
}
