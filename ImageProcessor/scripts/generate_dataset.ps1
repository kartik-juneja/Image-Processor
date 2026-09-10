# Powershell script to generate sample dataset under ./data/images/ using C# dotnet inline
$inputDir = "./data/images"
if (Test-Path $inputDir) {
    Remove-Item -Recurse -Force $inputDir
}
New-Item -ItemType Directory -Force -Path $inputDir | Out-Null

Write-Host "Generating valid sample images in $inputDir..."

$csharpCode = @'
using System;
using System.IO;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

public class DatasetGen
{
    public static void Generate()
    {
        string dir = "./data/images";
        Directory.CreateDirectory(dir);

        for (int i = 1; i <= 10; i++)
        {
            using (var img = new Image<Rgba32>(100 + i * 20, 100 + i * 15))
            {
                string path = Path.Combine(dir, "sample_image_" + i + ".png");
                img.SaveAsPng(path);
            }
        }

        // Create duplicate image
        File.Copy(Path.Combine(dir, "sample_image_1.png"), Path.Combine(dir, "duplicate_image_1.png"), true);
        File.Copy(Path.Combine(dir, "sample_image_2.png"), Path.Combine(dir, "duplicate_image_2.png"), true);

        // Create corrupt file
        File.WriteAllText(Path.Combine(dir, "corrupt_image.jpg"), "THIS IS NOT AN IMAGE");
    }
}
'@

Add-Type -TypeDefinition $csharpCode -ReferencedAssemblies "C:\Users\Iotasol11\.nuget\packages\sixlabors.imagesharp\4.1.1\lib\net8.0\SixLabors.ImageSharp.dll"
[DatasetGen]::Generate()

Write-Host "Sample dataset generated successfully."
