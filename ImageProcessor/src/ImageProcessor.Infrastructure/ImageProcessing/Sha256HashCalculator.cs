using ImageProcessor.Application.Interfaces;
using System.Security.Cryptography;

namespace ImageProcessor.Infrastructure.ImageProcessing;

public class Sha256HashCalculator : IHashCalculator
{
    public async Task<string> ComputeSha256Async(Stream stream, CancellationToken cancellationToken = default)
    {
        if (stream.CanSeek)
        {
            stream.Position = 0;
        }

        byte[] hashBytes = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }
}
