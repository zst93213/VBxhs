using System.Threading;
using System.Threading.Tasks;
using Vista.Accounts;
using Vista.Core.Adapters;
using Vista.Core.Adapters.Models;
using Vista.Core.Models;

namespace Vista.Features.Xiaohongshu.UseCases
{
    /// <summary>
    /// 用例：搜索小红书笔记（对应 redbook search 命令）。
    /// 瀑布流结果由表现层切换为单列大卡（设计计划 §七无障碍模式），
    /// 本用例只负责取数与分页，不关心展示。
    /// </summary>
    public sealed class SearchNotesUseCase
    {
        private readonly IPlatformAdapter _adapter;
        private readonly AccountContext _accountContext;

        public SearchNotesUseCase(IPlatformAdapter adapter, AccountContext accountContext)
        {
            _adapter = adapter;
            _accountContext = accountContext;
        }

        /// <param name="sort">popular|latest|image|video</param>
        public Task<PagedResult<PostCard>> ExecuteAsync(string keyword, string sort, string cursor, CancellationToken ct)
        {
            var account = _accountContext.EnsureCurrent();
            return _adapter.SearchPostsAsync(account, keyword, sort, cursor, ct);
        }
    }
}
