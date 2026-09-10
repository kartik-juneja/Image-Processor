using ImageProcessor.Infrastructure.ImageProcessing;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace ImageProcessor.Tests;

public class MetadataExtractorTests
{
    [Fact]
    public async Task ExtractMetadataAsync_ValidImage_ReturnsCorrectWidthHeightAndFormat()
    {
        // Arrange
        var extractor = new ImageSharpMetadataExtractor();
        using var ms = new MemoryStream();
        using (var image = new Image<Rgba32>(400, 250))
        {
            await image.SaveAsPngAsync(ms);
        }
        ms.Position = 0;

        // Act
        var meta = await extractor.ExtractMetadataAsync(ms);

        // Assert
        Assert.NotNull(meta);
        Assert.Equal(400, meta.Width);
        Assert.Equal(250, meta.Height);
        Assert.Equal("PNG", meta.Format, ignoreCase: true);
    }

    [Fact]
    public async Task ExtractMetadataAsync_InvalidData_ReturnsNull()
    {
        // Arrange
        var extractor = new ImageSharpMetadataExtractor();
        byte[] invalidBytes = "This is not an image file content"u8.ToArray();
        using var ms = new MemoryStream(invalidBytes);

        // Act
        var meta = await extractor.ExtractMetadataAsync(ms);

        // Assert
        Assert.Null(meta);
    }
}
