namespace ImageProcessor.Application.Options;

public class ProcessorOptions
{
    public const string SectionName = "ProcessorOptions";

    public int WorkerCount { get; set; } = Environment.ProcessorCount > 0 ? Environment.ProcessorCount : 4;
    public int QueueCapacity { get; set; } = 500;
    public int BatchSize { get; set; } = 100;
    public int ThumbnailMaxWidth { get; set; } = 300;
    public int ThumbnailMaxHeight { get; set; } = 300;
    public string InputDirectory { get; set; } = "./data/images";
    public string ThumbnailDirectory { get; set; } = "./data/thumbnails";
    public string DatabasePath { get; set; } = "./data/image_processor.db";
}
