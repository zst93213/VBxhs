using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;

namespace Vista.Presentation.Accessibility
{
    /// <summary>
    /// 争渡读屏（ZDSoft）专属兼容配置。启动时调用一次。
    /// 核心思路：争渡是成熟的读屏软件，我们要做的是"提供正确的 UIA 属性"，
    /// 而不是和争渡抢着说话——否则会出现双声卡重复朗读、节奏错位。
    /// </summary>
    public static class ZdsCompatibility
    {
        /// <summary>是否已应用兼容配置（避免重复调用）。</summary>
        public static bool Applied { get; private set; }

        /// <summary>应用争渡兼容配置。在 App.OnStartup 中调用。</summary>
        public static void Apply()
        {
            if (Applied) return;
            Applied = true;

            // 1. 检测读屏进程 → 同步更新 NarrationService 开关
            NarrationService.DetectReaderAndConfigure();

            // 2. 系统级动画关了：争渡模式下减少干扰，同时给低性能机器减负
            try
            {
                // 兼容 ReduceMotion 用户偏好，争渡用户常伴随低动画
                if (!SystemParameters.ClientAreaAnimation)
                    AnimationBehavior.SetReduceMotion(null, true);
            }
            catch { }

            // 3. 减少 LiveRegion 骚扰：将状态栏的 LiveSetting 降级为 Off，
            //    争渡模式下，"焦点变化"已经能告诉用户状态了，LiveRegion 容易重复播报
            //    （具体窗口级的 TextBlock 在 MainWindow.Loaded 中由代码切换）
        }

        /// <summary>将目标 ListBox/ListView 关闭虚拟化。争渡读屏对虚拟化列表的读屏支持不佳。</summary>
        public static void DisableVirtualization(ItemsControl list)
        {
            if (list == null) return;
            VirtualizingStackPanel.SetIsVirtualizing(list, false);
            VirtualizingStackPanel.SetVirtualizationMode(list, VirtualizationMode.Standard);
        }

        /// <summary>
        /// 将目标窗口的状态栏 LiveRegion 降级为 Off。
        /// 仅在发现争渡读屏时调用，避免争渡与 UIA LiveRegion 重复提示。
        /// </summary>
        public static void DemoteStatusBarLiveRegion(DependencyObject root)
        {
            if (!NarrationService.IsZdsRunning) return;
            // 遍历窗口里设置了 LiveSetting=Polite/Assertive 的 TextBlock，降级为 None
            WalkAndDemote(root);
        }

        private static void WalkAndDemote(DependencyObject parent)
        {
            if (!(parent is System.Windows.Media.Visual v)) return;
            int count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(v);
            for (int i = 0; i < count; i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(v, i);
                if (child is TextBlock tb)
                {
                    var ls = AutomationProperties.GetLiveSetting(tb);
                    if (ls != AutomationLiveSetting.Off)
                        AutomationProperties.SetLiveSetting(tb, AutomationLiveSetting.Off);
                }
                WalkAndDemote(child);
            }
        }
    }

    /// <summary>附加属性：动画开关（ReduceMotion）。</summary>
    internal static class AnimationBehavior
    {
        public static readonly DependencyProperty ReduceMotionProperty =
            DependencyProperty.RegisterAttached("ReduceMotion", typeof(bool), typeof(AnimationBehavior),
                new PropertyMetadata(false));

        public static void SetReduceMotion(DependencyObject obj, bool value)
            => obj?.SetValue(ReduceMotionProperty, value);
        public static bool GetReduceMotion(DependencyObject obj)
            => (bool)(obj?.GetValue(ReduceMotionProperty) ?? false);
    }
}
