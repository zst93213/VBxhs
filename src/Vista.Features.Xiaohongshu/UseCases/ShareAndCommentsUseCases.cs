using System.Threading;
using System.Threading.Tasks;
using Vista.Accounts;
using Vista.Core.Adapters;
using Vista.Core.Adapters.Models;
using Vista.Core.Models;

namespace Vista.Features.Xiaohongshu.UseCases
{
    /// <summary>
    /// 小红书分享用例（功能等价微博 Repost）。
    /// 小红书没有原生"转发"，因此实际行为是：
    ///   1) 生成分享卡片（标题+正文摘要+链接），通过 Copy 到剪贴板
    ///   2) 或调用 RepostAsync（小红书 Adapter 的默认实现会写系统剪贴板）
    /// 提供"一键分享"的一致交互，避免用户在双平台之间切换时重新理解功能。
    /// </summary>
    public sealed class ShareNoteUseCase
    {
        private readonly IPlatformAdapter _adapter;
        private readonly AccountContext _accountContext;

        public ShareNoteUseCase(IPlatformAdapter adapter, AccountContext accountContext)
        {
            _adapter = adapter;
            _accountContext = accountContext;
        }

        /// <summary>分享一篇笔记。comment 作为分享时附带的文案。</summary>
        public Task<bool> ExecuteAsync(string noteId, string comment = "", CancellationToken ct = default)
        {
            var account = _accountContext.EnsureCurrent();
            // 小红书 Adapter.RepostAsync 会处理成"分享到剪贴板"的形式，返回是否成功
            return _adapter.RepostAsync(account, noteId, comment, ct);
        }
    }

    /// <summary>评论区朗读/操作扩展用例（M0 占位，M3 接真实 Adapter）。</summary>
    public sealed class GetCommentsUseCase
    {
        private readonly IPlatformAdapter _adapter;
        private readonly AccountContext _accountContext;

        public GetCommentsUseCase(IPlatformAdapter adapter, AccountContext accountContext)
        {
            _adapter = adapter;
            _accountContext = accountContext;
        }

        public Task<PagedResult<Comment>> ExecuteAsync(string postId, string cursor, CancellationToken ct = default)
        {
            var account = _accountContext.EnsureCurrent();
            return _adapter.GetCommentsAsync(account, postId, cursor, ct);
        }
    }
}
