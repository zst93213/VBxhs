using System;
using System.Collections.Generic;
using Vista.Core;
using Vista.Core.Models;

namespace Vista.Accounts
{
    /// <summary>
    /// 一条已登录账号的元数据。凭证（Cookie/Token）单独存于 SecureCredentialVault，
    /// 此处只保留展示与分组用信息，便于在不解密凭证的情况下渲染账号 Chip 栏。
    /// </summary>
    public sealed class AccountInfo
    {
        public PlatformId Platform { get; set; }
        public string Uid { get; set; }
        public string DisplayName { get; set; }
        public string AvatarUrl { get; set; }
        public string GroupId { get; set; } // 所属分组（工作号/个人号/客户号）
        public DateTimeOffset LastLoginAt { get; set; }
        public bool IsTokenExpired { get; set; }

        public AccountId ToAccountId() => new AccountId(Platform, Uid);
    }

    /// <summary>账号分组。</summary>
    public sealed class AccountGroup
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public int Order { get; set; }
    }
}
