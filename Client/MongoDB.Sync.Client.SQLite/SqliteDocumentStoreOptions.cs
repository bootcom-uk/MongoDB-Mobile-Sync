namespace MongoDB.Sync.Client.SQLite;

public sealed class SqliteDocumentStoreOptions
{
    /// <summary>
    /// Specifies the file path for the SQLite database. This is a required option and must be provided when configuring the SqliteDocumentStore. The database file will be created at this location if it does not already exist. Ensure that the application has appropriate permissions to read and write to this path, and consider using a location that is suitable for your application's data storage needs (e.g., app-specific directories on mobile platforms).
    /// </summary>
    public required string DatabasePath { get; init; }

    /// <summary>
    /// Enable Write-Ahead Logging for better concurrency and performance. WAL mode is generally recommended for mobile sync scenarios, but may not be suitable for all environments. If disabled, the database will use the default rollback journal mode, which may have lower performance and concurrency. WAL mode allows multiple readers to access the database while a writer is active, improving performance in multi-threaded scenarios. However, it may not be compatible with certain file systems or backup strategies. Consider your application's specific requirements and environment when deciding whether to enable WAL mode.
    /// </summary>
    public bool EnableWal { get; init; } = true;

    /// <summary>
    /// Recommended: "NORMAL" for mobile sync. "FULL" if you prefer max durability.
    /// </summary>
    public string Synchronous { get; init; } = "NORMAL";

    /// <summary>
    /// Rough cache target (KB). Negative values mean "KB" in SQLite.
    /// Default is ~20MB.
    /// </summary>
    public int CacheSizeKb { get; init; } = 20_000;
}