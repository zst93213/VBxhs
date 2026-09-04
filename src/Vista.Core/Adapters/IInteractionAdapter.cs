using System.Threading;
using System.Threading.Tasks;
using Vista.Core.Adapters.Models;
using Vista.Core.Models;

namespace Vista.Core.Adapters
{
    /// <summary>
    /// 写入侧适配器（Interaction）。所有方法有副作用、需限速、需审计。
    /// 与 ICrawlerAdapter 分离，便于：1）按权限差异化（读多写少）；
    /// 2）写操作走独立的速率桶与队列；3）写操作强制经过账号上下文与限流自检。
    /// </summary>
    public interface IInteractionAdapter
    {
        /// <summary>点赞（Xiaohongshu-API: 点赞笔记，自动去重）。</summary>
        Task<bool> LikeAsync(AccountId account, string postId, CancellationToken ct);

        /// <summary>取消点赞。</summary>
        Task<bool> UnlikeAsync(AccountId account, string postId, CancellationToken ct);

        /// <summary>收藏到默认收藏夹（redbook: collect）。</summary>
        Task<bool> FavoriteAsync(AccountId account, string postId, CancellationToken ct);

        /// <summary>取消收藏（redbook: uncollect）。</summary>
        Task<bool> UnfavoriteAsync(AccountId account, string postId, CancellationToken ct);

        /// <summary>关注用户（Xiaohongshu-API: 关注用户，自动去重）。</summary>
        Task<bool> FollowAsync(AccountId account, string userId, CancellationToken ct);

        /// <summary>发布评论（redbook: comment）。</summary>
        Task<Comment> CommentAsync(AccountId account, string postId, string content, string replyToCommentId, CancellationToken ct);

        /// <summary>发布内容（微博：纯文本/九宫格/视频/文章；小红书：图文/视频笔记）。</summary>
        Task<string> PublishAsync(AccountId account, PublishRequest request, CancellationToken ct);

        /// <summary>转发（微博特有，小红书无原生转发，调用方应避免在小红书调用）。</summary>
        Task<bool> RepostAsync(AccountId account, string postId, string comment, CancellationToken ct);
    }
}
