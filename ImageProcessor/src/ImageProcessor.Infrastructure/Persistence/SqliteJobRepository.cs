using ImageProcessor.Application.Interfaces;
using ImageProcessor.Domain.Entities;
using ImageProcessor.Domain.Enums;
using Microsoft.Data.Sqlite;

namespace ImageProcessor.Infrastructure.Persistence;

public class SqliteJobRepository : IJobRepository
{
    private readonly string _connectionString;

    public SqliteJobRepository(string databasePath)
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        };
        _connectionString = builder.ConnectionString;
    }

    public async Task<ProcessingJob?> GetLatestJobAsync(CancellationToken cancellationToken = default)
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT JobId, TotalDiscovered, ProcessedCount, SuccessCount, FailedCount, Status, StartedAt, CompletedAt FROM ProcessingJob ORDER BY rowid DESC LIMIT 1";

        using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (await reader.ReadAsync(cancellationToken))
        {
            return new ProcessingJob
            {
                JobId = reader.GetString(0),
                TotalDiscovered = reader.GetInt32(1),
                ProcessedCount = reader.GetInt32(2),
                SuccessCount = reader.GetInt32(3),
                FailedCount = reader.GetInt32(4),
                Status = Enum.TryParse<JobStatus>(reader.GetString(5), out var status) ? status : JobStatus.Idle,
                StartedAt = DateTime.Parse(reader.GetString(6), null, System.Globalization.DateTimeStyles.RoundtripKind),
                CompletedAt = reader.IsDBNull(7) ? null : DateTime.Parse(reader.GetString(7), null, System.Globalization.DateTimeStyles.RoundtripKind)
            };
        }

        return null;
    }

    public async Task CreateJobAsync(ProcessingJob job, CancellationToken cancellationToken = default)
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        using var command = connection.CreateCommand();
        command.CommandText = @"
            INSERT INTO ProcessingJob (
                JobId, TotalDiscovered, ProcessedCount, SuccessCount, FailedCount, Status, StartedAt, CompletedAt, CancellationRequested
            ) VALUES (
                $JobId, $TotalDiscovered, $ProcessedCount, $SuccessCount, $FailedCount, $Status, $StartedAt, $CompletedAt, 0
            )
            ON CONFLICT(JobId) DO UPDATE SET
                TotalDiscovered=excluded.TotalDiscovered,
                ProcessedCount=excluded.ProcessedCount,
                SuccessCount=excluded.SuccessCount,
                FailedCount=excluded.FailedCount,
                Status=excluded.Status,
                CompletedAt=excluded.CompletedAt;
        ";

        command.Parameters.AddWithValue("$JobId", job.JobId);
        command.Parameters.AddWithValue("$TotalDiscovered", job.TotalDiscovered);
        command.Parameters.AddWithValue("$ProcessedCount", job.ProcessedCount);
        command.Parameters.AddWithValue("$SuccessCount", job.SuccessCount);
        command.Parameters.AddWithValue("$FailedCount", job.FailedCount);
        command.Parameters.AddWithValue("$Status", job.Status.ToString());
        command.Parameters.AddWithValue("$StartedAt", job.StartedAt.ToString("o"));
        command.Parameters.AddWithValue("$CompletedAt", (object?)job.CompletedAt?.ToString("o") ?? DBNull.Value);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task UpdateJobProgressAsync(string jobId, int totalDiscovered, int processedCount, int successCount, int failedCount, CancellationToken cancellationToken = default)
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        using var command = connection.CreateCommand();
        command.CommandText = @"
            UPDATE ProcessingJob SET
                TotalDiscovered = $TotalDiscovered,
                ProcessedCount = $ProcessedCount,
                SuccessCount = $SuccessCount,
                FailedCount = $FailedCount
            WHERE JobId = $JobId
        ";

        command.Parameters.AddWithValue("$JobId", jobId);
        command.Parameters.AddWithValue("$TotalDiscovered", totalDiscovered);
        command.Parameters.AddWithValue("$ProcessedCount", processedCount);
        command.Parameters.AddWithValue("$SuccessCount", successCount);
        command.Parameters.AddWithValue("$FailedCount", failedCount);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task SetJobStatusAsync(string jobId, JobStatus status, CancellationToken cancellationToken = default)
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        using var command = connection.CreateCommand();
        command.CommandText = @"
            UPDATE ProcessingJob SET
                Status = $Status,
                CompletedAt = $CompletedAt
            WHERE JobId = $JobId
        ";

        command.Parameters.AddWithValue("$JobId", jobId);
        command.Parameters.AddWithValue("$Status", status.ToString());
        command.Parameters.AddWithValue("$CompletedAt", status != JobStatus.Running ? DateTime.UtcNow.ToString("o") : (object)DBNull.Value);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task RequestCancellationAsync(CancellationToken cancellationToken = default)
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE ProcessingJob SET CancellationRequested = 1 WHERE Status = 'Running'";
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<bool> IsCancellationRequestedAsync(CancellationToken cancellationToken = default)
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM ProcessingJob WHERE Status = 'Running' AND CancellationRequested = 1";
        var res = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(res) > 0;
    }
}
