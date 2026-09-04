using System;
using System.Threading;
using System.Threading.Tasks;
using Vista.Accounts;
using Vista.Core;
using Vista.Core.Adapters;
using Vista.Core.Adapters.Models;
using Vista.Core.Models;
using Vista.Infrastructure.Http;

namespace Vista.Adapters.Xiaohongshu
{
    /// <summary>
    /// 小红书适配器。M0 仅骨架。
    /// 签名（a1 / web_id / x-s / x-t）参考 Spider_XHS 逆向结果；接口矩阵划分
    /// （Crawler 读 / Interaction 写）参考 Xiaohongshu-API 的 22+ 端点分类。
    /// 瀑布流在无障碍模式下由业务层切换为单列大卡（设计计划 §七）。
    /// </summary>
    public sealed class XiaohongshuAdapter : IPlatformAdapter
    {
        private readonly AccountRepository _accounts;
        private readonly Func<AccountId, ResilientHttpClient> _clientFactory;

        public XiaohongshuAdapter(AccountRepository accounts, Func<AccountId, ResilientHttpClient> clientFactory)
        {
            _accounts = accounts ?? throw new ArgumentNullException(nameof(accounts));
            _clientFactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));
        }

        public PlatformId Platform => PlatformId.Xiaohongshu;

        public bool ValidateCredential(AccountId account) => _accounts.GetCredential(account) != null;

        public Task<PagedResult<PostCard>> GetHomeTimelineAsync(AccountId account, string cursor, CancellationToken ct)
            => throw new NotImplementedException("M2 实现：homefeed（关注） / explore（发现）");

        public Task<PagedResult<PostCard>> SearchPostsAsync(AccountId account, string keyword, string sort, string cursor, CancellationToken ct)
            => throw new NotImplementedException("M2 实现：search/notes（排序：popular|latest|image|video）");

        public Task<PostDetail> GetPostDetailAsync(AccountId account, string postId, CancellationToken ct)
            => throw new NotImplementedException("M2 实现：note/detail（含图片/视频/标签）");

        public Task<PagedResult<Comment>> GetCommentsAsync(AccountId account, string postId, string cursor, CancellationToken ct)
            => throw new NotImplementedException("M2 实现：comment/list + sub_comment/list（楼中楼）");

        public Task<UserProfile> GetUserProfileAsync(AccountId account, string userId, CancellationToken ct)
            => throw new NotImplementedException("M2 实现：user/othername 或 user/self");

        public Task<PagedResult<PostCard>> GetFavoritesAsync(AccountId account, string cursor, CancellationToken ct)
            => throw new NotImplementedException("M2 实现：favorite/list/user");

        public Task<AccountHealth> CheckAccountHealthAsync(AccountId account, CancellationToken ct)
            => throw new NotImplementedException("M2 实现：参考 redbook health，探测隐藏 level 字段");

        public Task<bool> LikeAsync(AccountId account, string postId, CancellationToken ct)
            => throw new NotImplementedException("M3 实现：note/like（自动去重）");

        public Task<bool> UnlikeAsync(AccountId account, string postId, CancellationToken ct)
            => throw new NotImplementedException("M3 实现：note/dislike");

        public Task<bool> FavoriteAsync(AccountId account, string postId, CancellationToken ct)
            => throw new NotImplementedException("M3 实现：note/favorite（redbook collect）");

        public Task<bool> UnfavoriteAsync(AccountId account, string postId, CancellationToken ct)
            => throw new NotImplementedException("M3 实现：note/dislike_favorite（redbook uncollect）");

        public Task<bool> FollowAsync(AccountId account, string userId, CancellationToken ct)
            => throw new NotImplementedException("M3 实现：user/follow（自动去重）");

        public Task<Comment> CommentAsync(AccountId account, string postId, string content, string replyToCommentId, CancellationToken ct)
            => throw new NotImplementedException("M3 实现：comment/post + comment/sub");

        public Task<string> PublishAsync(AccountId account, PublishRequest request, CancellationToken ct)
            => throw new NotImplementedException("M3 实现：note/create（图文） / video/create（视频）");

        public Task<bool> RepostAsync(AccountId account, string postId, string comment, CancellationToken ct)
            => throw new NotImplementedException("小红书无原生转发；调用方应避免调用。");
    }
}
