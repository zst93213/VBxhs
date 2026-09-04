using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Vista.Accounts;
using Vista.Core.Adapters;
using Vista.Core.Adapters.Models;
using Vista.Core.Models;

namespace Vista.Features.Weibo.UseCases
{
    public sealed class GetCommentsUseCase
    {
        private readonly IPlatformAdapter _adapter;
        private readonly AccountContext _accountContext;

        public GetCommentsUseCase(IPlatformAdapter adapter, AccountContext accountContext)
        {
            _adapter = adapter;
            _accountContext = accountContext;
        }

        public async Task<IReadOnlyList<Comment>> ExecuteAsync(string postId, CancellationToken ct = default)
        {
            var account = _accountContext.EnsureCurrent();
            var result = await _adapter.GetCommentsAsync(account, postId, null, ct);
            return result?.Items ?? new List<Comment>();
        }
    }
}
