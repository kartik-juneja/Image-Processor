# Large-Scale Image Processor — .NET 8 Console EXE

A production-minded, high-performance .NET 8 console application designed to process large directories of image files efficiently with **bounded memory usage**, **controlled concurrency**, **resilient error handling**, and **reliable SQLite persistence**.

---

## 🏗️ Architectural Overview

The application is structured using a clean, layered architecture to maintain strict separation of concerns, testability, and maintainability:

```text
src/
├── ImageProcessor.Domain/          # Core Domain entities (ProcessedImage, ProcessingJob), Enums, Models
├── ImageProcessor.Application/     # Application Services, Interfaces, DTOs, and Configuration Options
├── ImageProcessor.Infrastructure/  # SQLite Repositories (WAL mode), SixLabors.ImageSharp Extractor & Generator
├── ImageProcessor.Processing/      # Bounded Channels Pipeline, Worker Pool, Single-Writer DB Batcher
└── ImageProcessor.Cli/             # Console Entry Point, DI Setup, Command Handlers & Formatters

tests/
└── ImageProcessor.Tests/           # Unit & Integration Tests (xUnit)

tools/
└── DatasetGenerator/               # Utility tool to generate sample image datasets for testing
```

---

## ⚡ Quick Start & Running Instructions

### Prerequisites
* [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) installed on your system.

### Step 1: (Optional) Generate Sample Image Dataset
You can use the built-in `DatasetGenerator` tool to generate sample image files (valid images, corrupted files, and duplicates) inside `./ImageProcessor/data/images/`:

```bash
dotnet run --project ImageProcessor/tools/DatasetGenerator/DatasetGenerator.csproj -- 50
```
*(Replace `50` with the number of test images you wish to generate).*

---

### Step 2: Start Processing Images
To process images in the input directory with parallel worker threads:

```bash
# Run with default worker count (system CPU logical core count)
dotnet run --project ImageProcessor/src/ImageProcessor.Cli/ImageProcessor.Cli.csproj -- process

# Run with custom worker count (e.g. 4 workers)
dotnet run --project ImageProcessor/src/ImageProcessor.Cli/ImageProcessor.Cli.csproj -- process --workers 4
```

---

### Step 3: Query & Manage Results

```bash
# Check current or last job status and processing metrics
dotnet run --project ImageProcessor/src/ImageProcessor.Cli/ImageProcessor.Cli.csproj -- status

# List processed images (paginated)
dotnet run --project ImageProcessor/src/ImageProcessor.Cli/ImageProcessor.Cli.csproj -- list --page 1 --size 10

# Search processed images by file name or format
dotnet run --project ImageProcessor/src/ImageProcessor.Cli/ImageProcessor.Cli.csproj -- search --name sample --format PNG

# View detailed info for a specific image by ID (includes duplicate detection)
dotnet run --project ImageProcessor/src/ImageProcessor.Cli/ImageProcessor.Cli.csproj -- info --id 1

# View thumbnail path for an image ID
dotnet run --project ImageProcessor/src/ImageProcessor.Cli/ImageProcessor.Cli.csproj -- thumbnail --id 1

# Send cancellation signal to a running job
dotnet run --project ImageProcessor/src/ImageProcessor.Cli/ImageProcessor.Cli.csproj -- cancel
```

---

### Step 4: Run Automated Tests

Execute the full xUnit test suite:

```bash
dotnet test ImageProcessor/tests/ImageProcessor.Tests/ImageProcessor.Tests.csproj
```

---

## ⚡ Concurrency & Pipeline Architecture

Processing thousands of large image files requires controlled concurrency to prevent CPU exhaustion and out-of-memory errors:

```text
  [Directory.EnumerateFiles] (Lazy Producer)
              │
              ▼
   ┌───────────────────────┐  Bounded Channel<string>
   │  Work Queue (500 max) │  (Backpressure applies when queue is full)
   └──────────┬────────────┘
              │
    ┌─────────┼─────────┐
    ▼         ▼         ▼
┌────────┐┌────────┐┌────────┐
│Worker 1││Worker 2││Worker N│  (N Concurrent Workers, configurable)
└───┬────┘└───┬────┘└───┬────┘  - Computes SHA-256 via Stream
    │         │         │       - Header-only Metadata via Image.IdentifyAsync
    └─────────┼─────────┘       - Resizes 300x300 Thumbnail to ./data/thumbnails/
              │
              ▼
   ┌───────────────────────┐  Bounded Channel<ProcessedImage>
   │ Result Queue(500 max) │
   └──────────┬────────────┘
              │
              ▼
   ┌───────────────────────┐
   │  SQLite Batch Writer  │  Single-threaded DB Consumer
   └───────────────────────┘  - Batch inserts inside SqliteTransaction (100 items/batch)
                              - Updates live job progress metrics
```

### Key Concurrency Highlights:
1. **Bounded Queues (`System.Threading.Channels`)**: Queue capacity is fixed (e.g., 500 items). If workers slow down, the file producer pauses (`WriteAsync` backpressure), preventing unbounded memory growth.
2. **Worker Pool (`N` Workers)**: Configurable worker count (defaults to `Environment.ProcessorCount`). No thread-per-file allocation.
3. **Single-Writer DB Batching**: All workers push results to a single SQLite batch writer task. This eliminates `SQLITE_BUSY` database lock contention under concurrent load.

