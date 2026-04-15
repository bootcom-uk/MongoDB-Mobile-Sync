using Microsoft.Data.Sqlite;
using MongoDB.Sync.Client.Interfaces;
using System.Globalization;

namespace MongoDB.Sync.Client.SQLite;

public sealed class SqliteDocumentStore : ILocalDocumentStore
{
    private readonly SqliteDocumentStoreOptions _options;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    private SqliteConnection? _connection;

    public SqliteDocumentStore(SqliteDocumentStoreOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public async Task InitializeAsync(CancellationToken token = default)
    {
        if (_connection != null)
            return;

        var cs = new SqliteConnectionStringBuilder
        {
            DataSource = _options.DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        }.ToString();

        _connection = new SqliteConnection(cs);
        await _connection.OpenAsync(token).ConfigureAwait(false);

        await ApplyPragmasAsync(_connection, token).ConfigureAwait(false);
        await EnsureSchemaAsync(_connection, token).ConfigureAwait(false);
    }

    public async Task ApplyBatchAsync(IReadOnlyList<ISyncDocument> documents, CancellationToken token = default)
    {
        if (documents == null) throw new ArgumentNullException(nameof(documents));
        if (documents.Count == 0) return;

        var conn = RequireConnection();

        await _writeLock.WaitAsync(token).ConfigureAwait(false);
        try
        {
            await using var tx = (SqliteTransaction) await conn.BeginTransactionAsync(token).ConfigureAwait(false);

            await using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"
INSERT INTO SyncDocuments (Collection, Id, Json, UpdatedAt, IsDeleted)
VALUES ($collection, $id, $json, $updatedAt, $isDeleted)
ON CONFLICT(Collection, Id) DO UPDATE SET
    Json      = excluded.Json,
    UpdatedAt = excluded.UpdatedAt,
    IsDeleted = excluded.IsDeleted;
";

            var pCollection = cmd.CreateParameter(); pCollection.ParameterName = "$collection"; cmd.Parameters.Add(pCollection);
            var pId = cmd.CreateParameter(); pId.ParameterName = "$id"; cmd.Parameters.Add(pId);
            var pJson = cmd.CreateParameter(); pJson.ParameterName = "$json"; cmd.Parameters.Add(pJson);
            var pUpdatedAt = cmd.CreateParameter(); pUpdatedAt.ParameterName = "$updatedAt"; cmd.Parameters.Add(pUpdatedAt);
            var pIsDeleted = cmd.CreateParameter(); pIsDeleted.ParameterName = "$isDeleted"; cmd.Parameters.Add(pIsDeleted);

            foreach (var d in documents)
            {
                if (d == null) continue;

                if (string.IsNullOrWhiteSpace(d.Collection))
                    throw new ArgumentException("ISyncDocument.Collection is required.");
                if (string.IsNullOrWhiteSpace(d.Id))
                    throw new ArgumentException("ISyncDocument.Id is required.");
                if (d.Json == null)
                    throw new ArgumentException("ISyncDocument.Json is required.");

                pCollection.Value = d.Collection;
                pId.Value = d.Id;
                pJson.Value = d.Json;

                // Store as ISO 8601 UTC ("O") so string order == date order.
                pUpdatedAt.Value = d.UpdatedAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
                pIsDeleted.Value = d.IsDeleted ? 1 : 0;

                await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            }

            await tx.CommitAsync(token).ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task<ISyncDocument?> GetByIdAsync(string collection, string id, bool includeDeleted = false, CancellationToken token = default)
    {
        var conn = RequireConnection();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT Collection, Id, Json, UpdatedAt, IsDeleted
FROM SyncDocuments
WHERE Collection = $collection
  AND Id = $id
" + (includeDeleted ? "" : "  AND IsDeleted = 0") + @"
LIMIT 1;
";
        cmd.Parameters.AddWithValue("$collection", collection);
        cmd.Parameters.AddWithValue("$id", id);

        await using var reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false);
        if (!await reader.ReadAsync(token).ConfigureAwait(false))
            return null;

        return ReadSyncDocument(reader);
    }

    public async Task<IReadOnlyList<ISyncDocument>> GetByIdsAsync(string collection, IReadOnlyList<string> ids, bool includeDeleted = false, CancellationToken token = default)
    {
        if (ids == null) throw new ArgumentNullException(nameof(ids));
        if (ids.Count == 0) return Array.Empty<ISyncDocument>();

        var conn = RequireConnection();

        // parameterised IN clause
        var paramNames = new string[ids.Count];
        for (int i = 0; i < ids.Count; i++)
            paramNames[i] = "$id" + i;

        var sql = $@"
SELECT Collection, Id, Json, UpdatedAt, IsDeleted
FROM SyncDocuments
WHERE Collection = $collection
  AND Id IN ({string.Join(",", paramNames)})
" + (includeDeleted ? "" : "  AND IsDeleted = 0") + @";
";

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("$collection", collection);
        for (int i = 0; i < ids.Count; i++)
            cmd.Parameters.AddWithValue(paramNames[i], ids[i]);

        var list = new List<ISyncDocument>(ids.Count);
        await using var reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false);
        while (await reader.ReadAsync(token).ConfigureAwait(false))
            list.Add(ReadSyncDocument(reader));

        return list;
    }

    public async Task<IReadOnlyList<ISyncDocument>> GetCollectionAsync(string collection, bool includeDeleted = false, CancellationToken token = default)
    {
        var conn = RequireConnection();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT Collection, Id, Json, UpdatedAt, IsDeleted
FROM SyncDocuments
WHERE Collection = $collection
" + (includeDeleted ? "" : "  AND IsDeleted = 0") + @"
ORDER BY UpdatedAt ASC;
";
        cmd.Parameters.AddWithValue("$collection", collection);

        var list = new List<ISyncDocument>();
        await using var reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false);
        while (await reader.ReadAsync(token).ConfigureAwait(false))
            list.Add(ReadSyncDocument(reader));

        return list;
    }

