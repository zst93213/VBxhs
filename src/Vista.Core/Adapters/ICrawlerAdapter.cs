using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Vista.Core.Adapters.Models;
using Vista.Core.Models;

namespace Vista.Core.Adapters
{
    /// <summary>
    /// 读取侧适配器（Crawler）。所有方法只读、可幂等、可缓存。
    /// 用例粒度参考 redbook CLI：search / feed / read / comments / user / favorites / boards / health。
    /// 每个方法对应一个 CLI 命令，便于将来直接暴露为命令行或 MCP 工具。
    /// </summary>
    public interface ICrawlerAdapter
    {
        /// <summary>当前账号的首页/关注信息流（redbook: feed）。</summary>
        Task<PagedResult<PostCard>> GetHomeTimelineAsync(AccountId account, string cursor, CancellationToken ct);

        /// <summary>搜索笔记/微博（redbook: search）。sort: popular|latest|image|video。</summary>
        Task<PagedResult<PostCard>> SearchPostsAsync(AccountId account, string keyword, string sort, string cursor, CancellationToken ct);

        /// <summary>读取单条详情（redbook: read）。含正文、图片/视频、互动数据、标签。</summary>
        Task<PostDetail> GetPostDetailAsync(AccountId account, string postId, CancellationToken ct);

        /// <summary>评论列表（redbook: comments），支持楼中楼子回复游标。</summary>
        Task<PagedResult<Comment>> GetCommentsAsync(AccountId account, string postId, string cursor, CancellationToken ct);

        /// <summary>用户主页信息 + 已发布内容游标（redbook: user / user-posts）。</summary>
        Task<UserProfile> GetUserProfileAsync(AccountId account, string userId, CancellationToken ct);

        /// <summary>当前账号的收藏列表（redbook: favorites）。</summary>
        Task<PagedResult<PostCard>> GetFavoritesAsync(AccountId account, string cursor, CancellationToken ct);

        /// <summary>
        /// 限流/账号健康自检（redbook: health）。
        /// 返回账号是否被隐形限流、剩余配额等。批量操作前必跑。
        /// </summary>
        Task<AccountHealth> CheckAccountHealthAsync(AccountId account, CancellationToken ct);
    }
}