---

## 💾 Memory Management Strategy

Designed specifically for handling large files (e.g. >20 MB) and massive directories:

- **Header-Only Metadata Extraction**: Uses `SixLabors.ImageSharp.Image.IdentifyAsync` to read image format, width, and height directly from stream headers **without decoding pixel buffers into memory**.
- **Streaming SHA-256 Hashes**: Hashes are computed using `SHA256.HashDataAsync` on file streams with 81,920 byte buffers without loading entire byte arrays.
- **Lazy Directory Streaming**: Uses `Directory.EnumerateFiles()` instead of `GetFiles()`, streaming paths as needed without loading all filenames into memory.
- **Resource Disposal**: All streams and ImageSharp objects are wrapped in `using` statements for immediate disposal.

---

## 🗄️ SQLite Persistence & Schema

SQLite is configured with **Write-Ahead Logging (WAL)** mode for optimal concurrent read/write throughput:

```sql
PRAGMA journal_mode=WAL;
PRAGMA synchronous=NORMAL;
```

### Table: `ProcessedImages`
| Column | Type | Description |
| :--- | :--- | :--- |
| `Id` | `INTEGER PRIMARY KEY` | Auto-increment primary key |
| `FileName` | `TEXT NOT NULL` | Name of the file |
| `FilePath` | `TEXT NOT NULL UNIQUE` | Absolute/relative file path |
| `FileSize` | `INTEGER NOT NULL` | Size in bytes |
| `Width` | `INTEGER` | Image width in pixels |
| `Height` | `INTEGER` | Image height in pixels |
| `Format` | `TEXT` | Decoded format (PNG, JPEG, etc.) |
| `SHA256` | `TEXT` | Hex SHA-256 hash for duplicate detection |
| `ThumbnailPath` | `TEXT` | Path to generated 300x300 thumbnail |
| `Status` | `TEXT NOT NULL` | `Success`, `Failed`, `Skipped` |
| `ErrorMessage` | `TEXT` | Error details if status is `Failed` |
| `CreatedAt` | `TEXT NOT NULL` | File creation timestamp |
| `ProcessedAt` | `TEXT NOT NULL` | Processing timestamp |

### Indexes:
- `idx_images_sha256`: Fast duplicate detection queries
- `idx_images_filename`: Fast name filter searches
- `idx_images_format`: Fast format filter searches
- `idx_images_status`: Status queries

---

## 🛡️ Resilience & Error Handling

- **Item-Level Failures**: If an image is corrupted, unreadable, or missing, the error is caught by `ImageWorker`, recorded with `Status = Failed` and an error description in SQLite, allowing the job to continue without crashing.
- **Concurrent Duplicate Thumbnail Idempotency**: If identical files are processed concurrently by two workers, atomic temporary file creation ensures zero file-lock collisions during thumbnail generation.
- **Cancellation**: Listens to `CancellationToken` (Ctrl+C) as well as database-driven cancellation requests via the `cancel` subcommand.

---

## 🖥️ CLI Commands Reference

Build and run the executable:

```bash
dotnet run --project src/ImageProcessor.Cli/ImageProcessor.Cli.csproj -- <command> [options]
```

| Command | Options | Description |
| :--- | :--- | :--- |
| `process` | `--workers N` | Starts image processing in `./data/images/` with N concurrent workers. Prints live progress. |
| `status` | None | Displays current/last job execution status and metrics. |
| `list` | `--page N --size N` | Displays paginated list of processed images. |
| `search` | `--name X --format X` | Filters processed images by file name substring or image format. |
| `info` | `--id X` | Displays full details for an image ID and identifies matching duplicate images. |
| `thumbnail` | `--id X` | Prints the generated thumbnail file path for an image ID. |
| `cancel` | None | Sends a cancellation request to an active running job. |

---

## 🧪 Automated Testing

Run the full xUnit automated test suite:

```bash
dotnet test tests/ImageProcessor.Tests/ImageProcessor.Tests.csproj
```

### Included Tests:
- `MetadataExtractorTests`: Validates format, width, and height extraction; handles invalid data.
- `DuplicateDetectionTests`: Tests SHA-256 computation and duplicate grouping.
- `CorruptImageHandlingTests`: Verifies corrupted image recording without pipeline crash.
- `PaginationAndSearchTests`: Validates SQLite pagination and name/format filtering.
- `ConcurrencyPipelineTests`: Tests bounded channel pipeline under high concurrent load with multiple workers.
- `CancellationTests`: Verifies pipeline cancels gracefully on token signal.

---

## 🚀 Scaling Analysis: Handling 1,000,000 Images

To scale the architecture from 5,000 images to **1,000,000+ images**:

1. **Distributed Queue Architecture**: Replace in-memory `System.Threading.Channels` with a distributed message queue like **RabbitMQ**, **Apache Kafka**, or **AWS SQS**.
2. **Object Storage**: Store original images and thumbnails in S3/MinIO rather than local filesystem paths to avoid inode and OS directory listing limits.
3. **Database Sharding / PostgreSQL**: Transition from single-file SQLite to a clustered **PostgreSQL**, indexing SHA-256 hashes with partition keys.
4. **Horizontal Worker Scaling**: Deploy workers as stateless containerized tasks (Docker / K8s / AWS ECS) scaling dynamically based on queue depth.
