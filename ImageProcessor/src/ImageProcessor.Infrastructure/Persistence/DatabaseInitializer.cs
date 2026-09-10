using Microsoft.Data.Sqlite;

namespace ImageProcessor.Infrastructure.Persistence;

public class DatabaseInitializer
{
    private readonly string _connectionString;

    public DatabaseInitializer(string databasePath)
    {
        var dir = Path.GetDirectoryName(databasePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        };
        _connectionString = builder.ConnectionString;
    }

    public async Task InitializeAsync()
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        using var command = connection.CreateCommand();
        command.CommandText = @"
            PRAGMA journal_mode=WAL;
            PRAGMA synchronous=NORMAL;

            CREATE TABLE IF NOT EXISTS ProcessedImages (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                FileName TEXT NOT NULL,
                FilePath TEXT NOT NULL UNIQUE,
                FileSize INTEGER NOT NULL,
                Width INTEGER NULL,
                Height INTEGER NULL,
                Format TEXT NULL,
                SHA256 TEXT NULL,
                ThumbnailPath TEXT NULL,
                Status TEXT NOT NULL,
                ErrorMessage TEXT NULL,
                CreatedAt TEXT NOT NULL,
                ProcessedAt TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS ProcessingJob (
                JobId TEXT PRIMARY KEY,
                TotalDiscovered INTEGER NOT NULL,
                ProcessedCount INTEGER NOT NULL,
                SuccessCount INTEGER NOT NULL,
                FailedCount INTEGER NOT NULL,
                Status TEXT NOT NULL,
                StartedAt TEXT NOT NULL,
                CompletedAt TEXT NULL,
                CancellationRequested INTEGER NOT NULL DEFAULT 0
            );

            CREATE INDEX IF NOT EXISTS idx_images_sha256 ON ProcessedImages(SHA256);
            CREATE INDEX IF NOT EXISTS idx_images_filename ON ProcessedImages(FileName);
            CREATE INDEX IF NOT EXISTS idx_images_format ON ProcessedImages(Format);
            CREATE INDEX IF NOT EXISTS idx_images_status ON ProcessedImages(Status);
        ";
        await command.ExecuteNonQueryAsync();
    }
}
