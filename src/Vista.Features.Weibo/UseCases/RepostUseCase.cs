using System.Threading;
using System.Threading.Tasks;
using Vista.Accounts;
using Vista.Core.Adapters;
using Vista.Core.Models;

namespace Vista.Features.Weibo.UseCases
{
    public sealed class RepostUseCase
    {
        private readonly IPlatformAdapter _adapter;
        private readonly AccountContext _accountContext;

        public RepostUseCase(IPlatformAdapter adapter, AccountContext accountContext)
        {
            _adapter = adapter;
            _accountContext = accountContext;
        }

        public Task<bool> ExecuteAsync(string postId, string comment = "", CancellationToken ct = default)
        {
            var account = _accountContext.EnsureCurrent();
            return _adapter.RepostAsync(account, postId, comment, ct);
        }
    }
}
