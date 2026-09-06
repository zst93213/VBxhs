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
        /// <summary>点赞（微博: attitudes/create）。</summary>
        Task<bool> LikeAsync(AccountId account, string postId, CancellationToken ct);

        /// <summary>取消点赞。</summary>
        Task<bool> UnlikeAsync(AccountId account, string postId, CancellationToken ct);

        /// <summary>收藏到默认收藏夹（微博: starred/create）。</summary>
        Task<bool> FavoriteAsync(AccountId account, string postId, CancellationToken ct);

        /// <summary>取消收藏。</summary>
        Task<bool> UnfavoriteAsync(AccountId account, string postId, CancellationToken ct);

        /// <summary>关注用户（微博: friendships/create）。</summary>
        Task<bool> FollowAsync(AccountId account, string userId, CancellationToken ct);

        /// <summary>发布评论（微博: comments/create）。</summary>
        Task<Comment> CommentAsync(AccountId account, string postId, string content, string replyToCommentId, CancellationToken ct);

        /// <summary>发布内容（微博：纯文本/九宫格/视频）。</summary>
        Task<string> PublishAsync(AccountId account, PublishRequest request, CancellationToken ct);

        /// <summary>转发（微博: statuses/repost）。</summary>
        Task<bool> RepostAsync(AccountId account, string postId, string comment, CancellationToken ct);

        /// <summary>超话签到（微博: /api/page/button 或超话签到接口）。返回签到结果（连续天数等）。</summary>
        Task<SuperTopicSignInResult> SignInSuperTopicAsync(AccountId account, string superTopicId, CancellationToken ct);

        /// <summary>取消关注用户（微博: friendships/destroy）。</summary>
        Task<bool> UnfollowAsync(AccountId account, string userId, CancellationToken ct);
    }
}
