using System;
using System.Threading.Tasks;

namespace Vista.Infrastructure.Cache
{
    /// <summary>
    /// 本地缓存抽象。业务层只依赖此接口，默认 SQLite 实现（设计计划 §九）。
    /// 读接口（ICrawlerAdapter）的结果可缓存以降低请求量；写接口结果不缓存。
    /// </summary>
    public interface ICacheStore
    {
        Task<T> GetAsync<T>(string key) where T : class;
        Task SetAsync<T>(string key, T value, TimeSpan ttl) where T : class;
        Task InvalidateAsync(string key);
    }
}
