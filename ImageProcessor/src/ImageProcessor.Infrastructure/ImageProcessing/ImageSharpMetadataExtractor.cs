using ImageProcessor.Application.Interfaces;
using SixLabors.ImageSharp;

namespace ImageProcessor.Infrastructure.ImageProcessing;

public class ImageSharpMetadataExtractor : IImageMetadataExtractor
{
    public async Task<ImageMetadataInfo?> ExtractMetadataAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        try
        {
            if (stream.CanSeek)
            {
                stream.Position = 0;
            }

            var info = await Image.IdentifyAsync(stream, cancellationToken);
            if (info == null)
            {
                return null;
            }

            string formatName = info.Metadata?.DecodedImageFormat?.Name ?? "UNKNOWN";
            return new ImageMetadataInfo(info.Width, info.Height, formatName);
        }
        catch (UnknownImageFormatException)
        {
            return null;
        }
        catch (InvalidImageContentException)
        {
            return null;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
