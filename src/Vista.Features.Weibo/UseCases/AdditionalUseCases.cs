using System.Threading;
using System.Threading.Tasks;
using Vista.Accounts;
using Vista.Core;
using Vista.Core.Adapters;
using Vista.Core.Adapters.Models;
using Vista.Core.Models;

namespace Vista.Features.Weibo.UseCases
{
    /// <summary>微博点赞（attitudes/create）。</summary>
    public sealed class LikeUseCase
    {
        private readonly IPlatformAdapter _adapter;
        private readonly AccountContext _ctx;
        public LikeUseCase(IPlatformAdapter adapter, AccountContext ctx) { _adapter = adapter; _ctx = ctx; }
        public Task<bool> ExecuteAsync(string postId, CancellationToken ct = default)
            => _adapter.LikeAsync(_ctx.EnsureCurrent(), postId, ct);
    }

    /// <summary>微博取消点赞（attitudes/destroy）。</summary>
    public sealed class UnlikeUseCase
    {
        private readonly IPlatformAdapter _adapter;
        private readonly AccountContext _ctx;
        public UnlikeUseCase(IPlatformAdapter adapter, AccountContext ctx) { _adapter = adapter; _ctx = ctx; }
        public Task<bool> ExecuteAsync(string postId, CancellationToken ct = default)
            => _adapter.UnlikeAsync(_ctx.EnsureCurrent(), postId, ct);
    }

    /// <summary>微博收藏（favorites/create）。</summary>
    public sealed class FavoriteUseCase
    {
        private readonly IPlatformAdapter _adapter;
        private readonly AccountContext _ctx;
        public FavoriteUseCase(IPlatformAdapter adapter, AccountContext ctx) { _adapter = adapter; _ctx = ctx; }
        public Task<bool> ExecuteAsync(string postId, CancellationToken ct = default)
            => _adapter.FavoriteAsync(_ctx.EnsureCurrent(), postId, ct);
    }

    /// <summary>微博取消收藏。</summary>
    public sealed class UnfavoriteUseCase
    {
        private readonly IPlatformAdapter _adapter;
        private readonly AccountContext _ctx;
        public UnfavoriteUseCase(IPlatformAdapter adapter, AccountContext ctx) { _adapter = adapter; _ctx = ctx; }
        public Task<bool> ExecuteAsync(string postId, CancellationToken ct = default)
            => _adapter.UnfavoriteAsync(_ctx.EnsureCurrent(), postId, ct);
    }

    /// <summary>微博关注用户（friendships/create）。</summary>
    public sealed class FollowUseCase
    {
        private readonly IPlatformAdapter _adapter;
        private readonly AccountContext _ctx;
        public FollowUseCase(IPlatformAdapter adapter, AccountContext ctx) { _adapter = adapter; _ctx = ctx; }
        public Task<bool> ExecuteAsync(string userId, CancellationToken ct = default)
            => _adapter.FollowAsync(_ctx.EnsureCurrent(), userId, ct);
    }

    /// <summary>微博发评论（comments/create）。</summary>
    public sealed class CommentUseCase
    {
        private readonly IPlatformAdapter _adapter;
        private readonly AccountContext _ctx;
        public CommentUseCase(IPlatformAdapter adapter, AccountContext ctx) { _adapter = adapter; _ctx = ctx; }
        public Task<Comment> ExecuteAsync(string postId, string content, string replyToCommentId = null, CancellationToken ct = default)
            => _adapter.CommentAsync(_ctx.EnsureCurrent(), postId, content, replyToCommentId, ct);
    }

    /// <summary>微博发布（statuses/update / upload / article）。</summary>
    public sealed class PublishUseCase
    {
        private readonly IPlatformAdapter _adapter;
        private readonly AccountContext _ctx;
        public PublishUseCase(IPlatformAdapter adapter, AccountContext ctx) { _adapter = adapter; _ctx = ctx; }
        public Task<string> ExecuteAsync(PublishRequest request, CancellationToken ct = default)
            => _adapter.PublishAsync(_ctx.EnsureCurrent(), request, ct);
    }

    /// <summary>微博搜索。</summary>
    public sealed class SearchUseCase
    {
        private readonly IPlatformAdapter _adapter;
        private readonly AccountContext _ctx;
        public SearchUseCase(IPlatformAdapter adapter, AccountContext ctx) { _adapter = adapter; _ctx = ctx; }
        public Task<PagedResult<PostCard>> ExecuteAsync(string keyword, string sort = "popular", string cursor = null, CancellationToken ct = default)
            => _adapter.SearchPostsAsync(_ctx.EnsureCurrent(), keyword, sort, cursor, ct);
    }

    /// <summary>微博正文详情（statuses/show）。</summary>
    public sealed class GetPostDetailUseCase
    {
        private readonly IPlatformAdapter _adapter;
        private readonly AccountContext _ctx;
        public GetPostDetailUseCase(IPlatformAdapter adapter, AccountContext ctx) { _adapter = adapter; _ctx = ctx; }
        public Task<PostDetail> ExecuteAsync(string postId, CancellationToken ct = default)
            => _adapter.GetPostDetailAsync(_ctx.EnsureCurrent(), postId, ct);
    }

    /// <summary>微博用户主页（users/show）。</summary>
    public sealed class GetUserProfileUseCase
    {
        private readonly IPlatformAdapter _adapter;
        private readonly AccountContext _ctx;
        public GetUserProfileUseCase(IPlatformAdapter adapter, AccountContext ctx) { _adapter = adapter; _ctx = ctx; }
        public Task<UserProfile> ExecuteAsync(string userId, CancellationToken ct = default)
            => _adapter.GetUserProfileAsync(_ctx.EnsureCurrent(), userId, ct);
    }

    /// <summary>微博收藏列表。</summary>
    public sealed class GetFavoritesUseCase
    {
        private readonly IPlatformAdapter _adapter;
        private readonly AccountContext _ctx;
        public GetFavoritesUseCase(IPlatformAdapter adapter, AccountContext ctx) { _adapter = adapter; _ctx = ctx; }
        public Task<PagedResult<PostCard>> ExecuteAsync(string cursor = null, CancellationToken ct = default)
            => _adapter.GetFavoritesAsync(_ctx.EnsureCurrent(), cursor, ct);
    }

    /// <summary>微博限流自检。</summary>
    public sealed class CheckAccountHealthUseCase
    {
        private readonly IPlatformAdapter _adapter;
        private readonly AccountContext _ctx;
        public CheckAccountHealthUseCase(IPlatformAdapter adapter, AccountContext ctx) { _adapter = adapter; _ctx = ctx; }
        public Task<AccountHealth> ExecuteAsync(CancellationToken ct = default)
            => _adapter.CheckAccountHealthAsync(_ctx.EnsureCurrent(), ct);
    }
}
