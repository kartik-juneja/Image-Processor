using ImageProcessor.Application.Interfaces;
using ImageProcessor.Domain.Entities;
using ImageProcessor.Domain.Models;

namespace ImageProcessor.Application.Services;

public record ImageDetailsResult(
    ProcessedImage Image,
    IReadOnlyList<ProcessedImage> Duplicates,
    bool HasDuplicates
);

public class ImageProcessingService
{
    private readonly IImageRepository _imageRepository;
    private readonly IJobRepository _jobRepository;

    public ImageProcessingService(IImageRepository imageRepository, IJobRepository jobRepository)
    {
        _imageRepository = imageRepository;
        _jobRepository = jobRepository;
    }

    public async Task<ProcessingJob?> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        return await _jobRepository.GetLatestJobAsync(cancellationToken);
    }

    public async Task<PaginatedList<ProcessedImage>> GetListAsync(int pageIndex = 1, int pageSize = 10, CancellationToken cancellationToken = default)
    {
        if (pageIndex < 1) pageIndex = 1;
        if (pageSize < 1) pageSize = 10;
        return await _imageRepository.GetPaginatedAsync(pageIndex, pageSize, cancellationToken);
    }

    public async Task<PaginatedList<ProcessedImage>> SearchAsync(ImageSearchFilter filter, int pageIndex = 1, int pageSize = 10, CancellationToken cancellationToken = default)
    {
        if (pageIndex < 1) pageIndex = 1;
        if (pageSize < 1) pageSize = 10;
        return await _imageRepository.SearchAsync(filter, pageIndex, pageSize, cancellationToken);
    }

    public async Task<ImageDetailsResult?> GetInfoAsync(long id, CancellationToken cancellationToken = default)
    {
        var image = await _imageRepository.GetByIdAsync(id, cancellationToken);
        if (image == null) return null;

        IReadOnlyList<ProcessedImage> duplicates = Array.Empty<ProcessedImage>();
        if (!string.IsNullOrEmpty(image.SHA256))
        {
            var matching = await _imageRepository.GetByHashAsync(image.SHA256, cancellationToken);
            duplicates = matching.Where(m => m.Id != image.Id).ToList();
        }

        return new ImageDetailsResult(image, duplicates, duplicates.Count > 0);
    }

    public async Task<string?> GetThumbnailPathAsync(long id, CancellationToken cancellationToken = default)
    {
        var image = await _imageRepository.GetByIdAsync(id, cancellationToken);
        return image?.ThumbnailPath;
    }

    public async Task RequestCancelAsync(CancellationToken cancellationToken = default)
    {
        await _jobRepository.RequestCancellationAsync(cancellationToken);
    }
}