    public async Task<IReadOnlyList<ISyncDocument>> GetChangesSinceAsync(string collection, DateTime since, bool includeDeleted = true, CancellationToken token = default)
    {
        var conn = RequireConnection();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT Collection, Id, Json, UpdatedAt, IsDeleted
FROM SyncDocuments
WHERE Collection = $collection
  AND UpdatedAt > $since
" + (includeDeleted ? "" : "  AND IsDeleted = 0") + @"
ORDER BY UpdatedAt ASC;
";
        cmd.Parameters.AddWithValue("$collection", collection);
        cmd.Parameters.AddWithValue("$since", since.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));

        var list = new List<ISyncDocument>();
        await using var reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false);
        while (await reader.ReadAsync(token).ConfigureAwait(false))
            list.Add(ReadSyncDocument(reader));

        return list;
    }

    public async Task<ICollectionCheckpoint> GetCheckpointAsync(string collection, CancellationToken token = default)
    {
        var conn = RequireConnection();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT Collection, LastUpdatedAt
FROM SyncCheckpoints
WHERE Collection = $collection
LIMIT 1;
";
        cmd.Parameters.AddWithValue("$collection", collection);

        await using var reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false);
        if (!await reader.ReadAsync(token).ConfigureAwait(false))
        {
            return new SqliteCollectionCheckpoint
            {
                Collection = collection,
                LastUpdatedAt = null
            };
        }

        var lastStr = reader.IsDBNull(1) ? null : reader.GetString(1);
        DateTime? last = null;

        if (!string.IsNullOrWhiteSpace(lastStr) &&
            DateTime.TryParse(lastStr, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dt))
        {
            last = dt.ToUniversalTime();
        }

        return new SqliteCollectionCheckpoint
        {
            Collection = reader.GetString(0),
            LastUpdatedAt = last
        };
    }

    public async Task SetCheckpointAsync(ICollectionCheckpoint checkpoint, CancellationToken token = default)
    {
        if (checkpoint == null) throw new ArgumentNullException(nameof(checkpoint));

        var conn = RequireConnection();

        await _writeLock.WaitAsync(token).ConfigureAwait(false);
        try
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
INSERT INTO SyncCheckpoints (Collection, LastUpdatedAt)
VALUES ($collection, $lastUpdatedAt)
ON CONFLICT(Collection) DO UPDATE SET
    LastUpdatedAt = excluded.LastUpdatedAt;
";
            cmd.Parameters.AddWithValue("$collection", checkpoint.Collection);
            cmd.Parameters.AddWithValue("$lastUpdatedAt",
                checkpoint.LastUpdatedAt?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));

            await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task ClearAsync(CancellationToken token = default)
    {
        var conn = RequireConnection();

        await _writeLock.WaitAsync(token).ConfigureAwait(false);
        try
        {
            await using var tx = (SqliteTransaction) await conn.BeginTransactionAsync(token).ConfigureAwait(false);

            await using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = "DELETE FROM SyncDocuments;";
                await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            }

            await using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = "DELETE FROM SyncCheckpoints;";
                await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            }

            await tx.CommitAsync(token).ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection != null)
        {
            await _connection.DisposeAsync().ConfigureAwait(false);
            _connection = null;
        }

        _writeLock.Dispose();
    }

    // -----------------------
    // Internals
    // -----------------------

    private SqliteConnection RequireConnection()
        => _connection ?? throw new InvalidOperationException("SqliteDocumentStore is not initialized. Call InitializeAsync() first.");

    private async Task ApplyPragmasAsync(SqliteConnection conn, CancellationToken token)
    {
        await using var cmd = conn.CreateCommand();

        // Explicit, predictable behaviour.
        // cache_size: negative => KB
        cmd.CommandText = $@"
PRAGMA foreign_keys = ON;
PRAGMA temp_store = MEMORY;
PRAGMA cache_size = -{Math.Max(1000, _options.CacheSizeKb)};
PRAGMA synchronous = {_options.Synchronous};
" + (_options.EnableWal ? "PRAGMA journal_mode = WAL;\n" : "");

        await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
    }

    private static async Task EnsureSchemaAsync(SqliteConnection conn, CancellationToken token)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
