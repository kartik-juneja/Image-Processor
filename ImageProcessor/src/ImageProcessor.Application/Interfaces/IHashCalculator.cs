namespace ImageProcessor.Application.Interfaces;

public interface IHashCalculator
{
    Task<string> ComputeSha256Async(Stream stream, CancellationToken cancellationToken = default);
}
