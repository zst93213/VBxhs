using System;
using System.Collections.Generic;
using System.Linq;
using Vista.Core.Models;
using Vista.Accounts.Vault;

namespace Vista.Accounts
{
    /// <summary>
    /// 账号仓库。设计计划 §五"账号分组/快速切换/多账号并行"的数据层落地。
    /// 凭证存取走 SecureCredentialVault（DPAPI），元数据走内存索引（M0 简化，
    /// 后续 M3 接 SQLite）。
    /// </summary>
    public sealed class AccountRepository
    {
        private readonly SecureCredentialVault _vault;
        private readonly Dictionary<string, AccountInfo> _meta = new Dictionary<string, AccountInfo>(StringComparer.Ordinal);
        private readonly Dictionary<string, byte[]> _creds = new Dictionary<string, byte[]>(StringComparer.Ordinal);

        public AccountRepository(SecureCredentialVault vault)
        {
            _vault = vault ?? throw new ArgumentNullException(nameof(vault));
        }

        /// <summary>注册一个账号。credential 为原始凭证（Cookie/Token）字节，内部加密存储。</summary>
        public void Register(AccountInfo info, byte[] credential)
        {
            if (info == null) throw new ArgumentNullException(nameof(info));
            var key = info.ToAccountId().Key;
            _meta[key] = info;
            _creds[key] = _vault.Protect(credential ?? new byte[0]);
        }

        public IReadOnlyList<AccountInfo> List() => _meta.Values.ToList();

        public IReadOnlyList<AccountInfo> ListByGroup(string groupId) =>
            _meta.Values.Where(a => a.GroupId == groupId).ToList();

        public AccountInfo Get(AccountId id) => _meta.TryGetValue(id.Key, out var v) ? v : null;

        /// <summary>取出该账号的解密凭证（仅在调用方使用时短暂持有）。</summary>
        public byte[] GetCredential(AccountId id)
        {
            return _creds.TryGetValue(id.Key, out var cipher) ? _vault.Unprotect(cipher) : null;
        }

        /// <summary>注销：删除凭证与元数据，并清空该账号缓存（缓存清理在 Infrastructure 注入）。</summary>
        public bool Revoke(AccountId id)
        {
            var removed = _meta.Remove(id.Key);
            _creds.Remove(id.Key);
            return removed;
        }
    }
}