CREATE TABLE IF NOT EXISTS SyncDocuments (
    Collection TEXT NOT NULL,
    Id         TEXT NOT NULL,
    Json       TEXT NOT NULL,
    UpdatedAt  TEXT NOT NULL,
    IsDeleted  INTEGER NOT NULL,
    PRIMARY KEY (Collection, Id)
);

-- Fast 'diff/resume' and 'changes since' reads
CREATE INDEX IF NOT EXISTS IX_SyncDocuments_Collection_UpdatedAt
ON SyncDocuments (Collection, UpdatedAt);

-- Checkpoints per collection
CREATE TABLE IF NOT EXISTS SyncCheckpoints (
    Collection    TEXT NOT NULL PRIMARY KEY,
    LastUpdatedAt TEXT NULL
);
";
        await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
    }

    private static ISyncDocument ReadSyncDocument(SqliteDataReader reader)
    {
        // Column order: Collection, Id, Json, UpdatedAt, IsDeleted
        var collection = reader.GetString(0);
        var id = reader.GetString(1);
        var json = reader.GetString(2);
        var updatedAtStr = reader.GetString(3);
        var isDeleted = reader.GetInt32(4) == 1;

        var updatedAt = DateTime.Parse(updatedAtStr, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind).ToUniversalTime();

        return new SqliteSyncDocument
        {
            Collection = collection,
            Id = id,
            Json = json,
            UpdatedAt = updatedAt,
            IsDeleted = isDeleted
        };
    }

    // -----------------------
    // Concrete implementations
    // (live inside provider to avoid circular deps)
    // -----------------------

    internal sealed class SqliteSyncDocument : ISyncDocument
    {
        public required string Id { get; init; }
        public required string Collection { get; init; }
        public required string Json { get; init; }
        public required DateTime UpdatedAt { get; init; }
        public bool IsDeleted { get; init; }
    }

    internal sealed class SqliteCollectionCheckpoint : ICollectionCheckpoint
    {
        public required string Collection { get; init; }
        public DateTime? LastUpdatedAt { get; init; }
    }
}