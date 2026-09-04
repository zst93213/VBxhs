using Vista.Core.Models;

namespace Vista.Core.Adapters
{
    /// <summary>
    /// 平台适配器统一入口。参考 sinaweibopy 的"统一接口 + 平台 mixin"思路：
    /// 业务层只依赖此接口，平台差异由各 Adapter 实现。新增平台只加一个 Adapter。
    ///
    /// 该接口是"门面"——它组合了 ICrawlerAdapter（读）与 IInteractionAdapter（写）两组能力，
    /// 二分参考自 Xiaohongshu-API 的 Crawler/Interaction 接口矩阵划分，便于按权限/限速差异化。
    /// </summary>
    public interface IPlatformAdapter : ICrawlerAdapter, IInteractionAdapter
    {
        /// <summary>本适配器对应的平台。</summary>
        PlatformId Platform { get; }

        /// <summary>
        /// 校验账号凭证是否仍有效（如 Token 未过期、Cookie 未失效）。
        /// 在账号切换、批量操作前由账号层调用，避免无效账号浪费请求配额。
        /// </summary>
        bool ValidateCredential(AccountId account);
    }
}
