using ImageProcessor.Application.Interfaces;
using ImageProcessor.Application.Options;
using ImageProcessor.Application.Services;
using ImageProcessor.Domain.Entities;
using ImageProcessor.Domain.Enums;
using ImageProcessor.Domain.Models;
using ImageProcessor.Infrastructure.ImageProcessing;
using ImageProcessor.Infrastructure.Persistence;
using ImageProcessor.Processing.Pipeline;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ImageProcessor.Cli;

public class Program
{
    public static async Task<int> Main(string[] args)
    {
        var config = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
            .AddEnvironmentVariables()
            .Build();

        var options = new ProcessorOptions();
        config.GetSection(ProcessorOptions.SectionName).Bind(options);

        var services = new ServiceCollection();
        services.AddSingleton(options);
        services.AddLogging(builder =>
        {
            builder.AddConfiguration(config.GetSection("Logging"));
            builder.AddConsole();
        });

        // Infrastructure
        services.AddSingleton(sp => new DatabaseInitializer(options.DatabasePath));
        services.AddSingleton<IImageRepository>(sp => new SqliteImageRepository(options.DatabasePath));
        services.AddSingleton<IJobRepository>(sp => new SqliteJobRepository(options.DatabasePath));
        services.AddSingleton<IImageMetadataExtractor, ImageSharpMetadataExtractor>();
        services.AddSingleton<IThumbnailGenerator, ImageSharpThumbnailGenerator>();
        services.AddSingleton<IHashCalculator, Sha256HashCalculator>();

        // Application Service
        services.AddSingleton<ImageProcessingService>();

        // Processing Pipeline
        services.AddTransient<FileDiscoveryProducer>();
        services.AddTransient<ImageWorker>();
        services.AddSingleton(sp => new SqliteBatchWriter(
            sp.GetRequiredService<IImageRepository>(),
            sp.GetRequiredService<IJobRepository>(),
            options.BatchSize,
            sp.GetRequiredService<ILogger<SqliteBatchWriter>>()
        ));
        services.AddSingleton(sp => new Func<ImageWorker>(() => sp.GetRequiredService<ImageWorker>()));
        services.AddSingleton<BoundedProcessingPipeline>();

        var provider = services.BuildServiceProvider();

        // Initialize Database Schema & Tables
        var dbInit = provider.GetRequiredService<DatabaseInitializer>();
        await dbInit.InitializeAsync();

        if (args.Length == 0)
        {
            PrintUsage();
            return 0;
        }

        string command = args[0].ToLowerInvariant();
        var cmdArgs = args.Skip(1).ToArray();

        try
        {
            switch (command)
            {
                case "process":
                    return await HandleProcessAsync(provider, cmdArgs);

                case "status":
                    return await HandleStatusAsync(provider);

                case "list":
                    return await HandleListAsync(provider, cmdArgs);

                case "search":
                    return await HandleSearchAsync(provider, cmdArgs);

                case "info":
                    return await HandleInfoAsync(provider, cmdArgs);

                case "thumbnail":
                    return await HandleThumbnailAsync(provider, cmdArgs);

                case "cancel":
                    return await HandleCancelAsync(provider);

                case "--help":
                case "-h":
                case "help":
                    PrintUsage();
                    return 0;

                default:
                    Console.WriteLine($"Unknown command '{command}'.");
                    PrintUsage();
                    return 1;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Fatal Error: {ex.Message}");
            return 1;
        }
    }

    private static async Task<int> HandleProcessAsync(IServiceProvider provider, string[] args)
    {
        int? workers = GetIntArgument(args, "--workers");
        var pipeline = provider.GetRequiredService<BoundedProcessingPipeline>();
        using var cts = new CancellationTokenSource();

        Console.CancelKeyPress += (sender, e) =>
        {
            Console.WriteLine("\nCancellation requested (Ctrl+C)...");
            e.Cancel = true;
            cts.Cancel();
        };

        var job = await pipeline.RunPipelineAsync(workers, cts.Token);
        return job.Status == JobStatus.Completed ? 0 : 1;
    }

    private static async Task<int> HandleStatusAsync(IServiceProvider provider)
    {
        var service = provider.GetRequiredService<ImageProcessingService>();
        var job = await service.GetStatusAsync();

        if (job == null)
        {
            Console.WriteLine("No processing job history found.");
            return 0;
        }

        Console.WriteLine($"--- Job Status ---");
        Console.WriteLine($"Job ID      : {job.JobId}");
        Console.WriteLine($"Status      : {job.Status}");
        Console.WriteLine($"Discovered  : {job.TotalDiscovered}");
        Console.WriteLine($"Processed   : {job.ProcessedCount}");
        Console.WriteLine($"Success     : {job.SuccessCount}");
        Console.WriteLine($"Failed      : {job.FailedCount}");
        Console.WriteLine($"Started At  : {job.StartedAt.ToLocalTime():yyyy-MM-dd HH:mm:ss}");
        Console.WriteLine($"Completed At: {(job.CompletedAt.HasValue ? job.CompletedAt.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") : "N/A")}");
        return 0;
    }

    private static async Task<int> HandleListAsync(IServiceProvider provider, string[] args)
    {
        int page = GetIntArgument(args, "--page") ?? 1;
        int size = GetIntArgument(args, "--size") ?? 10;

        var service = provider.GetRequiredService<ImageProcessingService>();
        var result = await service.GetListAsync(page, size);

        Console.WriteLine($"--- Processed Images (Page {result.PageIndex}/{Math.Max(1, result.TotalPages)}, Total: {result.TotalCount}) ---");
        PrintImageTable(result.Items);
        return 0;
    }

    private static async Task<int> HandleSearchAsync(IServiceProvider provider, string[] args)
    {
        string? name = GetStringArgument(args, "--name");
        string? format = GetStringArgument(args, "--format");
        int page = GetIntArgument(args, "--page") ?? 1;
        int size = GetIntArgument(args, "--size") ?? 10;

        var service = provider.GetRequiredService<ImageProcessingService>();
        var filter = new ImageSearchFilter { Name = name, Format = format };
        var result = await service.SearchAsync(filter, page, size);

        Console.WriteLine($"--- Search Results (Page {result.PageIndex}/{Math.Max(1, result.TotalPages)}, Total: {result.TotalCount}) ---");
        PrintImageTable(result.Items);
        return 0;
    }

    private static async Task<int> HandleInfoAsync(IServiceProvider provider, string[] args)
    {
        long? id = GetLongArgument(args, "--id");
        if (!id.HasValue)
        {
            Console.WriteLine("Error: --id <number> is required for 'info' command.");
            return 1;
        }

        var service = provider.GetRequiredService<ImageProcessingService>();
        var info = await service.GetInfoAsync(id.Value);

        if (info == null)
        {
            Console.WriteLine($"Image with ID {id.Value} not found.");
            return 1;
        }

        var img = info.Image;
        Console.WriteLine($"--- Image Details (ID: {img.Id}) ---");
        Console.WriteLine($"File Name    : {img.FileName}");
        Console.WriteLine($"File Path    : {img.FilePath}");
        Console.WriteLine($"File Size    : {FormatFileSize(img.FileSize)} ({img.FileSize} bytes)");
        Console.WriteLine($"Resolution   : {(img.Width.HasValue && img.Height.HasValue ? $"{img.Width}x{img.Height}" : "N/A")}");
        Console.WriteLine($"Format       : {img.Format ?? "N/A"}");
        Console.WriteLine($"SHA-256      : {img.SHA256 ?? "N/A"}");
        Console.WriteLine($"Thumbnail    : {img.ThumbnailPath ?? "N/A"}");
        Console.WriteLine($"Status       : {img.Status}");
        if (!string.IsNullOrEmpty(img.ErrorMessage))
        {
            Console.WriteLine($"Error        : {img.ErrorMessage}");
        }
        Console.WriteLine($"Processed At : {img.ProcessedAt.ToLocalTime():yyyy-MM-dd HH:mm:ss}");

        Console.WriteLine($"\nDuplicate Group Information:");
        if (info.HasDuplicates)
        {
            Console.WriteLine($"  [WARNING] This image HAS {info.Duplicates.Count} duplicate(s) with matching SHA-256 hash!");
            foreach (var dup in info.Duplicates)
            {
                Console.WriteLine($"   - ID: {dup.Id} | Name: {dup.FileName} | Path: {dup.FilePath}");
            }
        }
        else
        {
            Console.WriteLine("  No duplicate images found for this hash.");
        }

        return 0;
    }

    private static async Task<int> HandleThumbnailAsync(IServiceProvider provider, string[] args)
    {
        long? id = GetLongArgument(args, "--id");
        if (!id.HasValue)
        {
            Console.WriteLine("Error: --id <number> is required for 'thumbnail' command.");
            return 1;
        }

        var service = provider.GetRequiredService<ImageProcessingService>();
        string? thumbPath = await service.GetThumbnailPathAsync(id.Value);

        if (string.IsNullOrEmpty(thumbPath))
        {
            Console.WriteLine($"Thumbnail not found or not generated for Image ID {id.Value}.");
            return 1;
        }

        Console.WriteLine(thumbPath);
        return 0;
    }

    private static async Task<int> HandleCancelAsync(IServiceProvider provider)
    {
        var service = provider.GetRequiredService<ImageProcessingService>();
        await service.RequestCancelAsync();
        Console.WriteLine("Cancellation request sent successfully to active processing job.");
        return 0;
    }

    private static void PrintImageTable(IReadOnlyList<ProcessedImage> items)
    {
        if (items.Count == 0)
        {
            Console.WriteLine("No records found.");
            return;
        }

        Console.WriteLine($"{"ID",-6} | {"Status",-8} | {"Size",-10} | {"Resolution",-11} | {"Format",-8} | {"FileName"}");
        Console.WriteLine(new string('-', 85));

        foreach (var img in items)
        {
            string res = (img.Width.HasValue && img.Height.HasValue) ? $"{img.Width}x{img.Height}" : "N/A";
            Console.WriteLine($"{img.Id,-6} | {img.Status,-8} | {FormatFileSize(img.FileSize),-10} | {res,-11} | {img.Format ?? "N/A",-8} | {img.FileName}");
        }
    }

    private static string FormatFileSize(long bytes)
    {
        if (bytes >= 1024 * 1024)
            return $"{bytes / (1024.0 * 1024.0):F2} MB";
        if (bytes >= 1024)
            return $"{bytes / 1024.0:F2} KB";
        return $"{bytes} B";
    }

    private static int? GetIntArgument(string[] args, string flag)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i].Equals(flag, StringComparison.OrdinalIgnoreCase) && int.TryParse(args[i + 1], out int val))
            {
                return val;
            }
        }
        return null;
    }

    private static long? GetLongArgument(string[] args, string flag)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i].Equals(flag, StringComparison.OrdinalIgnoreCase) && long.TryParse(args[i + 1], out long val))
            {
                return val;
            }
        }
        return null;
    }

    private static string? GetStringArgument(string[] args, string flag)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i].Equals(flag, StringComparison.OrdinalIgnoreCase))
            {
                return args[i + 1];
            }
        }
        return null;
    }

    private static void PrintUsage()
    {
        Console.WriteLine(@"
===================================================================
 Large-Scale Image Processing CLI EXE (.NET 8)
===================================================================
Commands:
  process [--workers N]            Start processing images in ./data/images/
  status                           Show current/last job status and progress
  list [--page N] [--size N]       Paginated list of processed images
  search [--name X] [--format X]   Filter processed images
  info --id X                      Retrieve detailed info & duplicates for image ID
  thumbnail --id X                 Print thumbnail file path for image ID
  cancel                           Cancel active processing job
");
    }
}
