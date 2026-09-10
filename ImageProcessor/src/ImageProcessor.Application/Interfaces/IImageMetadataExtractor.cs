namespace ImageProcessor.Application.Interfaces;

public record ImageMetadataInfo(int Width, int Height, string Format);

public interface IImageMetadataExtractor
{
    Task<ImageMetadataInfo?> ExtractMetadataAsync(Stream stream, CancellationToken cancellationToken = default);
}
