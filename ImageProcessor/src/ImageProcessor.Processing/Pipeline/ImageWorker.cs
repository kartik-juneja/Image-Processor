using System.Threading.Channels;
using ImageProcessor.Application.Interfaces;
using ImageProcessor.Application.Options;
using ImageProcessor.Domain.Entities;
using ImageProcessor.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace ImageProcessor.Processing.Pipeline;

public class ImageWorker
{
    private readonly IHashCalculator _hashCalculator;
    private readonly IImageMetadataExtractor _metadataExtractor;
    private readonly IThumbnailGenerator _thumbnailGenerator;
    private readonly ProcessorOptions _options;
    private readonly ILogger<ImageWorker> _logger;

    public ImageWorker(
        IHashCalculator hashCalculator,
        IImageMetadataExtractor metadataExtractor,
        IThumbnailGenerator thumbnailGenerator,
        ProcessorOptions options,
        ILogger<ImageWorker> logger)
    {
        _hashCalculator = hashCalculator;
        _metadataExtractor = metadataExtractor;
        _thumbnailGenerator = thumbnailGenerator;
        _options = options;
        _logger = logger;
    }

    public async Task ProcessWorkAsync(ChannelReader<string> reader, ChannelWriter<ProcessedImage> resultWriter, CancellationToken cancellationToken)
    {
        await foreach (var filePath in reader.ReadAllAsync(cancellationToken))
        {
            if (cancellationToken.IsCancellationRequested) break;

            ProcessedImage result;
            try
            {
                result = await ProcessSingleFileAsync(filePath, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected failure while processing file '{FilePath}'", filePath);
                var fi = new FileInfo(filePath);
                result = new ProcessedImage
                {
                    FileName = Path.GetFileName(filePath),
                    FilePath = filePath,
                    FileSize = fi.Exists ? fi.Length : 0,
                    Status = ProcessingStatus.Failed,
                    ErrorMessage = $"Unexpected error: {ex.Message}",
                    CreatedAt = fi.Exists ? fi.CreationTimeUtc : DateTime.UtcNow,
                    ProcessedAt = DateTime.UtcNow
                };
            }

            await resultWriter.WriteAsync(result, cancellationToken);
        }
    }

    private async Task<ProcessedImage> ProcessSingleFileAsync(string filePath, CancellationToken cancellationToken)
    {
        var fileInfo = new FileInfo(filePath);
        if (!fileInfo.Exists)
        {
            return new ProcessedImage
            {
                FileName = Path.GetFileName(filePath),
                FilePath = filePath,
                FileSize = 0,
                Status = ProcessingStatus.Failed,
                ErrorMessage = "File does not exist or was removed.",
                CreatedAt = DateTime.UtcNow,
                ProcessedAt = DateTime.UtcNow
            };
        }

        string fileName = fileInfo.Name;
        long fileSize = fileInfo.Length;
        DateTime createdAt = fileInfo.CreationTimeUtc;

        string hash;
        try
        {
            using var fileStreamForHash = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
            hash = await _hashCalculator.ComputeSha256Async(fileStreamForHash, cancellationToken);
        }
        catch (Exception ex)
        {
            return new ProcessedImage
            {
                FileName = fileName,
                FilePath = filePath,
                FileSize = fileSize,
                Status = ProcessingStatus.Failed,
                ErrorMessage = $"Failed to compute hash: {ex.Message}",
                CreatedAt = createdAt,
                ProcessedAt = DateTime.UtcNow
            };
        }

        ImageMetadataInfo? metadata = null;
        try
        {
            using var fileStreamForMeta = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
            metadata = await _metadataExtractor.ExtractMetadataAsync(fileStreamForMeta, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Metadata extraction failed for '{FilePath}'", filePath);
        }

        if (metadata == null)
        {
            return new ProcessedImage
            {
                FileName = fileName,
                FilePath = filePath,
                FileSize = fileSize,
                SHA256 = hash,
                Status = ProcessingStatus.Failed,
                ErrorMessage = "Unsupported or corrupted image format.",
                CreatedAt = createdAt,
                ProcessedAt = DateTime.UtcNow
            };
        }

        string? thumbnailPath = null;
        try
        {
            using var fileStreamForThumb = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
            thumbnailPath = await _thumbnailGenerator.GenerateThumbnailAsync(
                fileStreamForThumb,
                _options.ThumbnailDirectory,
                hash,
                _options.ThumbnailMaxWidth,
                _options.ThumbnailMaxHeight,
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Thumbnail generation failed for '{FilePath}'", filePath);
            return new ProcessedImage
            {
                FileName = fileName,
                FilePath = filePath,
                FileSize = fileSize,
                Width = metadata.Width,
                Height = metadata.Height,
                Format = metadata.Format,
                SHA256 = hash,
                Status = ProcessingStatus.Failed,
                ErrorMessage = $"Thumbnail creation failed: {ex.Message}",
                CreatedAt = createdAt,
                ProcessedAt = DateTime.UtcNow
            };
        }

        return new ProcessedImage
        {
            FileName = fileName,
            FilePath = filePath,
            FileSize = fileSize,
            Width = metadata.Width,
            Height = metadata.Height,
            Format = metadata.Format,
            SHA256 = hash,
            ThumbnailPath = thumbnailPath,
            Status = ProcessingStatus.Success,
            CreatedAt = createdAt,
            ProcessedAt = DateTime.UtcNow
        };
    }
}
