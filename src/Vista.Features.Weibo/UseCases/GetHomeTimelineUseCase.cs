using System.Threading;
using System.Threading.Tasks;
using Vista.Accounts;
using Vista.Core.Adapters;
using Vista.Core.Adapters.Models;
using Vista.Core.Models;

namespace Vista.Features.Weibo.UseCases
{
    /// <summary>
    /// 用例：拉取微博首页时间线（对应 redbook feed 命令）。
    /// 用例只依赖 IPlatformAdapter 接口与 AccountContext，不知道是微博还是小红书的真实 HTTP。
    /// 通过构造注入指定平台 Adapter，实现"同接口、不同平台"复用。
    /// </summary>
    public sealed class GetHomeTimelineUseCase
    {
        private readonly IPlatformAdapter _adapter;
        private readonly AccountContext _accountContext;

        public GetHomeTimelineUseCase(IPlatformAdapter adapter, AccountContext accountContext)
        {
            _adapter = adapter;
            _accountContext = accountContext;
        }

        public Task<PagedResult<PostCard>> ExecuteAsync(string cursor, CancellationToken ct)
        {
            var account = _accountContext.EnsureCurrent();
            return _adapter.GetHomeTimelineAsync(account, cursor, ct);
        }
    }
}
