using System.Threading;
using System.Threading.Tasks;
using Vista.Accounts;
using Vista.Core;
using Vista.Core.Adapters;
using Vista.Core.Adapters.Models;
using Vista.Core.Models;

namespace Vista.Features.Xiaohongshu.UseCases
{
    /// <summary>小红书点赞 / 取消点赞。</summary>
    public sealed class LikeNoteUseCase
    {
        private readonly IPlatformAdapter _adapter;
        private readonly AccountContext _ctx;
        public LikeNoteUseCase(IPlatformAdapter adapter, AccountContext ctx) { _adapter = adapter; _ctx = ctx; }
        public Task<bool> ExecuteAsync(string noteId, CancellationToken ct = default)
            => _adapter.LikeAsync(_ctx.EnsureCurrent(), noteId, ct);
    }

    public sealed class UnlikeNoteUseCase
    {
        private readonly IPlatformAdapter _adapter;
        private readonly AccountContext _ctx;
        public UnlikeNoteUseCase(IPlatformAdapter adapter, AccountContext ctx) { _adapter = adapter; _ctx = ctx; }
        public Task<bool> ExecuteAsync(string noteId, CancellationToken ct = default)
            => _adapter.UnlikeAsync(_ctx.EnsureCurrent(), noteId, ct);
    }

    /// <summary>小红书收藏 / 取消收藏。</summary>
    public sealed class FavoriteNoteUseCase
    {
        private readonly IPlatformAdapter _adapter;
        private readonly AccountContext _ctx;
        public FavoriteNoteUseCase(IPlatformAdapter adapter, AccountContext ctx) { _adapter = adapter; _ctx = ctx; }
        public Task<bool> ExecuteAsync(string noteId, CancellationToken ct = default)
            => _adapter.FavoriteAsync(_ctx.EnsureCurrent(), noteId, ct);
    }

    public sealed class UnfavoriteNoteUseCase
    {
        private readonly IPlatformAdapter _adapter;
        private readonly AccountContext _ctx;
        public UnfavoriteNoteUseCase(IPlatformAdapter adapter, AccountContext ctx) { _adapter = adapter; _ctx = ctx; }
        public Task<bool> ExecuteAsync(string noteId, CancellationToken ct = default)
            => _adapter.UnfavoriteAsync(_ctx.EnsureCurrent(), noteId, ct);
    }

    /// <summary>小红书关注 / 取消关注。</summary>
    public sealed class FollowUserUseCase
    {
        private readonly IPlatformAdapter _adapter;
        private readonly AccountContext _ctx;
        public FollowUserUseCase(IPlatformAdapter adapter, AccountContext ctx) { _adapter = adapter; _ctx = ctx; }
        public Task<bool> ExecuteAsync(string userId, CancellationToken ct = default)
            => _adapter.FollowAsync(_ctx.EnsureCurrent(), userId, ct);
    }

    /// <summary>小红书发评论。</summary>
    public sealed class CommentNoteUseCase
    {
        private readonly IPlatformAdapter _adapter;
        private readonly AccountContext _ctx;
        public CommentNoteUseCase(IPlatformAdapter adapter, AccountContext ctx) { _adapter = adapter; _ctx = ctx; }
        public Task<Comment> ExecuteAsync(string noteId, string content, string replyToCommentId = null, CancellationToken ct = default)
            => _adapter.CommentAsync(_ctx.EnsureCurrent(), noteId, content, replyToCommentId, ct);
    }

    /// <summary>小红书发布笔记。</summary>
    public sealed class PublishNoteUseCase
    {
        private readonly IPlatformAdapter _adapter;
        private readonly AccountContext _ctx;
        public PublishNoteUseCase(IPlatformAdapter adapter, AccountContext ctx) { _adapter = adapter; _ctx = ctx; }
        public Task<string> ExecuteAsync(PublishRequest request, CancellationToken ct = default)
            => _adapter.PublishAsync(_ctx.EnsureCurrent(), request, ct);
    }

    /// <summary>小红书笔记详情。</summary>
    public sealed class GetNoteDetailUseCase
    {
        private readonly IPlatformAdapter _adapter;
        private readonly AccountContext _ctx;
        public GetNoteDetailUseCase(IPlatformAdapter adapter, AccountContext ctx) { _adapter = adapter; _ctx = ctx; }
        public Task<PostDetail> ExecuteAsync(string noteId, CancellationToken ct = default)
            => _adapter.GetPostDetailAsync(_ctx.EnsureCurrent(), noteId, ct);
    }

    /// <summary>小红书用户主页。</summary>
    public sealed class GetUserProfileUseCase
    {
        private readonly IPlatformAdapter _adapter;
        private readonly AccountContext _ctx;
        public GetUserProfileUseCase(IPlatformAdapter adapter, AccountContext ctx) { _adapter = adapter; _ctx = ctx; }
        public Task<UserProfile> ExecuteAsync(string userId, CancellationToken ct = default)
            => _adapter.GetUserProfileAsync(_ctx.EnsureCurrent(), userId, ct);
    }

    /// <summary>小红书收藏列表。</summary>
    public sealed class GetFavoritesUseCase
    {
        private readonly IPlatformAdapter _adapter;
        private readonly AccountContext _ctx;
        public GetFavoritesUseCase(IPlatformAdapter adapter, AccountContext ctx) { _adapter = adapter; _ctx = ctx; }
        public Task<PagedResult<PostCard>> ExecuteAsync(string cursor = null, CancellationToken ct = default)
            => _adapter.GetFavoritesAsync(_ctx.EnsureCurrent(), cursor, ct);
    }

    /// <summary>小红书限流自检。</summary>
    public sealed class CheckAccountHealthUseCase
    {
        private readonly IPlatformAdapter _adapter;
        private readonly AccountContext _ctx;
        public CheckAccountHealthUseCase(IPlatformAdapter adapter, AccountContext ctx) { _adapter = adapter; _ctx = ctx; }
        public Task<AccountHealth> ExecuteAsync(CancellationToken ct = default)
            => _adapter.CheckAccountHealthAsync(_ctx.EnsureCurrent(), ct);
    }
}
