using ImageProcessor.Domain.Entities;
using ImageProcessor.Domain.Enums;
using ImageProcessor.Domain.Models;
using ImageProcessor.Infrastructure.Persistence;
using Xunit;

namespace ImageProcessor.Tests;

public class PaginationAndSearchTests : IDisposable
{
    private readonly string _tempDbPath;
    private readonly SqliteImageRepository _repository;

    public PaginationAndSearchTests()
    {
        _tempDbPath = Path.Combine(Path.GetTempPath(), $"test_page_{Guid.NewGuid():N}.db");
        var dbInit = new DatabaseInitializer(_tempDbPath);
        dbInit.InitializeAsync().GetAwaiter().GetResult();
        _repository = new SqliteImageRepository(_tempDbPath);
    }

    public void Dispose()
    {
        if (File.Exists(_tempDbPath))
        {
            try { File.Delete(_tempDbPath); } catch { }
        }
    }

    [Fact]
    public async Task GetPaginatedAsync_PaginationWorksCorrectly()
    {
        var list = new List<ProcessedImage>();
        for (int i = 1; i <= 25; i++)
        {
            list.Add(new ProcessedImage
            {
                FileName = $"img_{i}.png",
                FilePath = $"/data/img_{i}.png",
                FileSize = 100 * i,
                Format = i % 2 == 0 ? "PNG" : "JPEG",
                Status = ProcessingStatus.Success
            });
        }

        await _repository.AddBatchAsync(list);

        var page1 = await _repository.GetPaginatedAsync(1, 10);
        var page2 = await _repository.GetPaginatedAsync(2, 10);
        var page3 = await _repository.GetPaginatedAsync(3, 10);

        Assert.Equal(25, page1.TotalCount);
        Assert.Equal(3, page1.TotalPages);
        Assert.Equal(10, page1.Items.Count);
        Assert.Equal(10, page2.Items.Count);
        Assert.Equal(5, page3.Items.Count);
    }

    [Fact]
    public async Task SearchAsync_FiltersByNameAndFormat()
    {
        var list = new List<ProcessedImage>
        {
            new ProcessedImage { FileName = "vacation_beach.jpg", FilePath = "/data/1.jpg", FileSize = 100, Format = "JPEG", Status = ProcessingStatus.Success },
            new ProcessedImage { FileName = "vacation_mountain.png", FilePath = "/data/2.png", FileSize = 200, Format = "PNG", Status = ProcessingStatus.Success },
            new ProcessedImage { FileName = "work_office.jpg", FilePath = "/data/3.jpg", FileSize = 300, Format = "JPEG", Status = ProcessingStatus.Success }
        };

        await _repository.AddBatchAsync(list);

        var searchByName = await _repository.SearchAsync(new ImageSearchFilter { Name = "vacation" }, 1, 10);
        var searchByFormat = await _repository.SearchAsync(new ImageSearchFilter { Format = "PNG" }, 1, 10);

        Assert.Equal(2, searchByName.TotalCount);
        Assert.Single(searchByFormat.Items);
        Assert.Equal("vacation_mountain.png", searchByFormat.Items[0].FileName);
    }
}
