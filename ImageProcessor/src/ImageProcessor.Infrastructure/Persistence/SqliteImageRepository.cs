using ImageProcessor.Application.Interfaces;
using ImageProcessor.Domain.Entities;
using ImageProcessor.Domain.Enums;
using ImageProcessor.Domain.Models;
using Microsoft.Data.Sqlite;

namespace ImageProcessor.Infrastructure.Persistence;

public class SqliteImageRepository : IImageRepository
{
    private readonly string _connectionString;

    public SqliteImageRepository(string databasePath)
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        };
        _connectionString = builder.ConnectionString;
    }

    public async Task AddBatchAsync(IEnumerable<ProcessedImage> images, CancellationToken cancellationToken = default)
    {
        var itemList = images.ToList();
        if (itemList.Count == 0) return;

        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        using var transaction = connection.BeginTransaction();

        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = @"
            INSERT INTO ProcessedImages (
                FileName, FilePath, FileSize, Width, Height, Format, SHA256, ThumbnailPath, Status, ErrorMessage, CreatedAt, ProcessedAt
            ) VALUES (
                $FileName, $FilePath, $FileSize, $Width, $Height, $Format, $SHA256, $ThumbnailPath, $Status, $ErrorMessage, $CreatedAt, $ProcessedAt
            )
            ON CONFLICT(FilePath) DO UPDATE SET
                FileName=excluded.FileName,
                FileSize=excluded.FileSize,
                Width=excluded.Width,
                Height=excluded.Height,
                Format=excluded.Format,
                SHA256=excluded.SHA256,
                ThumbnailPath=excluded.ThumbnailPath,
                Status=excluded.Status,
                ErrorMessage=excluded.ErrorMessage,
                ProcessedAt=excluded.ProcessedAt;
        ";

        var pFileName = command.Parameters.Add("$FileName", SqliteType.Text);
        var pFilePath = command.Parameters.Add("$FilePath", SqliteType.Text);
        var pFileSize = command.Parameters.Add("$FileSize", SqliteType.Integer);
        var pWidth = command.Parameters.Add("$Width", SqliteType.Integer);
        var pHeight = command.Parameters.Add("$Height", SqliteType.Integer);
        var pFormat = command.Parameters.Add("$Format", SqliteType.Text);
        var pSHA256 = command.Parameters.Add("$SHA256", SqliteType.Text);
        var pThumbnailPath = command.Parameters.Add("$ThumbnailPath", SqliteType.Text);
        var pStatus = command.Parameters.Add("$Status", SqliteType.Text);
        var pErrorMessage = command.Parameters.Add("$ErrorMessage", SqliteType.Text);
        var pCreatedAt = command.Parameters.Add("$CreatedAt", SqliteType.Text);
        var pProcessedAt = command.Parameters.Add("$ProcessedAt", SqliteType.Text);

        foreach (var img in itemList)
        {
            pFileName.Value = img.FileName;
            pFilePath.Value = img.FilePath;
            pFileSize.Value = img.FileSize;
            pWidth.Value = (object?)img.Width ?? DBNull.Value;
            pHeight.Value = (object?)img.Height ?? DBNull.Value;
            pFormat.Value = (object?)img.Format ?? DBNull.Value;
            pSHA256.Value = (object?)img.SHA256 ?? DBNull.Value;
            pThumbnailPath.Value = (object?)img.ThumbnailPath ?? DBNull.Value;
            pStatus.Value = img.Status.ToString();
            pErrorMessage.Value = (object?)img.ErrorMessage ?? DBNull.Value;
            pCreatedAt.Value = img.CreatedAt.ToString("o");
            pProcessedAt.Value = img.ProcessedAt.ToString("o");

            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<ProcessedImage?> GetByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, FileName, FilePath, FileSize, Width, Height, Format, SHA256, ThumbnailPath, Status, ErrorMessage, CreatedAt, ProcessedAt FROM ProcessedImages WHERE Id = $Id";
        command.Parameters.AddWithValue("$Id", id);

        using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (await reader.ReadAsync(cancellationToken))
        {
            return MapReaderToEntity(reader);
        }

        return null;
    }

    public async Task<ProcessedImage?> GetByFilePathAsync(string filePath, CancellationToken cancellationToken = default)
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, FileName, FilePath, FileSize, Width, Height, Format, SHA256, ThumbnailPath, Status, ErrorMessage, CreatedAt, ProcessedAt FROM ProcessedImages WHERE FilePath = $FilePath";
        command.Parameters.AddWithValue("$FilePath", filePath);

        using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (await reader.ReadAsync(cancellationToken))
        {
            return MapReaderToEntity(reader);
        }

        return null;
    }

    public async Task<IReadOnlyList<ProcessedImage>> GetByHashAsync(string sha256, CancellationToken cancellationToken = default)
    {
        var list = new List<ProcessedImage>();
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, FileName, FilePath, FileSize, Width, Height, Format, SHA256, ThumbnailPath, Status, ErrorMessage, CreatedAt, ProcessedAt FROM ProcessedImages WHERE SHA256 = $SHA256";
        command.Parameters.AddWithValue("$SHA256", sha256);

        using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(MapReaderToEntity(reader));
        }

        return list;
    }

    public async Task<PaginatedList<ProcessedImage>> GetPaginatedAsync(int pageIndex, int pageSize, CancellationToken cancellationToken = default)
    {
        return await SearchAsync(new ImageSearchFilter(), pageIndex, pageSize, cancellationToken);
    }

    public async Task<PaginatedList<ProcessedImage>> SearchAsync(ImageSearchFilter filter, int pageIndex, int pageSize, CancellationToken cancellationToken = default)
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var whereClauses = new List<string>();
        using var countCmd = connection.CreateCommand();
        using var queryCmd = connection.CreateCommand();

        if (!string.IsNullOrWhiteSpace(filter.Name))
        {
            whereClauses.Add("FileName LIKE $Name");
            countCmd.Parameters.AddWithValue("$Name", $"%{filter.Name.Trim()}%");
            queryCmd.Parameters.AddWithValue("$Name", $"%{filter.Name.Trim()}%");
        }

        if (!string.IsNullOrWhiteSpace(filter.Format))
        {
            whereClauses.Add("Format LIKE $Format");
            countCmd.Parameters.AddWithValue("$Format", $"%{filter.Format.Trim()}%");
            queryCmd.Parameters.AddWithValue("$Format", $"%{filter.Format.Trim()}%");
        }

        if (filter.Status.HasValue)
        {
            whereClauses.Add("Status = $Status");
            countCmd.Parameters.AddWithValue("$Status", filter.Status.Value.ToString());
            queryCmd.Parameters.AddWithValue("$Status", filter.Status.Value.ToString());
        }

        string whereSql = whereClauses.Count > 0 ? " WHERE " + string.Join(" AND ", whereClauses) : "";

        countCmd.CommandText = $"SELECT COUNT(*) FROM ProcessedImages{whereSql}";
        var totalCountObj = await countCmd.ExecuteScalarAsync(cancellationToken);
        int totalCount = Convert.ToInt32(totalCountObj);

        int offset = (pageIndex - 1) * pageSize;
        queryCmd.CommandText = $"SELECT Id, FileName, FilePath, FileSize, Width, Height, Format, SHA256, ThumbnailPath, Status, ErrorMessage, CreatedAt, ProcessedAt FROM ProcessedImages{whereSql} ORDER BY Id ASC LIMIT $Limit OFFSET $Offset";
        queryCmd.Parameters.AddWithValue("$Limit", pageSize);
        queryCmd.Parameters.AddWithValue("$Offset", offset);

        var items = new List<ProcessedImage>();
        using var reader = await queryCmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(MapReaderToEntity(reader));
        }

        return new PaginatedList<ProcessedImage>(items, totalCount, pageIndex, pageSize);
    }

    public async Task<int> GetTotalCountAsync(CancellationToken cancellationToken = default)
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM ProcessedImages";
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(result);
    }

    private static ProcessedImage MapReaderToEntity(SqliteDataReader reader)
    {
        return new ProcessedImage
        {
            Id = reader.GetInt64(0),
            FileName = reader.GetString(1),
            FilePath = reader.GetString(2),
            FileSize = reader.GetInt64(3),
            Width = reader.IsDBNull(4) ? null : reader.GetInt32(4),
            Height = reader.IsDBNull(5) ? null : reader.GetInt32(5),
            Format = reader.IsDBNull(6) ? null : reader.GetString(6),
            SHA256 = reader.IsDBNull(7) ? null : reader.GetString(7),
            ThumbnailPath = reader.IsDBNull(8) ? null : reader.GetString(8),
            Status = Enum.TryParse<ProcessingStatus>(reader.GetString(9), out var status) ? status : ProcessingStatus.Failed,
            ErrorMessage = reader.IsDBNull(10) ? null : reader.GetString(10),
            CreatedAt = DateTime.Parse(reader.GetString(11), null, System.Globalization.DateTimeStyles.RoundtripKind),
            ProcessedAt = DateTime.Parse(reader.GetString(12), null, System.Globalization.DateTimeStyles.RoundtripKind)
        };
    }
}
