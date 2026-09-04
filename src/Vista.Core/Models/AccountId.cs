using System;

namespace Vista.Core.Models
{
    /// <summary>
    /// 账号唯一标识（值对象）。一个账号 = 平台 + 该平台内的用户 UID。
    /// 用 struct + 显式字段，避免引用类型在多账号切换时被意外共享。
    /// </summary>
    public readonly struct AccountId : IEquatable<AccountId>
    {
        public PlatformId Platform { get; }
        public string Uid { get; }

        public AccountId(PlatformId platform, string uid)
        {
            if (string.IsNullOrWhiteSpace(uid))
                throw new ArgumentException("uid 不能为空", nameof(uid));
            Platform = platform;
            Uid = uid;
        }

        /// <summary>稳定的字符串化形式，作为缓存键、日志脱敏后的标识。</summary>
        public string Key => Platform + ":" + Uid;

        public bool Equals(AccountId other) => Platform == other.Platform && Uid == other.Uid;
        public override bool Equals(object obj) => obj is AccountId other && Equals(other);
        public override int GetHashCode() => unchecked((int)Platform * 397 ^ (Uid?.GetHashCode() ?? 0));
        public override string ToString() => "[" + PlatformIds.DisplayName(Platform) + "] " + Uid;

        public static bool operator ==(AccountId a, AccountId b) => a.Equals(b);
        public static bool operator !=(AccountId a, AccountId b) => !a.Equals(b);
    }
}
