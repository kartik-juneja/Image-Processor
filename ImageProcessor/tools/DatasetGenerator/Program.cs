using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace DatasetGenerator;

public class Program
{
    public static void Main(string[] args)
    {
        string dir = "./data/images";
        if (Directory.Exists(dir))
        {
            Directory.Delete(dir, true);
        }
        Directory.CreateDirectory(dir);

        Console.WriteLine($"Generating sample images in {dir}...");

        for (int i = 1; i <= 10; i++)
        {
            using var img = new Image<Rgba32>(200 + i * 20, 150 + i * 15);
            string path = Path.Combine(dir, $"sample_image_{i}.png");
            img.SaveAsPng(path);
        }

        // Duplicate images (identical files)
        File.Copy(Path.Combine(dir, "sample_image_1.png"), Path.Combine(dir, "duplicate_image_1.png"), true);
        File.Copy(Path.Combine(dir, "sample_image_2.png"), Path.Combine(dir, "duplicate_image_2.png"), true);

        // Corrupt file
        File.WriteAllText(Path.Combine(dir, "corrupt_image.jpg"), "THIS IS NOT A VALID IMAGE CONTENT");

        Console.WriteLine("Sample dataset generated successfully: 10 PNGs, 2 duplicates, 1 corrupt file.");
    }
}
