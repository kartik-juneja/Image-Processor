using ImageProcessor.Domain.Entities;
using ImageProcessor.Domain.Models;

namespace ImageProcessor.Application.Interfaces;

public interface IImageRepository
{
    Task AddBatchAsync(IEnumerable<ProcessedImage> images, CancellationToken cancellationToken = default);
    Task<ProcessedImage?> GetByIdAsync(long id, CancellationToken cancellationToken = default);
    Task<ProcessedImage?> GetByFilePathAsync(string filePath, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ProcessedImage>> GetByHashAsync(string sha256, CancellationToken cancellationToken = default);
    Task<PaginatedList<ProcessedImage>> GetPaginatedAsync(int pageIndex, int pageSize, CancellationToken cancellationToken = default);
    Task<PaginatedList<ProcessedImage>> SearchAsync(ImageSearchFilter filter, int pageIndex, int pageSize, CancellationToken cancellationToken = default);
    Task<int> GetTotalCountAsync(CancellationToken cancellationToken = default);
}
