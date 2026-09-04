using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using Vista.Core.Adapters.Models;

namespace Vista.Presentation.Accessibility
{
    /// <summary>
    /// 信息流卡片控件。M1 时间线里每一条都渲染成 CardItem。
    /// 关键点：重写 OnCreateAutomationPeer，让读屏拿到结构化的 CardAutomationPeer，
    /// 而不是退回默认 FrameworkElementAutomationPeer（语义丢失）。
    /// 参考 MS 官方 Custom Automation Peers 指南。
    /// </summary>
    public class CardItem : ContentControl
    {
        // 不重写 DefaultStyleKey：沿用 ContentControl 默认模板（含 ContentPresenter），
        // M1 再用 themes/generic.xaml 提供卡片专属可视化。M0 聚焦无障碍语义。
        /// <summary>绑定的卡片数据。Peer 据此生成朗读文本与结构。</summary>
        public static readonly DependencyProperty PostProperty =
            DependencyProperty.Register(nameof(Post), typeof(PostCard), typeof(CardItem),
                new PropertyMetadata(null, OnPostChanged));

        public PostCard Post
        {
            get => (PostCard)GetValue(PostProperty);
            set => SetValue(PostProperty, value);
        }

        private static void OnPostChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            // 数据变化时通知读屏：触发 LiveRegionChanged，让读屏朗读新卡片
            var peer = UIElementAutomationPeer.FromElement((CardItem)d) as CardAutomationPeer;
            peer?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        }

        protected override AutomationPeer OnCreateAutomationPeer()
            => new CardAutomationPeer(this);
    }
}
