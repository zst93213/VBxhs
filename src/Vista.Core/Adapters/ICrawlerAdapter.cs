using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Vista.Core.Adapters.Models;
using Vista.Core.Models;

namespace Vista.Core.Adapters
{
    /// <summary>
    /// 读取侧适配器（Crawler）。所有方法只读、可幂等、可缓存。
    /// </summary>
    public interface ICrawlerAdapter
    {
        /// <summary>当前账号的首页/关注信息流。</summary>
        Task<PagedResult<PostCard>> GetHomeTimelineAsync(AccountId account, string cursor, CancellationToken ct);

        /// <summary>搜索笔记/微博。sort: popular|latest|image|video。</summary>
        Task<PagedResult<PostCard>> SearchPostsAsync(AccountId account, string keyword, string sort, string cursor, CancellationToken ct);

        /// <summary>读取单条详情。含正文、图片/视频、互动数据、标签。</summary>
        Task<PostDetail> GetPostDetailAsync(AccountId account, string postId, CancellationToken ct);

        /// <summary>评论列表，支持楼中楼子回复游标。</summary>
        Task<PagedResult<Comment>> GetCommentsAsync(AccountId account, string postId, string cursor, CancellationToken ct);

        /// <summary>用户主页信息。</summary>
        Task<UserProfile> GetUserProfileAsync(AccountId account, string userId, CancellationToken ct);

        /// <summary>当前账号的收藏列表。</summary>
        Task<PagedResult<PostCard>> GetFavoritesAsync(AccountId account, string cursor, CancellationToken ct);

        /// <summary>限流/账号健康自检。批量操作前必跑。</summary>
        Task<AccountHealth> CheckAccountHealthAsync(AccountId account, CancellationToken ct);

        /// <summary>热搜榜。</summary>
        Task<IReadOnlyList<HotSearchItem>> GetHotSearchAsync(AccountId account, CancellationToken ct);

        /// <summary>热门微博（推荐流）。</summary>
        Task<PagedResult<PostCard>> GetHotWeiboAsync(AccountId account, string cursor, CancellationToken ct);

        /// <summary>指定用户发布的微博列表。</summary>
        Task<PagedResult<PostCard>> GetUserPostsAsync(AccountId account, string userId, string cursor, CancellationToken ct);

        /// <summary>指定用户的粉丝列表。</summary>
        Task<PagedResult<UserProfile>> GetUserFollowersAsync(AccountId account, string userId, string cursor, CancellationToken ct);

        /// <summary>指定用户的关注列表。</summary>
        Task<PagedResult<UserProfile>> GetUserFollowingAsync(AccountId account, string userId, string cursor, CancellationToken ct);

        /// <summary>超话信息流（指定超话的帖子列表）。</summary>
        Task<PagedResult<PostCard>> GetSuperTopicFeedAsync(AccountId account, string superTopicId, string cursor, CancellationToken ct);
    }
}
