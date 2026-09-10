using System.Diagnostics;
using System.Threading.Channels;
using ImageProcessor.Application.Interfaces;
using ImageProcessor.Application.Options;
using ImageProcessor.Domain.Entities;
using ImageProcessor.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace ImageProcessor.Processing.Pipeline;

public class BoundedProcessingPipeline
{
    private readonly FileDiscoveryProducer _producer;
    private readonly Func<ImageWorker> _workerFactory;
    private readonly SqliteBatchWriter _batchWriter;
    private readonly IJobRepository _jobRepository;
    private readonly ProcessorOptions _options;
    private readonly ILogger<BoundedProcessingPipeline> _logger;

    public BoundedProcessingPipeline(
        FileDiscoveryProducer producer,
        Func<ImageWorker> workerFactory,
        SqliteBatchWriter batchWriter,
        IJobRepository jobRepository,
        ProcessorOptions options,
        ILogger<BoundedProcessingPipeline> logger)
    {
        _producer = producer;
        _workerFactory = workerFactory;
        _batchWriter = batchWriter;
        _jobRepository = jobRepository;
        _options = options;
        _logger = logger;
    }

    public async Task<ProcessingJob> RunPipelineAsync(int? customWorkerCount = null, CancellationToken cancellationToken = default)
    {
        int workerCount = customWorkerCount.HasValue && customWorkerCount.Value > 0
            ? customWorkerCount.Value
            : _options.WorkerCount;

        int queueCapacity = Math.Max(10, _options.QueueCapacity);

        string jobId = Guid.NewGuid().ToString("N");
        var initialJob = new ProcessingJob
        {
            JobId = jobId,
            TotalDiscovered = 0,
            ProcessedCount = 0,
            SuccessCount = 0,
            FailedCount = 0,
            Status = JobStatus.Running,
            StartedAt = DateTime.UtcNow
        };

        await _jobRepository.CreateJobAsync(initialJob, cancellationToken);

        _logger.LogInformation("Job {JobId} started. Input: '{Input}', Workers: {Workers}, QueueCapacity: {QueueCapacity}",
            jobId, _options.InputDirectory, workerCount, queueCapacity);

        Console.WriteLine($"[Job {jobId}] Started processing directory '{_options.InputDirectory}' with {workerCount} workers...");

        var fileChannelOptions = new BoundedChannelOptions(queueCapacity)
        {
            SingleWriter = true,
            SingleReader = false,
            FullMode = BoundedChannelFullMode.Wait
        };
        var fileChannel = Channel.CreateBounded<string>(fileChannelOptions);

        var resultChannelOptions = new BoundedChannelOptions(queueCapacity)
        {
            SingleWriter = false,
            SingleReader = true,
            FullMode = BoundedChannelFullMode.Wait
        };
        var resultChannel = Channel.CreateBounded<ProcessedImage>(resultChannelOptions);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        int totalDiscovered = 0;
        int currentProcessed = 0;
        int currentSuccess = 0;
        int currentFailed = 0;

        var stopwatch = Stopwatch.StartNew();

        // 1. Start Producer
        var producerTask = Task.Run(async () =>
        {
            totalDiscovered = await _producer.ProduceFilePathsAsync(_options.InputDirectory, fileChannel.Writer, cts.Token);
        }, cts.Token);

        // 2. Start Workers
        var workerTasks = new List<Task>();
        for (int i = 0; i < workerCount; i++)
        {
            workerTasks.Add(Task.Run(async () =>
            {
                var worker = _workerFactory();
                await worker.ProcessWorkAsync(fileChannel.Reader, resultChannel.Writer, cts.Token);
            }, cts.Token));
        }

        // When all workers finish, complete result channel
        var workersCompletionTask = Task.WhenAll(workerTasks).ContinueWith(_ =>
        {
            resultChannel.Writer.Complete();
        }, TaskScheduler.Default);

        // 3. Start DB Batch Writer
        var writerTask = Task.Run(async () =>
        {
            await _batchWriter.WriteResultsAsync(
                resultChannel.Reader,
                jobId,
                () => totalDiscovered,
                (processed, success, failed) =>
                {
                    Interlocked.Exchange(ref currentProcessed, processed);
                    Interlocked.Exchange(ref currentSuccess, success);
                    Interlocked.Exchange(ref currentFailed, failed);

                    Console.Write($"\r[Progress] Discovered: {totalDiscovered} | Processed: {processed} | Success: {success} | Failed: {failed}    ");
                },
                cts.Token);
        }, cts.Token);

        // 4. Start Cancellation & Status Monitor
        var cancelMonitorTask = Task.Run(async () =>
        {
            while (!cts.Token.IsCancellationRequested && !writerTask.IsCompleted)
            {
                await Task.Delay(500, cts.Token).ConfigureAwait(false);
                if (await _jobRepository.IsCancellationRequestedAsync(cts.Token))
                {
                    _logger.LogWarning("Cancellation request detected in database for Job {JobId}.", jobId);
                    cts.Cancel();
                    break;
                }
            }
        }, cts.Token);

        JobStatus finalStatus = JobStatus.Completed;
        try
        {
            await Task.WhenAll(producerTask, workersCompletionTask, writerTask);
        }
        catch (OperationCanceledException)
        {
            finalStatus = JobStatus.Cancelled;
            _logger.LogWarning("Job {JobId} was cancelled by user.", jobId);
            Console.WriteLine($"\n[Job {jobId}] Cancellation completed.");
        }
        catch (Exception ex)
        {
            finalStatus = JobStatus.Failed;
            _logger.LogError(ex, "Job {JobId} failed with error.", jobId);
            Console.WriteLine($"\n[Job {jobId}] Failed with error: {ex.Message}");
        }
        finally
        {
            stopwatch.Stop();
            await _jobRepository.SetJobStatusAsync(jobId, finalStatus, CancellationToken.None);
            await _jobRepository.UpdateJobProgressAsync(jobId, totalDiscovered, currentProcessed, currentSuccess, currentFailed, CancellationToken.None);
        }

        Console.WriteLine($"\n[Job {jobId}] Finished with status: {finalStatus}. Total: {totalDiscovered}, Success: {currentSuccess}, Failed: {currentFailed}, Time: {stopwatch.Elapsed.TotalSeconds:F2}s");

        return new ProcessingJob
        {
            JobId = jobId,
            TotalDiscovered = totalDiscovered,
            ProcessedCount = currentProcessed,
            SuccessCount = currentSuccess,
            FailedCount = currentFailed,
            Status = finalStatus,
            StartedAt = initialJob.StartedAt,
            CompletedAt = DateTime.UtcNow
        };
    }
}
