using System;
using Vista.Core.Models;

namespace Vista.Accounts
{
    /// <summary>
    /// 当前账号上下文。业务层据此分发请求到对应平台 Adapter（设计计划 §三第 2/4 层衔接）。
    /// 切换账号只换此上下文，不重启窗口（§五"快速切换"）。
    /// </summary>
    public sealed class AccountContext
    {
        private AccountId? _current;

        public AccountId? Current => _current;

        public event EventHandler<AccountId> CurrentChanged;

        /// <summary>切换当前账号。</summary>
        public void SwitchTo(AccountId id)
        {
            if (_current == id) return;
            _current = id;
            CurrentChanged?.Invoke(this, id);
        }

        /// <summary>清空当前账号（退出登录全部账号时）。</summary>
        public void Clear()
        {
            _current = null;
            CurrentChanged?.Invoke(this, default);
        }

        /// <summary>确保有当前账号，否则抛异常——写操作前调用。</summary>
        public AccountId EnsureCurrent()
        {
            if (!_current.HasValue)
                throw new InvalidOperationException("尚未选择当前账号，无法执行操作。");
            return _current.Value;
        }
    }
}
