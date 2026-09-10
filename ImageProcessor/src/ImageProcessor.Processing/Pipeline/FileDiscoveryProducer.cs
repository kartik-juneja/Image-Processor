using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace ImageProcessor.Processing.Pipeline;

public class FileDiscoveryProducer
{
    private readonly ILogger<FileDiscoveryProducer> _logger;

    public FileDiscoveryProducer(ILogger<FileDiscoveryProducer> logger)
    {
        _logger = logger;
    }

    public async Task<int> ProduceFilePathsAsync(string inputDirectory, ChannelWriter<string> writer, CancellationToken cancellationToken)
    {
        int count = 0;
        try
        {
            if (!Directory.Exists(inputDirectory))
            {
                _logger.LogWarning("Input directory '{InputDirectory}' does not exist.", inputDirectory);
                writer.Complete();
                return 0;
            }

            _logger.LogInformation("Starting file discovery in '{InputDirectory}'", inputDirectory);

            // EnumerateFiles yields files lazily without loading the whole list into memory
            foreach (var filePath in Directory.EnumerateFiles(inputDirectory))
            {
                cancellationToken.ThrowIfCancellationRequested();
                await writer.WriteAsync(filePath, cancellationToken);
                count++;
            }

            _logger.LogInformation("File discovery completed. Total discovered files: {Count}", count);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("File discovery was cancelled after discovering {Count} files.", count);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred during file discovery in '{InputDirectory}'", inputDirectory);
        }
        finally
        {
            writer.Complete();
        }

        return count;
    }
}
