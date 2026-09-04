using System;
using System.Threading;
using System.Threading.Tasks;
using Vista.Accounts;
using Vista.Core;
using Vista.Core.Adapters;
using Vista.Core.Adapters.Models;
using Vista.Core.Models;
using Vista.Infrastructure.Http;

namespace Vista.Adapters.Weibo
{
    /// <summary>
    /// 微博适配器。M0 仅骨架——实现接口结构与限速注入点，真实 HTTP 调用留待 M1。
    /// 签名/限速设计参考 weibo_netcore_sdk（OAuth2）与各开源爬虫的 Cookie 模式。
    /// 每个 AccountId 对应独立的 ResilientHttpClient（防关联，§五）。
    /// </summary>
    public sealed class WeiboAdapter : IPlatformAdapter
    {
        private readonly AccountRepository _accounts;
        private readonly Func<AccountId, ResilientHttpClient> _clientFactory;

        public WeiboAdapter(AccountRepository accounts, Func<AccountId, ResilientHttpClient> clientFactory)
        {
            _accounts = accounts ?? throw new ArgumentNullException(nameof(accounts));
            _clientFactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));
        }

        public PlatformId Platform => PlatformId.Weibo;

        public bool ValidateCredential(AccountId account)
        {
            // M1 实现：取凭证后调用 /account/profile 轻量接口判断是否过期
            return _accounts.GetCredential(account) != null;
        }

        public Task<PagedResult<PostCard>> GetHomeTimelineAsync(AccountId account, string cursor, CancellationToken ct)
            => throw new NotImplementedException("M1 实现：调用 friends/timeline 或 home/v2/index");

        public Task<PagedResult<PostCard>> SearchPostsAsync(AccountId account, string keyword, string sort, string cursor, CancellationToken ct)
            => throw new NotImplementedException("M1 实现：调用 search/topics 或 search/finder");

        public Task<PostDetail> GetPostDetailAsync(AccountId account, string postId, CancellationToken ct)
            => throw new NotImplementedException("M1 实现：调用 statuses/show");

        public Task<PagedResult<Comment>> GetCommentsAsync(AccountId account, string postId, string cursor, CancellationToken ct)
            => throw new NotImplementedException("M1 实现：调用 comments/show");

        public Task<UserProfile> GetUserProfileAsync(AccountId account, string userId, CancellationToken ct)
            => throw new NotImplementedException("M1 实现：调用 users/show");

        public Task<PagedResult<PostCard>> GetFavoritesAsync(AccountId account, string cursor, CancellationToken ct)
            => throw new NotImplementedException("M1 实现：调用 favorites/list");

        public Task<AccountHealth> CheckAccountHealthAsync(AccountId account, CancellationToken ct)
            => throw new NotImplementedException("M1 实现：调用轻量接口探测限流");

        public Task<bool> LikeAsync(AccountId account, string postId, CancellationToken ct)
            => throw new NotImplementedException("M3 实现：调用 attitudes/create");

        public Task<bool> UnlikeAsync(AccountId account, string postId, CancellationToken ct)
            => throw new NotImplementedException("M3 实现：调用 attitudes/destroy");

        public Task<bool> FavoriteAsync(AccountId account, string postId, CancellationToken ct)
            => throw new NotImplementedException("M3 实现：调用 favorites/create");

        public Task<bool> UnfavoriteAsync(AccountId account, string postId, CancellationToken ct)
            => throw new NotImplementedException("M3 实现：调用 favorites/destroy");

        public Task<bool> FollowAsync(AccountId account, string userId, CancellationToken ct)
            => throw new NotImplementedException("M3 实现：调用 friendships/create");

        public Task<Comment> CommentAsync(AccountId account, string postId, string content, string replyToCommentId, CancellationToken ct)
            => throw new NotImplementedException("M3 实现：调用 comments/create");

        public Task<string> PublishAsync(AccountId account, PublishRequest request, CancellationToken ct)
            => throw new NotImplementedException("M3 实现：statuses/update(文本) / upload(九宫格) / article(长文)");

        public Task<bool> RepostAsync(AccountId account, string postId, string comment, CancellationToken ct)
            => throw new NotImplementedException("M3 实现：statuses/repost");
    }
}
