namespace Vista.Core
{
    /// <summary>
    /// 平台标识。新增平台只需在此枚举加一项，并在 Vista.Adapters 实现对应 Adapter。
    /// 设计上保持稳定字符串化，便于序列化与跨进程传递（如 WinAppDriver 测试）。
    /// </summary>
    public enum PlatformId
    {
        /// <summary>新浪微博</summary>
        Weibo = 1,

        /// <summary>小红书 / RedNote</summary>
        Xiaohongshu = 2,

        // 预留接入点（M5 路线图提及）：抖音、B 站
    }

    /// <summary>平台元数据辅助方法。</summary>
    public static class PlatformIds
    {
        /// <summary>平台中文显示名，用于读屏朗读与 UI 展示。</summary>
        public static string DisplayName(PlatformId p)
        {
            switch (p)
            {
                case PlatformId.Weibo: return "微博";
                case PlatformId.Xiaohongshu: return "小红书";
                default: return p.ToString();
            }
        }
    }
}
