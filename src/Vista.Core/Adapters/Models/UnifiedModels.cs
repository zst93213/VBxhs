using System;
using System.Collections.Generic;

namespace Vista.Core.Adapters.Models
{
    /// <summary>
    /// 信息流卡片（统一模型）。微博"微博"与小红书"笔记"在卡片层抽象为同构：
    /// 都有作者、时间、正文摘要、媒体、互动数。读屏按字段分段朗读。
    /// </summary>
    public sealed class PostCard
    {
        public string Id { get; set; }
        public string AuthorName { get; set; }
        public string AuthorId { get; set; }
        public string AuthorAvatar { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public string TextSummary { get; set; }
        public MediaType Media { get; set; }
        public int LikeCount { get; set; }
        public int CommentCount { get; set; }
        public int RepostCount { get; set; }
        public int CollectCount { get; set; }
        public bool IsVideo { get; set; }

        /// <summary>读屏朗读用的聚合文本（供 NarrationService 使用）。</summary>
        public string SpokenLabel =>
            AuthorName + " 于 " + CreatedAt.LocalDateTime.ToString("MM-dd HH:mm") +
            " 发布：" + TextSummary +
            "，点赞 " + LikeCount + "，评论 " + CommentCount;
    }

    /// <summary>详情，含完整正文与媒体清单。</summary>
    public sealed class PostDetail
    {
        public string Id { get; set; }
        public string FullText { get; set; }
        public IReadOnlyList<string> ImageUrls { get; set; }
        public string VideoUrl { get; set; }
        public IReadOnlyList<string> Tags { get; set; }
        public PostCard Card { get; set; }
    }

    public enum MediaType { Text, Images, Video, Article }

    /// <summary>评论（含楼中楼结构）。</summary>
    public sealed class Comment
    {
        public string Id { get; set; }
        public string PostId { get; set; }
        public string AuthorName { get; set; }
        public string AuthorId { get; set; }
        public string Content { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public int LikeCount { get; set; }
        public string ParentCommentId { get; set; } // null=一级评论
    }

    public sealed class UserProfile
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Avatar { get; set; }
        public string Bio { get; set; }
        public int FollowCount { get; set; }
        public int FollowerCount { get; set; }
        public int PostCount { get; set; }
    }

    /// <summary>账号健康度（限流自检结果，参考 redbook health）。</summary>
    public sealed class AccountHealth
    {
        public bool IsLimited { get; set; }
        public string Level { get; set; } // 平台返回的隐藏 level 字段
        public int RemainingQuota { get; set; }
        public DateTimeOffset CheckedAt { get; set; }
        public string SpokenSummary => IsLimited
            ? "账号疑似被限流，建议暂停发布"
            : "账号状态正常，剩余配额 " + RemainingQuota;
    }

    /// <summary>发布请求（统一）。各 Adapter 按平台规则映射字段。</summary>
    public sealed class PublishRequest
    {
        public string Text { get; set; }
        public IReadOnlyList<string> ImagePaths { get; set; }
        public string VideoPath { get; set; }
        public IReadOnlyList<string> Tags { get; set; }
        public string Location { get; set; }
        public Visibility Visibility { get; set; }
    }

    public enum Visibility { Public, Friends, Private }
}
