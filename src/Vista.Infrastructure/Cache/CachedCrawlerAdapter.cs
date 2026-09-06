using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Vista.Core.Adapters;
using Vista.Core.Adapters.Models;
using Vista.Core.Models;

namespace Vista.Infrastructure.Cache
{
    public sealed class CachedCrawlerAdapter : ICrawlerAdapter
    {
        private readonly ICrawlerAdapter _inner;
        private readonly ICacheStore _cache;
        private readonly TimeSpan _defaultTtl;

        public CachedCrawlerAdapter(ICrawlerAdapter inner, ICacheStore cache, TimeSpan? defaultTtl = null)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));
            _defaultTtl = defaultTtl ?? TimeSpan.FromMinutes(30);
        }

        private static string Key(string method, AccountId account, params object[] parts)
        {
            var key = $"{account.Key}:{method}";
            foreach (var p in parts)
                key += ":" + (p?.ToString() ?? "null");
            return key;
        }

        public async Task<PagedResult<PostCard>> GetHomeTimelineAsync(AccountId account, string cursor, CancellationToken ct)
        {
            var cacheKey = Key("home", account, cursor ?? "root");
            var cached = await _cache.GetAsync<PagedResult<PostCard>>(cacheKey);
            if (cached != null) return cached;

            var fresh = await _inner.GetHomeTimelineAsync(account, cursor, ct);
            if (fresh != null)
                await _cache.SetAsync(cacheKey, fresh, _defaultTtl);
            return fresh;
        }

        public async Task<PagedResult<PostCard>> SearchPostsAsync(AccountId account, string keyword, string sort, string cursor, CancellationToken ct)
        {
            var cacheKey = Key("search", account, keyword, sort, cursor ?? "root");
            var cached = await _cache.GetAsync<PagedResult<PostCard>>(cacheKey);
            if (cached != null) return cached;

            var fresh = await _inner.SearchPostsAsync(account, keyword, sort, cursor, ct);
            if (fresh != null)
                await _cache.SetAsync(cacheKey, fresh, _defaultTtl);
            return fresh;
        }

        public async Task<PostDetail> GetPostDetailAsync(AccountId account, string postId, CancellationToken ct)
        {
            var cacheKey = Key("detail", account, postId);
            var cached = await _cache.GetAsync<PostDetail>(cacheKey);
            if (cached != null) return cached;

            var fresh = await _inner.GetPostDetailAsync(account, postId, ct);
            if (fresh != null)
                await _cache.SetAsync(cacheKey, fresh, _defaultTtl);
            return fresh;
        }

        public async Task<PagedResult<Comment>> GetCommentsAsync(AccountId account, string postId, string cursor, CancellationToken ct)
        {
            var cacheKey = Key("comments", account, postId, cursor ?? "root");
            var cached = await _cache.GetAsync<PagedResult<Comment>>(cacheKey);
            if (cached != null) return cached;

            var fresh = await _inner.GetCommentsAsync(account, postId, cursor, ct);
            if (fresh != null)
                await _cache.SetAsync(cacheKey, fresh, _defaultTtl);
            return fresh;
        }

        public async Task<UserProfile> GetUserProfileAsync(AccountId account, string userId, CancellationToken ct)
        {
            var cacheKey = Key("profile", account, userId);
            var cached = await _cache.GetAsync<UserProfile>(cacheKey);
            if (cached != null) return cached;

            var fresh = await _inner.GetUserProfileAsync(account, userId, ct);
            if (fresh != null)
                await _cache.SetAsync(cacheKey, fresh, _defaultTtl);
            return fresh;
        }

        public async Task<PagedResult<PostCard>> GetFavoritesAsync(AccountId account, string cursor, CancellationToken ct)
        {
            var cacheKey = Key("favorites", account, cursor ?? "root");
            var cached = await _cache.GetAsync<PagedResult<PostCard>>(cacheKey);
            if (cached != null) return cached;

            var fresh = await _inner.GetFavoritesAsync(account, cursor, ct);
            if (fresh != null)
                await _cache.SetAsync(cacheKey, fresh, _defaultTtl);
            return fresh;
        }

        public async Task<AccountHealth> CheckAccountHealthAsync(AccountId account, CancellationToken ct)
        {
            var cacheKey = Key("health", account);
            var cached = await _cache.GetAsync<AccountHealth>(cacheKey);
            if (cached != null) return cached;

            var fresh = await _inner.CheckAccountHealthAsync(account, ct);
            if (fresh != null)
                await _cache.SetAsync(cacheKey, fresh, TimeSpan.FromMinutes(5));
            return fresh;
        }

        public async Task<IReadOnlyList<HotSearchItem>> GetHotSearchAsync(AccountId account, CancellationToken ct)
        {
            var cacheKey = Key("hotsearch", account);
            var cached = await _cache.GetAsync<IReadOnlyList<HotSearchItem>>(cacheKey);
            if (cached != null) return cached;

            var fresh = await _inner.GetHotSearchAsync(account, ct);
            if (fresh != null)
                await _cache.SetAsync(cacheKey, fresh, TimeSpan.FromMinutes(10));
            return fresh;
        }

        public async Task<PagedResult<PostCard>> GetHotWeiboAsync(AccountId account, string cursor, CancellationToken ct)
        {
            var cacheKey = Key("hotweibo", account, cursor ?? "root");
            var cached = await _cache.GetAsync<PagedResult<PostCard>>(cacheKey);
            if (cached != null) return cached;

            var fresh = await _inner.GetHotWeiboAsync(account, cursor, ct);
            if (fresh != null)
                await _cache.SetAsync(cacheKey, fresh, _defaultTtl);
            return fresh;
        }

        public async Task<PagedResult<PostCard>> GetUserPostsAsync(AccountId account, string userId, string cursor, CancellationToken ct)
        {
            var cacheKey = Key("userposts", account, userId, cursor ?? "root");
            var cached = await _cache.GetAsync<PagedResult<PostCard>>(cacheKey);
            if (cached != null) return cached;

            var fresh = await _inner.GetUserPostsAsync(account, userId, cursor, ct);
            if (fresh != null)
                await _cache.SetAsync(cacheKey, fresh, _defaultTtl);
            return fresh;
        }

        public async Task<PagedResult<UserProfile>> GetUserFollowersAsync(AccountId account, string userId, string cursor, CancellationToken ct)
        {
            var cacheKey = Key("followers", account, userId, cursor ?? "root");
            var cached = await _cache.GetAsync<PagedResult<UserProfile>>(cacheKey);
            if (cached != null) return cached;

            var fresh = await _inner.GetUserFollowersAsync(account, userId, cursor, ct);
            if (fresh != null)
                await _cache.SetAsync(cacheKey, fresh, _defaultTtl);
            return fresh;
        }

        public async Task<PagedResult<UserProfile>> GetUserFollowingAsync(AccountId account, string userId, string cursor, CancellationToken ct)
        {
            var cacheKey = Key("following", account, userId, cursor ?? "root");
            var cached = await _cache.GetAsync<PagedResult<UserProfile>>(cacheKey);
            if (cached != null) return cached;

            var fresh = await _inner.GetUserFollowingAsync(account, userId, cursor, ct);
            if (fresh != null)
                await _cache.SetAsync(cacheKey, fresh, _defaultTtl);
            return fresh;
        }

        public async Task<PagedResult<PostCard>> GetSuperTopicFeedAsync(AccountId account, string superTopicId, string cursor, CancellationToken ct)
        {
            var cacheKey = Key("supertopic", account, superTopicId, cursor ?? "root");
            var cached = await _cache.GetAsync<PagedResult<PostCard>>(cacheKey);
            if (cached != null) return cached;

            var fresh = await _inner.GetSuperTopicFeedAsync(account, superTopicId, cursor, ct);
            if (fresh != null)
                await _cache.SetAsync(cacheKey, fresh, _defaultTtl);
            return fresh;
        }
    }
}
