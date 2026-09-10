namespace ImageProcessor.Application.Interfaces;

public interface IThumbnailGenerator
{
    Task<string> GenerateThumbnailAsync(Stream inputStream, string outputDirectory, string fileNameHash, int maxWidth, int maxHeight, CancellationToken cancellationToken = default);
}
