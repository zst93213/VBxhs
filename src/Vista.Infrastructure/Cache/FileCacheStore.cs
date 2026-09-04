using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace Vista.Infrastructure.Cache
{
    public sealed class FileCacheStore : ICacheStore
    {
        private readonly string _cacheDir;
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            WriteIndented = false,
            PropertyNameCaseInsensitive = true
        };

        public FileCacheStore(string cacheDir = null)
        {
            _cacheDir = cacheDir ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Vista", "cache");
            if (!Directory.Exists(_cacheDir))
                Directory.CreateDirectory(_cacheDir);
        }

        private string FilePath(string key)
        {
            var safeKey = string.Join("_",
                key.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
            return Path.Combine(_cacheDir, safeKey + ".json");
        }

        private sealed class CacheEntry
        {
            public string Value { get; set; }
            public long? ExpiresAt { get; set; }
        }

        public Task<T> GetAsync<T>(string key) where T : class
        {
            try
            {
                var path = FilePath(key);
                if (!File.Exists(path)) return Task.FromResult<T>(null);

                var entryJson = File.ReadAllText(path);
                var entry = JsonSerializer.Deserialize<CacheEntry>(entryJson, JsonOptions);
                if (entry == null) return Task.FromResult<T>(null);

                if (entry.ExpiresAt.HasValue && DateTimeOffset.UtcNow.ToUnixTimeSeconds() > entry.ExpiresAt.Value)
                {
                    try { File.Delete(path); } catch { }
                    return Task.FromResult<T>(null);
                }

                var result = JsonSerializer.Deserialize<T>(entry.Value, JsonOptions);
                return Task.FromResult(result);
            }
            catch
            {
                return Task.FromResult<T>(null);
            }
        }

        public Task SetAsync<T>(string key, T value, TimeSpan ttl) where T : class
        {
            var valueJson = JsonSerializer.Serialize(value, JsonOptions);
            long? expiresAt = ttl == Timeout.InfiniteTimeSpan ? null
                : DateTimeOffset.UtcNow.Add(ttl).ToUnixTimeSeconds();

            var entry = new CacheEntry
            {
                Value = valueJson,
                ExpiresAt = expiresAt
            };

            try
            {
                File.WriteAllText(FilePath(key), JsonSerializer.Serialize(entry, JsonOptions));
            }
            catch { }
            return Task.CompletedTask;
        }

        public Task InvalidateAsync(string key)
        {
            try { File.Delete(FilePath(key)); } catch { }
            return Task.CompletedTask;
        }
    }
}
