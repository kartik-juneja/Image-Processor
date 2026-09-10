---
trigger: always_on
---

# Coding Agent Skill — Image Processor

## General

- Write clean, production-quality C#/.NET code.
- Prefer simple, maintainable solutions over over-engineering.
- Follow SOLID principles pragmatically.
- Keep classes and methods focused on a single responsibility.
- Use descriptive names; avoid abbreviations and generic names like `data`, `obj`, `manager`.
- Prefer immutable records/models where appropriate.
- Do not duplicate logic; extract reusable logic into focused services.
- Keep business logic separate from CLI, database, and infrastructure concerns.

## Architecture

Use clear separation:

- `Cli` → command parsing and user output only.
- `Application` → business/application orchestration.
- `Domain` → entities, enums, and core models.
- `Infrastructure` → SQLite, filesystem, image library, external implementations.
- `Worker/Processing` → concurrency and pipeline orchestration.
- `Tests` → unit and integration tests.

Use dependency injection instead of manually constructing services.

Depend on interfaces where an abstraction provides real value. Avoid unnecessary interfaces and generic abstractions.

## Async & Concurrency

- Use `async/await` for I/O-bound operations.
- Pass `CancellationToken` through async call chains.
- Never create one task/thread per input item for large workloads.
- Use bounded concurrency and backpressure.
- Prefer `System.Threading.Channels` for producer/consumer pipelines.
- Keep shared mutable state to a minimum.
- Use `Interlocked`, concurrent collections, or other thread-safe mechanisms when required.
- Do not use `lock` around large or slow operations.

## Memory & Resources

- Never load large files completely into memory unless there is a specific reason.
- Prefer streaming APIs for large files.
- Do not place large `byte[]` or image objects in queues.
- Dispose `Stream`, image, database, and other `IDisposable` resources correctly.
- Use `using` / `await using`.
- Keep object lifetimes as short as practical.

## Database

- Keep SQL/data-access code inside Infrastructure.
- Always use parameterized SQL.
- Use transactions for related writes.
- Batch writes where appropriate.
- Avoid uncontrolled concurrent database writes.
- Add indexes based on actual query patterns.
- Keep repository methods focused and explicit.
- Never put SQL directly inside CLI commands or domain classes.

## Error Handling

- Handle expected per-item failures without terminating the complete processing pipeline.
- Never silently swallow exceptions.
- Log useful context when handling failures.
- Do not use `catch (Exception) { }`.
- Treat `OperationCanceledException` separately from normal failures.
- Preserve useful error information for failed operations.
- Fail fast for invalid application configuration or startup conditions.

## Logging

Use `ILogger<T>` or the project's logging abstraction.

- Prefer structured logging.
- Log lifecycle events, important failures, cancellation, and meaningful progress.
- Avoid excessive logging inside tight loops.
- Never log sensitive information.

Example:

```csharp
_logger.LogInformation(
    "Processed {ProcessedCount} of {TotalCount} images",
    processedCount,
    totalCount);