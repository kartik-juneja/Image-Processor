using ImageProcessor.Application.Options;
using ImageProcessor.Domain.Enums;
using ImageProcessor.Infrastructure.ImageProcessing;
using ImageProcessor.Infrastructure.Persistence;
using ImageProcessor.Processing.Pipeline;
using Microsoft.Extensions.Logging.Abstractions;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace ImageProcessor.Tests;

public class ConcurrencyPipelineTests : IDisposable
{
    private readonly string _tempWorkDir;
    private readonly string _imagesDir;
    private readonly string _thumbsDir;
    private readonly string _dbPath;

    public ConcurrencyPipelineTests()
    {
        _tempWorkDir = Path.Combine(Path.GetTempPath(), $"test_pipeline_{Guid.NewGuid():N}");
        _imagesDir = Path.Combine(_tempWorkDir, "images");
        _thumbsDir = Path.Combine(_tempWorkDir, "thumbs");
        _dbPath = Path.Combine(_tempWorkDir, "pipeline.db");

        Directory.CreateDirectory(_imagesDir);
        Directory.CreateDirectory(_thumbsDir);

        var dbInit = new DatabaseInitializer(_dbPath);
        dbInit.InitializeAsync().GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempWorkDir))
        {
            try { Directory.Delete(_tempWorkDir, true); } catch { }
        }
    }

    [Fact]
    public async Task RunPipelineAsync_MultipleImagesWithMultipleWorkers_ProcessesAllSuccessfully()
    {
        // Arrange: Generate 15 sample images in images directory
        for (int i = 1; i <= 15; i++)
        {
            string imgPath = Path.Combine(_imagesDir, $"test_img_{i}.png");
            using var img = new Image<Rgba32>(100 + i * 10, 100 + i * 10);
            await img.SaveAsPngAsync(imgPath);
        }

        var hashCalc = new Sha256HashCalculator();
        var metaExtractor = new ImageSharpMetadataExtractor();
        var thumbGen = new ImageSharpThumbnailGenerator();
        var options = new ProcessorOptions
        {
            WorkerCount = 4,
            QueueCapacity = 20,
            BatchSize = 5,
            InputDirectory = _imagesDir,
            ThumbnailDirectory = _thumbsDir,
            DatabasePath = _dbPath
        };

        var imageRepo = new SqliteImageRepository(_dbPath);
        var jobRepo = new SqliteJobRepository(_dbPath);

        var producer = new FileDiscoveryProducer(NullLogger<FileDiscoveryProducer>.Instance);
        var workerFactory = new Func<ImageWorker>(() => new ImageWorker(
            hashCalc, metaExtractor, thumbGen, options, NullLogger<ImageWorker>.Instance));

        var batchWriter = new SqliteBatchWriter(imageRepo, jobRepo, options.BatchSize, NullLogger<SqliteBatchWriter>.Instance);
        var pipeline = new BoundedProcessingPipeline(producer, workerFactory, batchWriter, jobRepo, options, NullLogger<BoundedProcessingPipeline>.Instance);

        // Act
        var job = await pipeline.RunPipelineAsync(customWorkerCount: 4, CancellationToken.None);

        // Assert
        Assert.Equal(JobStatus.Completed, job.Status);
        Assert.Equal(15, job.TotalDiscovered);
        Assert.Equal(15, job.SuccessCount);
        Assert.Equal(0, job.FailedCount);

        int countInDb = await imageRepo.GetTotalCountAsync();
        Assert.Equal(15, countInDb);
    }
}
