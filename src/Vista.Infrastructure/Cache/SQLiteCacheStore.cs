using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System.Data.SQLite;

namespace Vista.Infrastructure.Cache
{
    public sealed class SQLiteCacheStore : ICacheStore
    {
        private readonly string _connectionString;
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            WriteIndented = false,
            PropertyNameCaseInsensitive = true
        };

        public SQLiteCacheStore(string dbPath)
        {
            if (string.IsNullOrWhiteSpace(dbPath))
                dbPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Vista", "cache.db");

            var dir = Path.GetDirectoryName(dbPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            _connectionString = $"Data Source={dbPath};Version=3;";
            EnsureSchema();
        }

        private void EnsureSchema()
        {
            using var conn = new SQLiteConnection(_connectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
CREATE TABLE IF NOT EXISTS CacheEntries (
    Key TEXT PRIMARY KEY,
    Value TEXT NOT NULL,
    CreatedAt INTEGER NOT NULL,
    ExpiresAt INTEGER
);
CREATE INDEX IF NOT EXISTS IX_CacheEntries_ExpiresAt ON CacheEntries(ExpiresAt);
";
            cmd.ExecuteNonQuery();
        }

        public async Task<T> GetAsync<T>(string key) where T : class
        {
            using var conn = new SQLiteConnection(_connectionString);
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT Value, ExpiresAt FROM CacheEntries WHERE Key = @key";
            cmd.Parameters.AddWithValue("@key", key);

            using var reader = await cmd.ExecuteReaderAsync();
            if (!await reader.ReadAsync()) return null;

            var expiresAt = reader.IsDBNull(1) ? (long?)null : reader.GetInt64(1);
            if (expiresAt.HasValue && DateTimeOffset.UtcNow.ToUnixTimeSeconds() > expiresAt.Value)
            {
                await InvalidateAsync(key);
                return null;
            }

            var json = reader.GetString(0);
            return JsonSerializer.Deserialize<T>(json, JsonOptions);
        }

        public async Task SetAsync<T>(string key, T value, TimeSpan ttl) where T : class
        {
            var json = JsonSerializer.Serialize(value, JsonOptions);
            long? expiresAt = ttl == Timeout.InfiniteTimeSpan ? null
                : DateTimeOffset.UtcNow.Add(ttl).ToUnixTimeSeconds();

            using var conn = new SQLiteConnection(_connectionString);
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
INSERT OR REPLACE INTO CacheEntries (Key, Value, CreatedAt, ExpiresAt)
VALUES (@key, @value, @createdAt, @expiresAt);
";
            cmd.Parameters.AddWithValue("@key", key);
            cmd.Parameters.AddWithValue("@value", json);
            cmd.Parameters.AddWithValue("@createdAt", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            cmd.Parameters.AddWithValue("@expiresAt", (object?)expiresAt ?? DBNull.Value);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task InvalidateAsync(string key)
        {
            using var conn = new SQLiteConnection(_connectionString);
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM CacheEntries WHERE Key = @key";
            cmd.Parameters.AddWithValue("@key", key);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task CleanupExpiredAsync()
        {
            using var conn = new SQLiteConnection(_connectionString);
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM CacheEntries WHERE ExpiresAt IS NOT NULL AND ExpiresAt < @now";
            cmd.Parameters.AddWithValue("@now", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            await cmd.ExecuteNonQueryAsync();
        }
    }
}
