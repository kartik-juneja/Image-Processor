using System.Threading.Channels;
using ImageProcessor.Application.Options;
using ImageProcessor.Domain.Entities;
using ImageProcessor.Domain.Enums;
using ImageProcessor.Infrastructure.ImageProcessing;
using ImageProcessor.Processing.Pipeline;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ImageProcessor.Tests;

public class CorruptImageHandlingTests : IDisposable
{
    private readonly string _tempDir;

    public CorruptImageHandlingTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"test_corrupt_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, true); } catch { }
        }
    }

    [Fact]
    public async Task ProcessSingleFileAsync_CorruptImage_RecordsFailedStatus()
    {
        // Arrange
        string corruptFilePath = Path.Combine(_tempDir, "bad_image.jpg");
        await File.WriteAllTextAsync(corruptFilePath, "NOT AN IMAGE FILE CONTENT");

        var hashCalc = new Sha256HashCalculator();
        var metaExtractor = new ImageSharpMetadataExtractor();
        var thumbGen = new ImageSharpThumbnailGenerator();
        var options = new ProcessorOptions
        {
            InputDirectory = _tempDir,
            ThumbnailDirectory = Path.Combine(_tempDir, "thumbs")
        };
        var worker = new ImageWorker(hashCalc, metaExtractor, thumbGen, options, NullLogger<ImageWorker>.Instance);

        var fileChannel = Channel.CreateUnbounded<string>();
        var resultChannel = Channel.CreateUnbounded<ProcessedImage>();

        await fileChannel.Writer.WriteAsync(corruptFilePath);
        fileChannel.Writer.Complete();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        // Act
        await worker.ProcessWorkAsync(fileChannel.Reader, resultChannel.Writer, cts.Token);
        resultChannel.Writer.Complete();

        // Assert
        var results = new List<ProcessedImage>();
        await foreach (var item in resultChannel.Reader.ReadAllAsync())
        {
            results.Add(item);
        }

        Assert.Single(results);
        Assert.Equal(ProcessingStatus.Failed, results[0].Status);
        Assert.NotNull(results[0].ErrorMessage);
        Assert.Contains("Unsupported or corrupted", results[0].ErrorMessage);
    }
}
