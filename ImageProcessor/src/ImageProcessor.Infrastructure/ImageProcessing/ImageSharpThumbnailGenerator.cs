using ImageProcessor.Application.Interfaces;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;

namespace ImageProcessor.Infrastructure.ImageProcessing;

public class ImageSharpThumbnailGenerator : IThumbnailGenerator
{
    public async Task<string> GenerateThumbnailAsync(Stream inputStream, string outputDirectory, string fileNameHash, int maxWidth, int maxHeight, CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(outputDirectory))
        {
            Directory.CreateDirectory(outputDirectory);
        }

        if (inputStream.CanSeek)
        {
            inputStream.Position = 0;
        }

        string safeName = string.IsNullOrWhiteSpace(fileNameHash) ? Guid.NewGuid().ToString("N") : fileNameHash;
        string outputPath = Path.Combine(outputDirectory, $"{safeName}_thumb.jpg");

        // Idempotency / Concurrent Duplicate check:
        if (File.Exists(outputPath))
        {
            return outputPath;
        }

        string tempPath = Path.Combine(outputDirectory, $"{safeName}_{Guid.NewGuid():N}.tmp");

        try
        {
            using (var image = await Image.LoadAsync(inputStream, cancellationToken))
            {
                image.Mutate(x => x.Resize(new ResizeOptions
                {
                    Size = new Size(maxWidth, maxHeight),
                    Mode = ResizeMode.Max
                }));

                await image.SaveAsJpegAsync(tempPath, cancellationToken);
            }

            // Atomic move/replace or ignore if destination already created concurrently
            if (!File.Exists(outputPath))
            {
                try
                {
                    File.Move(tempPath, outputPath);
                }
                catch (IOException)
                {
                    if (File.Exists(tempPath)) File.Delete(tempPath);
                }
            }
            else
            {
                if (File.Exists(tempPath)) File.Delete(tempPath);
            }
        }
        catch (Exception)
        {
            if (File.Exists(tempPath)) try { File.Delete(tempPath); } catch { }
            throw;
        }

        return File.Exists(outputPath) ? outputPath : tempPath;
    }
}
