using ImageProcessor.Application.Services;
using ImageProcessor.Domain.Entities;
using ImageProcessor.Domain.Enums;
using ImageProcessor.Infrastructure.ImageProcessing;
using ImageProcessor.Infrastructure.Persistence;
using Xunit;

namespace ImageProcessor.Tests;

public class DuplicateDetectionTests : IDisposable
{
    private readonly string _tempDbPath;
    private readonly DatabaseInitializer _dbInit;
    private readonly SqliteImageRepository _repository;
    private readonly SqliteJobRepository _jobRepository;
    private readonly Sha256HashCalculator _hashCalculator;

    public DuplicateDetectionTests()
    {
        _tempDbPath = Path.Combine(Path.GetTempPath(), $"test_dup_{Guid.NewGuid():N}.db");
        _dbInit = new DatabaseInitializer(_tempDbPath);
        _dbInit.InitializeAsync().GetAwaiter().GetResult();
        _repository = new SqliteImageRepository(_tempDbPath);
        _jobRepository = new SqliteJobRepository(_tempDbPath);
        _hashCalculator = new Sha256HashCalculator();
    }

    public void Dispose()
    {
        if (File.Exists(_tempDbPath))
        {
            try { File.Delete(_tempDbPath); } catch { }
        }
    }

    [Fact]
    public async Task Sha256_IdenticalStreams_ReturnMatchingHashes()
    {
        byte[] data = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 };
        using var stream1 = new MemoryStream(data);
        using var stream2 = new MemoryStream(data);

        string hash1 = await _hashCalculator.ComputeSha256Async(stream1);
        string hash2 = await _hashCalculator.ComputeSha256Async(stream2);

        Assert.Equal(hash1, hash2);
    }

    [Fact]
    public async Task GetInfoAsync_DuplicateImagesPresent_FlagsDuplicatesCorrectly()
    {
        string sharedHash = "abc123sha256hash";
        var img1 = new ProcessedImage { FileName = "orig.jpg", FilePath = "/data/orig.jpg", FileSize = 100, SHA256 = sharedHash, Status = ProcessingStatus.Success };
        var img2 = new ProcessedImage { FileName = "dup.jpg", FilePath = "/data/dup.jpg", FileSize = 100, SHA256 = sharedHash, Status = ProcessingStatus.Success };

        await _repository.AddBatchAsync(new[] { img1, img2 });

        var service = new ImageProcessingService(_repository, _jobRepository);
        var paginated = await _repository.GetPaginatedAsync(1, 10);
        var first = paginated.Items.First(i => i.FileName == "orig.jpg");

        var info = await service.GetInfoAsync(first.Id);

        Assert.NotNull(info);
        Assert.True(info.HasDuplicates);
        Assert.Single(info.Duplicates);
        Assert.Equal("dup.jpg", info.Duplicates[0].FileName);
    }
}
